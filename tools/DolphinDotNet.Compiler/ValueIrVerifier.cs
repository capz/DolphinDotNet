namespace DolphinDotNet.Compiler;

internal static class ValueIrVerifier
{
    public static void Verify(ValueIrMethod method)
    {
        foreach(var block in method.Blocks)
        {
            if(block.Terminator is null)throw new InvalidDataException($"Value IR block {block.Id} in {method.Key} has no terminator.");
            foreach(var opaque in block.Instructions.OfType<ValueIrOpaqueStackEffect>())
                throw new NotSupportedException($"Value IR has no semantic lowering for CIL opcode 0x{opaque.OpCode:x4} in {method.Key}.");
        }
    }
}
