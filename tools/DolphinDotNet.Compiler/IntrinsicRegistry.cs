using System.Reflection.Metadata;

namespace DolphinDotNet.Compiler;

internal enum IntrinsicKind
{
    None,
    ObjectConstructor,
    ObjectGetHashCode,
    ObjectEquals,
    StringLength,
    StringCharAt,
    StringEquals,
    StringStartsWith,
    StringEndsWith,
    StringContains,
    StringIndexOf,
    StringSubstring,
    StringConcat,
    ExceptionConstructor,
    ExceptionMessage,
    GameCubeWriteLine,
    GameCubeReadButtonsDown,
    GameCubePresentDemoFrame,
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
        string? type=null;string? name=null;var parameterCount=-1;
        if(handle.Kind==HandleKind.MemberReference)
        {
            var member=md.GetMemberReference((MemberReferenceHandle)handle);
            type=MetadataLoader.ResolveTypeName(md,member.Parent);name=md.GetString(member.Name);var sr=md.GetBlobReader(member.Signature);var sh=sr.ReadSignatureHeader();if(sh.IsGeneric)sr.ReadCompressedInteger();parameterCount=sr.ReadCompressedInteger();
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
            ("System.Object",".ctor")=>IntrinsicKind.ObjectConstructor,
            ("System.Object","GetHashCode")=>IntrinsicKind.ObjectGetHashCode,
            ("System.Object","Equals")=>IntrinsicKind.ObjectEquals,
            ("System.String","get_Length")=>IntrinsicKind.StringLength,
            ("System.String","get_Chars")=>IntrinsicKind.StringCharAt,
            ("System.String","Equals")=>IntrinsicKind.StringEquals,
            ("System.String","op_Equality")=>IntrinsicKind.StringEquals,
            ("System.String","StartsWith") when parameterCount==1=>IntrinsicKind.StringStartsWith,
            ("System.String","EndsWith") when parameterCount==1=>IntrinsicKind.StringEndsWith,
            ("System.String","Contains") when parameterCount==1=>IntrinsicKind.StringContains,
            ("System.String","IndexOf") when parameterCount==1=>IntrinsicKind.StringIndexOf,
            ("System.String","Substring") when parameterCount is 1 or 2=>IntrinsicKind.StringSubstring,
            ("System.String","Concat") when parameterCount==2=>IntrinsicKind.StringConcat,
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
            ("System.ArraySegment`1",".ctor")=>IntrinsicKind.ArraySegmentConstructor,
            ("System.ArraySegment`1","get_Array")=>IntrinsicKind.ArraySegmentArray,
            ("System.ArraySegment`1","get_Offset")=>IntrinsicKind.ArraySegmentOffset,
            ("System.ArraySegment`1","get_Count")=>IntrinsicKind.ArraySegmentCount,
            ("System.ArraySegment`1","get_Item")=>IntrinsicKind.ArraySegmentItem,
            ("System.ArraySegment`1","GetEnumerator")=>IntrinsicKind.ArraySegmentGetEnumerator,
            ("System.ArraySegment`1+Enumerator","MoveNext")=>IntrinsicKind.ArraySegmentEnumeratorMoveNext,
            ("System.ArraySegment`1+Enumerator","get_Current")=>IntrinsicKind.ArraySegmentEnumeratorCurrent,
            ("System.ArraySegment`1+Enumerator","Dispose")=>IntrinsicKind.ArraySegmentEnumeratorDispose,
            ("DolphinDotNet.GameCube.GameCube","WriteLine")=>IntrinsicKind.GameCubeWriteLine,
            ("DolphinDotNet.GameCube.GameCube","ReadButtonsDown")=>IntrinsicKind.GameCubeReadButtonsDown,
            ("DolphinDotNet.GameCube.GameCube","PresentDemoFrame")=>IntrinsicKind.GameCubePresentDemoFrame,
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
