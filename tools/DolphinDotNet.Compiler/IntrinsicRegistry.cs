using System.Reflection.Metadata;

namespace DolphinDotNet.Compiler;

internal enum IntrinsicKind
{
    None,
    ObjectConstructor,
    ExceptionConstructor,
    StringLength,
    StringChars,
    StringEquals,
    StringConcat,
    StringStartsWith,
    StringEndsWith,
    StringContains,
    ObjectReferenceEquals,
    ObjectEquals,
    ObjectToString,
    ObjectGetHashCode,
    ObjectGetType,
    StringIndexOf,
    StringSubstring,
    ArrayRank,
    ArrayGetLength,
    ArrayClear,
    ArrayCopy,
    GameCubeWriteLine,
    GameCubeReadButtonsDown,
    GameCubePresentDemoFrame
}

internal static class IntrinsicRegistry
{
    public static IntrinsicKind Classify(MetadataReader md,EntityHandle handle)
    {
        string? type=null;string? name=null;
        if(handle.Kind==HandleKind.MemberReference)
        {
            var member=md.GetMemberReference((MemberReferenceHandle)handle);
            type=MetadataLoader.ResolveTypeName(md,member.Parent);name=md.GetString(member.Name);
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
            ("System.Exception",".ctor")=>IntrinsicKind.ExceptionConstructor,
            ("System.String","get_Length")=>IntrinsicKind.StringLength,
            ("System.String","get_Chars")=>IntrinsicKind.StringChars,
            ("System.String","Equals")=>IntrinsicKind.StringEquals,
            ("System.String","op_Equality")=>IntrinsicKind.StringEquals,
            ("System.String","Concat")=>IntrinsicKind.StringConcat,
            ("System.String","StartsWith")=>IntrinsicKind.StringStartsWith,
            ("System.String","EndsWith")=>IntrinsicKind.StringEndsWith,
            ("System.String","Contains")=>IntrinsicKind.StringContains,
            ("System.Object","ReferenceEquals")=>IntrinsicKind.ObjectReferenceEquals,
            ("System.Object","Equals")=>IntrinsicKind.ObjectEquals,
            ("System.Object","ToString")=>IntrinsicKind.ObjectToString,
            ("System.Object","GetHashCode")=>IntrinsicKind.ObjectGetHashCode,
            ("System.Object","GetType")=>IntrinsicKind.ObjectGetType,
            ("System.String","IndexOf")=>IntrinsicKind.StringIndexOf,
            ("System.String","Substring")=>IntrinsicKind.StringSubstring,
            ("System.Array","get_Rank")=>IntrinsicKind.ArrayRank,
            ("System.Array","GetLength")=>IntrinsicKind.ArrayGetLength,
            ("System.Array","Clear")=>IntrinsicKind.ArrayClear,
            ("System.Array","Copy")=>IntrinsicKind.ArrayCopy,
            ("DolphinDotNet.GameCube.GameCube","WriteLine")=>IntrinsicKind.GameCubeWriteLine,
            ("DolphinDotNet.GameCube.GameCube","ReadButtonsDown")=>IntrinsicKind.GameCubeReadButtonsDown,
            ("DolphinDotNet.GameCube.GameCube","PresentDemoFrame")=>IntrinsicKind.GameCubePresentDemoFrame,
            _=>IntrinsicKind.None
        };
    }
}
