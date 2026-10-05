using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Usage: dndc <assembly.dll> [output.h]");
    return 2;
}

var output = args.Length == 2 ? args[1] : "generated_program.h";
try
{
    var compiled = Compiler.Compile(args[0]);
    File.WriteAllText(output, CEmitter.Emit(compiled));
    Console.WriteLine($"Compiled {args[0]} -> {output} ({compiled.Code.Count} bytes, {compiled.Strings.Count} strings)");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"dndc: {ex.Message}");
    return 1;
}

internal sealed record CompiledProgram(List<byte> Code, List<string> Strings);

internal static class Compiler
{
    private const byte Nop = 0x00, LdcI4 = 0x01, Add = 0x02, Sub = 0x03,
        Mul = 0x04, Div = 0x05, CallInternal = 0x06, Pop = 0x07, Ret = 0x08;
    private const byte WriteLine = 1, WriteInt = 2;

    public static CompiledProgram Compile(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata) throw new InvalidDataException("Input is not a managed .NET assembly.");

        var md = pe.GetMetadataReader();
        var cor = pe.PEHeaders.CorHeader ?? throw new InvalidDataException("Missing CLI header.");
        if (cor.EntryPointTokenOrRelativeVirtualAddress == 0)
            throw new InvalidDataException("Assembly has no managed entry point.");

        var entry = MetadataTokens.EntityHandle(cor.EntryPointTokenOrRelativeVirtualAddress);
        if (entry.Kind != HandleKind.MethodDefinition)
            throw new NotSupportedException("Only MethodDef entry points are supported.");

        var method = md.GetMethodDefinition((MethodDefinitionHandle)entry);
        if (method.RelativeVirtualAddress == 0) throw new InvalidDataException("Entry point has no method body.");
        var body = pe.GetMethodBody(method.RelativeVirtualAddress);
        if (body.ExceptionRegions.Length != 0)
            throw new NotSupportedException("Exception regions are not supported yet.");

        var strings = new List<string>();
        var stringIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var code = new List<byte>();
        var il = body.GetILBytes();
        int p = 0;

        while (p < il.Length)
        {
            int offset = p;
            byte op = il[p++];
            switch (op)
            {
                case 0x00: code.Add(Nop); break;                       // nop
                case 0x15: EmitI4(code, -1); break;                   // ldc.i4.m1
                case >= 0x16 and <= 0x1e: EmitI4(code, op - 0x16); break;
                case 0x1f: Need(il, p, 1, offset); EmitI4(code, (sbyte)il[p++]); break;
                case 0x20:
                    Need(il, p, 4, offset);
                    EmitI4(code, BitConverter.ToInt32(il, p)); p += 4; break;
                case 0x58: code.Add(Add); break;
                case 0x59: code.Add(Sub); break;
                case 0x5a: code.Add(Mul); break;
                case 0x5b: code.Add(Div); break;
                case 0x26: code.Add(Pop); break;
                case 0x72: // ldstr -> string-table index on DND evaluation stack
                {
                    Need(il, p, 4, offset);
                    int token = BitConverter.ToInt32(il, p); p += 4;
                    var handle = MetadataTokens.UserStringHandle(token & 0x00ffffff);
                    string value = md.GetUserString(handle);
                    if (!stringIds.TryGetValue(value, out int id))
                    {
                        id = strings.Count;
                        strings.Add(value);
                        stringIds.Add(value, id);
                    }
                    EmitI4(code, id);
                    break;
                }
                case 0x28: // call
                {
                    Need(il, p, 4, offset);
                    int token = BitConverter.ToInt32(il, p); p += 4;
                    var target = MetadataTokens.EntityHandle(token);
                    var call = ResolveConsoleCall(md, target);
                    code.Add(CallInternal);
                    code.Add(call);
                    break;
                }
                case 0x2a: code.Add(Ret); break;
                default:
                    throw new NotSupportedException($"Unsupported CIL opcode 0x{op:x2} at IL_{offset:x4}.");
            }
        }

        return new CompiledProgram(code, strings);
    }

    private static byte ResolveConsoleCall(MetadataReader md, EntityHandle handle)
    {
        string typeName, typeNamespace, methodName;
        BlobHandle signature;

        if (handle.Kind == HandleKind.MemberReference)
        {
            var member = md.GetMemberReference((MemberReferenceHandle)handle);
            methodName = md.GetString(member.Name);
            signature = member.Signature;
            (typeNamespace, typeName) = GetParentType(md, member.Parent);
        }
        else if (handle.Kind == HandleKind.MethodDefinition)
        {
            var method = md.GetMethodDefinition((MethodDefinitionHandle)handle);
            methodName = md.GetString(method.Name);
            signature = method.Signature;
            var type = md.GetTypeDefinition(method.GetDeclaringType());
            typeName = md.GetString(type.Name);
            typeNamespace = md.GetString(type.Namespace);
        }
        else throw new NotSupportedException($"Unsupported call token kind {handle.Kind}.");

        if (typeNamespace != "System" || typeName != "Console" || methodName != "WriteLine")
            throw new NotSupportedException($"Unsupported method call: {typeNamespace}.{typeName}.{methodName}.");

        var reader = md.GetBlobReader(signature);
        var header = reader.ReadSignatureHeader();
        if (header.IsGeneric) reader.ReadCompressedInteger();
        int parameterCount = reader.ReadCompressedInteger();
        reader.ReadSignatureTypeCode(); // return type
        if (parameterCount != 1) throw new NotSupportedException("Only one-argument Console.WriteLine is supported.");
        var parameter = reader.ReadSignatureTypeCode();
        return parameter switch
        {
            SignatureTypeCode.String => WriteLine,
            SignatureTypeCode.Int32 => WriteInt,
            _ => throw new NotSupportedException($"Console.WriteLine({parameter}) is not supported.")
        };
    }

    private static (string Namespace, string Name) GetParentType(MetadataReader md, EntityHandle parent) =>
        parent.Kind switch
        {
            HandleKind.TypeReference => GetTypeReference(md, (TypeReferenceHandle)parent),
            HandleKind.TypeDefinition => GetTypeDefinition(md, (TypeDefinitionHandle)parent),
            _ => throw new NotSupportedException($"Unsupported member parent {parent.Kind}.")
        };

    private static (string, string) GetTypeReference(MetadataReader md, TypeReferenceHandle h)
    {
        var t = md.GetTypeReference(h);
        return (md.GetString(t.Namespace), md.GetString(t.Name));
    }

    private static (string, string) GetTypeDefinition(MetadataReader md, TypeDefinitionHandle h)
    {
        var t = md.GetTypeDefinition(h);
        return (md.GetString(t.Namespace), md.GetString(t.Name));
    }

    private static void EmitI4(List<byte> code, int value)
    {
        code.Add(LdcI4);
        code.AddRange(BitConverter.GetBytes(value)); // compiler host is little-endian on supported .NET platforms
    }

    private static void Need(byte[] il, int p, int count, int offset)
    {
        if (p + count > il.Length) throw new InvalidDataException($"Truncated CIL at IL_{offset:x4}.");
    }
}

internal static class CEmitter
{
    public static string Emit(CompiledProgram program)
    {
        var b = new StringBuilder();
        b.AppendLine("#ifndef DND_GENERATED_PROGRAM_H");
        b.AppendLine("#define DND_GENERATED_PROGRAM_H");
        b.AppendLine("#include <stdint.h>");
        b.AppendLine();
        b.AppendLine("static const char *dnd_generated_strings[] = {");
        foreach (var s in program.Strings)
            b.Append("    \"").Append(Escape(s)).AppendLine("\",");
        if (program.Strings.Count == 0) b.AppendLine("    0,");
        b.AppendLine("};");
        b.AppendLine($"static const uint32_t dnd_generated_string_count = {program.Strings.Count}u;");
        b.AppendLine();
        b.AppendLine("static const uint8_t dnd_generated_code[] = {");
        for (int i = 0; i < program.Code.Count; i += 12)
        {
            b.Append("    ");
            b.Append(string.Join(", ", program.Code.Skip(i).Take(12).Select(x => $"0x{x:x2}")));
            b.AppendLine(",");
        }
        b.AppendLine("};");
        b.AppendLine($"static const uint32_t dnd_generated_code_size = {program.Code.Count}u;");
        b.AppendLine("#endif");
        return b.ToString();
    }

    private static string Escape(string s) => s
        .Replace("\\", "\\\\").Replace("\"", "\\\"")
        .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
}
