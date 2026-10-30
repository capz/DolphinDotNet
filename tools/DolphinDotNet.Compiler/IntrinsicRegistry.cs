using System.Reflection.Metadata;

namespace DolphinDotNet.Compiler;

internal enum IntrinsicKind
{
    None,
    NativeStorage,
    NativeComparison,
    InitializeArray,
    StringFromChars,
    ObjectConstructor,
    ObjectGetHashCode,
    ObjectToString,
    ObjectEquals,
    ObjectStaticEquals,
    StringLength,
    StringCharAt,
    StringEquals,
    StringInstanceEquals,
    StringNotEquals,
    StringStartsWith,
    StringEndsWith,
    StringContains,
    StringIndexOf,
    StringSubstring,
    StringConcat,
    PrimitiveToString,
    PrimitiveToStringProvider,
    ExceptionConstructor,
    ExceptionMessage,
    GameCubeWriteLine,
    GameCubeReadButtonsDown,
    NullableConstructor,
    NullableHasValue,
    NullableValue,
    NullableGetValueOrDefault,
    NullableGetValueOrDefaultValue,
    NullableEquals,
    NullableGetHashCode,
    KeyValuePairConstructor,
    KeyValuePairKey,
    KeyValuePairValue,
    ShapedArrayConstructor, ShapedArrayGet, ShapedArraySet, ShapedArrayAddress,
    ArrayLength,
    ArrayLongLength,
    ArrayRank,
    ArrayGetLength,
    ArrayGetLowerBound,
    ArrayGetUpperBound,
    ArrayClear,
    ArrayCopy,
    ArrayIndexOf,
    ArrayInterfaceCount,
    ArrayInterfaceIsReadOnly,
    ArrayInterfaceItemGet,
    ArrayInterfaceItemSet,
    ArrayInterfaceContains,
    ArrayInterfaceIndexOf,
    ArraySegmentConstructor,
    ArraySegmentArray,
    ArraySegmentOffset,
    ArraySegmentCount,
    ArraySegmentItem,
    ArraySegmentGetEnumerator,
    ArraySegmentEnumeratorMoveNext,
    ArraySegmentEnumeratorCurrent,
    ArraySegmentEnumeratorDispose
}

internal static class IntrinsicRegistry
{
    public static IntrinsicKind Classify(MetadataReader md,EntityHandle handle)
    {
        string? type=null;string? name=null;var genericMethod=false;var parameterCount=-1;string[] parameterTypes=Array.Empty<string>();
        if(handle.Kind==HandleKind.MethodSpecification) return Classify(md,md.GetMethodSpecification((MethodSpecificationHandle)handle).Method);
        if(handle.Kind==HandleKind.MemberReference)
        {
            var member=md.GetMemberReference((MemberReferenceHandle)handle);
            if(member.Parent.Kind==HandleKind.TypeSpecification){
                var arrayReader=md.GetBlobReader(md.GetTypeSpecification((TypeSpecificationHandle)member.Parent).Signature);
                if(arrayReader.ReadSignatureTypeCode()==SignatureTypeCode.Array)return md.GetString(member.Name) switch {
                    ".ctor"=>IntrinsicKind.ShapedArrayConstructor,"Get"=>IntrinsicKind.ShapedArrayGet,"Set"=>IntrinsicKind.ShapedArraySet,"Address"=>IntrinsicKind.ShapedArrayAddress,_=>IntrinsicKind.None};
            }
            type=MetadataLoader.ResolveTypeName(md,member.Parent);name=md.GetString(member.Name);parameterTypes=member.DecodeMethodSignature(new MethodSignatureIdentity(),null).ParameterTypes.ToArray();var sr=md.GetBlobReader(member.Signature);var sh=sr.ReadSignatureHeader();if(sh.IsGeneric){genericMethod=true;sr.ReadCompressedInteger();}parameterCount=sr.ReadCompressedInteger();
        }
        else if(handle.Kind==HandleKind.MethodDefinition)
        {
            var method=md.GetMethodDefinition((MethodDefinitionHandle)handle);
            var declaring=md.GetTypeDefinition(method.GetDeclaringType());
            type=string.IsNullOrEmpty(md.GetString(declaring.Namespace))?md.GetString(declaring.Name):md.GetString(declaring.Namespace)+"."+md.GetString(declaring.Name);
            name=md.GetString(method.Name);
        }
        return (type,name) switch
        {
            ("Dolphin.Numerics.NativeNumeric",_)=>IntrinsicKind.NativeStorage,
            ("Dolphin.Text.NativeText",_)=>IntrinsicKind.NativeStorage,
            ("System.String",".ctor") when (parameterCount is 1 or 3&&parameterTypes.FirstOrDefault()=="Char[]")||(parameterCount==2&&parameterTypes.FirstOrDefault()=="Char")=>IntrinsicKind.StringFromChars,
            ("Dolphin.Graphics.NativeModel",_)=>IntrinsicKind.NativeStorage,
            ("Dolphin.Graphics.NativeGraphics",_)=>IntrinsicKind.NativeStorage,
            ("Dolphin.Graphics.Raw.NativeGX",_)=>IntrinsicKind.NativeStorage,
            ("Dolphin.Input.NativeInput",_)=>IntrinsicKind.NativeStorage,
            ("Dolphin.NativePlatform",_)=>IntrinsicKind.NativeStorage,
            ("Dolphin.Hardware.NativeHardware",_)=>IntrinsicKind.NativeStorage,
            ("Dolphin.Audio.NativeAudio",_)=>IntrinsicKind.NativeStorage,
            ("Dolphin.Timing.NativeTiming",_)=>IntrinsicKind.NativeStorage,
            ("Dolphin.Diagnostics.NativeDiagnostics",_)=>IntrinsicKind.NativeStorage,
            ("Dolphin.Diagnostics.NativeCorpusBridge",_)=>IntrinsicKind.NativeStorage,
            ("Dolphin.Memory.NativeAram",_)=>IntrinsicKind.NativeStorage,
            ("Dolphin.IO.NativeDol",_)=>IntrinsicKind.NativeStorage,
            ("Dolphin.Storage.NativeStorage",_)=>IntrinsicKind.NativeStorage,
            ("Dolphin.Network.NativeNetwork",_)=>IntrinsicKind.NativeStorage,
            ("Dolphin.Threading.Tasks.NativeAsync",_)=>IntrinsicKind.NativeStorage,
            ("Dolphin.Collections.NativeComparison",_)=>IntrinsicKind.NativeComparison,
            ("System.Runtime.CompilerServices.RuntimeHelpers","InitializeArray")=>IntrinsicKind.InitializeArray,
            ("System.Object",".ctor")=>IntrinsicKind.ObjectConstructor,
            ("System.Object","ToString") when parameterCount==0=>IntrinsicKind.ObjectToString,
            ("System.Object","GetHashCode")=>IntrinsicKind.ObjectGetHashCode,
            ("System.Object","Equals") when parameterCount==2=>IntrinsicKind.ObjectStaticEquals,
            ("System.Object","Equals")=>IntrinsicKind.ObjectEquals,
            ("System.String","Equals") when parameterCount==1&&parameterTypes[0]=="String"=>IntrinsicKind.StringInstanceEquals,
            ("System.String","Equals") when parameterCount==2&&parameterTypes.All(t=>t=="String")=>IntrinsicKind.StringEquals,
            ("System.String","StartsWith") when parameterCount==1&&parameterTypes[0]=="String"=>IntrinsicKind.StringStartsWith,
            ("System.String","EndsWith") when parameterCount==1&&parameterTypes[0]=="String"=>IntrinsicKind.StringEndsWith,
            ("System.String","Contains") when parameterCount==1&&parameterTypes[0]=="String"=>IntrinsicKind.StringContains,
            ("System.String","IndexOf") when parameterCount==1&&parameterTypes[0]=="String"=>IntrinsicKind.StringIndexOf,
            ("System.String","get_Length")=>IntrinsicKind.StringLength,
            ("System.String","get_Chars")=>IntrinsicKind.StringCharAt,
            ("System.String","op_Equality")=>IntrinsicKind.StringEquals,
            ("System.String","op_Inequality")=>IntrinsicKind.StringNotEquals,
            ("System.String","Substring") when parameterCount is 1 or 2=>IntrinsicKind.StringSubstring,
            ("System.String","Concat") when parameterCount==2&&parameterTypes.All(t=>t=="String")=>IntrinsicKind.StringConcat,
            ("System.Int32" or "System.Int64" or "System.UInt32" or "System.UInt64" or "System.Int16" or "System.UInt16" or "System.Byte" or "System.SByte" or "System.Single" or "System.Double" or "System.Boolean" or "System.Char","ToString") when (parameterCount==1&&parameterTypes[0].EndsWith("System.IFormatProvider",StringComparison.Ordinal))||(parameterCount==2&&parameterTypes[0]=="String"&&parameterTypes[1].EndsWith("System.IFormatProvider",StringComparison.Ordinal))=>IntrinsicKind.PrimitiveToStringProvider,
            ("System.Int32" or "System.Int64" or "System.UInt32" or "System.UInt64" or "System.Int16" or "System.UInt16" or "System.Byte" or "System.SByte" or "System.Single" or "System.Double" or "System.Boolean" or "System.Char","ToString") when parameterCount==0||parameterCount==1&&parameterTypes[0]=="String"=>IntrinsicKind.PrimitiveToString,

            (_, ".ctor") when type?.EndsWith("Exception",StringComparison.Ordinal)==true=>IntrinsicKind.ExceptionConstructor,
            (_, "get_Message") when type?.EndsWith("Exception",StringComparison.Ordinal)==true=>IntrinsicKind.ExceptionMessage,
            ("System.Nullable`1",".ctor")=>IntrinsicKind.NullableConstructor,
            ("System.Nullable`1","get_HasValue")=>IntrinsicKind.NullableHasValue,
            ("System.Nullable`1","get_Value")=>IntrinsicKind.NullableValue,
            ("System.Nullable`1","GetValueOrDefault") when parameterCount==1=>IntrinsicKind.NullableGetValueOrDefaultValue,
            ("System.Nullable`1","GetValueOrDefault")=>IntrinsicKind.NullableGetValueOrDefault,
            ("System.Nullable`1","Equals")=>IntrinsicKind.NullableEquals,
            ("System.Nullable`1","GetHashCode")=>IntrinsicKind.NullableGetHashCode,
            ("System.Collections.Generic.KeyValuePair`2",".ctor")=>IntrinsicKind.KeyValuePairConstructor,
            ("System.Collections.Generic.KeyValuePair`2","get_Key")=>IntrinsicKind.KeyValuePairKey,
            ("System.Collections.Generic.KeyValuePair`2","get_Value")=>IntrinsicKind.KeyValuePairValue,
            ("System.Array","get_Length")=>IntrinsicKind.ArrayLength,
            ("System.Array","get_LongLength")=>IntrinsicKind.ArrayLongLength,
            ("System.Array","get_Rank")=>IntrinsicKind.ArrayRank,
            ("System.Array","GetLength")=>IntrinsicKind.ArrayGetLength,
            ("System.Array","GetLowerBound")=>IntrinsicKind.ArrayGetLowerBound,
            ("System.Array","GetUpperBound")=>IntrinsicKind.ArrayGetUpperBound,
            ("System.Array","Clear") when parameterCount==3=>IntrinsicKind.ArrayClear,
            ("System.Array","Copy") when parameterCount is 3 or 5=>IntrinsicKind.ArrayCopy,
            ("System.Array","IndexOf") when !genericMethod=>IntrinsicKind.ArrayIndexOf,
            ("System.Collections.Generic.ICollection`1","get_Count")=>IntrinsicKind.ArrayInterfaceCount,
            ("System.Collections.Generic.ICollection`1","get_IsReadOnly")=>IntrinsicKind.ArrayInterfaceIsReadOnly,
            ("System.Collections.Generic.IList`1","get_Item")=>IntrinsicKind.ArrayInterfaceItemGet,
            ("System.Collections.Generic.IReadOnlyList`1","get_Item")=>IntrinsicKind.ArrayInterfaceItemGet,
            ("System.Collections.Generic.IList`1","set_Item")=>IntrinsicKind.ArrayInterfaceItemSet,
            ("System.Collections.Generic.ICollection`1","Contains")=>IntrinsicKind.ArrayInterfaceContains,
            ("System.Collections.Generic.IList`1","IndexOf")=>IntrinsicKind.ArrayInterfaceIndexOf,
            ("System.Collections.Generic.IReadOnlyCollection`1","get_Count")=>IntrinsicKind.ArrayInterfaceCount,
            ("System.ArraySegment`1",".ctor")=>IntrinsicKind.ArraySegmentConstructor,
            ("System.ArraySegment`1","get_Array")=>IntrinsicKind.ArraySegmentArray,
            ("System.ArraySegment`1","get_Offset")=>IntrinsicKind.ArraySegmentOffset,
            ("System.ArraySegment`1","get_Count")=>IntrinsicKind.ArraySegmentCount,
            ("System.ArraySegment`1","get_Item")=>IntrinsicKind.ArraySegmentItem,
            ("System.ArraySegment`1","GetEnumerator")=>IntrinsicKind.ArraySegmentGetEnumerator,
            ("System.ArraySegment`1+Enumerator","MoveNext")=>IntrinsicKind.ArraySegmentEnumeratorMoveNext,
            ("System.ArraySegment`1+Enumerator","get_Current")=>IntrinsicKind.ArraySegmentEnumeratorCurrent,
            ("System.ArraySegment`1+Enumerator","Dispose")=>IntrinsicKind.ArraySegmentEnumeratorDispose,
            ("System.Console","WriteLine") when parameterCount==1&&parameterTypes[0]=="String"=>IntrinsicKind.GameCubeWriteLine,
            ("Dolphin.Diagnostics.DebugOverlay","WriteLine")=>IntrinsicKind.GameCubeWriteLine,
            ("Dolphin.Input.Controller","ReadButtonsDown")=>IntrinsicKind.GameCubeReadButtonsDown,
            (_, "GetEnumerator") when type?.Contains("ArraySegment",StringComparison.Ordinal)==true=>IntrinsicKind.ArraySegmentGetEnumerator,
            (_, "MoveNext") when type?.Contains("ArraySegment",StringComparison.Ordinal)==true&&type.Contains("Enumerator",StringComparison.Ordinal)=>IntrinsicKind.ArraySegmentEnumeratorMoveNext,
            (_, "get_Current") when type?.Contains("ArraySegment",StringComparison.Ordinal)==true&&type.Contains("Enumerator",StringComparison.Ordinal)=>IntrinsicKind.ArraySegmentEnumeratorCurrent,
            (_, "Dispose") when type?.Contains("ArraySegment",StringComparison.Ordinal)==true&&type.Contains("Enumerator",StringComparison.Ordinal)=>IntrinsicKind.ArraySegmentEnumeratorDispose,
            (_, "MoveNext") when type is "Enumerator" or "System.Enumerator"=>IntrinsicKind.ArraySegmentEnumeratorMoveNext,
            (_, "get_Current") when type is "Enumerator" or "System.Enumerator"=>IntrinsicKind.ArraySegmentEnumeratorCurrent,
            (_, "Dispose") when type is "Enumerator" or "System.Enumerator"=>IntrinsicKind.ArraySegmentEnumeratorDispose,
            _=>IntrinsicKind.None
        };
    }
}
