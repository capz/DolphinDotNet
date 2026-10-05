using System.Reflection.Metadata;

namespace DolphinDotNet.Compiler;

internal enum IntrinsicKind
{
    None,
    ObjectConstructor,
    StringLength,
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
            ("System.String","get_Length")=>IntrinsicKind.StringLength,
            ("DolphinDotNet.GameCube.GameCube","WriteLine")=>IntrinsicKind.GameCubeWriteLine,
            ("DolphinDotNet.GameCube.GameCube","ReadButtonsDown")=>IntrinsicKind.GameCubeReadButtonsDown,
            ("DolphinDotNet.GameCube.GameCube","PresentDemoFrame")=>IntrinsicKind.GameCubePresentDemoFrame,
            _=>IntrinsicKind.None
        };
    }
}
