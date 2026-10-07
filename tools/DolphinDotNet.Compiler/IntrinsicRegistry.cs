using System.Reflection.Metadata;

namespace DolphinDotNet.Compiler;

internal enum IntrinsicKind
{
    None,
    ObjectConstructor,
    StringLength,
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
    ArraySegmentItem
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
            ("System.String","get_Length")=>IntrinsicKind.StringLength,
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
            ("DolphinDotNet.GameCube.GameCube","WriteLine")=>IntrinsicKind.GameCubeWriteLine,
            ("DolphinDotNet.GameCube.GameCube","ReadButtonsDown")=>IntrinsicKind.GameCubeReadButtonsDown,
            ("DolphinDotNet.GameCube.GameCube","PresentDemoFrame")=>IntrinsicKind.GameCubePresentDemoFrame,
            _=>IntrinsicKind.None
        };
    }
}
