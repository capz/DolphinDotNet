using System;

namespace Dolphin.Graphics;

internal static class NativeGraphics
{
    private static Exception Unsupported() => new PlatformNotSupportedException("Requires DolphinDotNet AOT.");
    internal static void GraphicsBeginFrame(float r, float g, float b, float a) => throw Unsupported();
    internal static void GraphicsFrustum(float left, float right, float bottom, float top, float near, float far) => throw Unsupported();
    internal static void GraphicsTranslate(float x, float y, float z) => throw Unsupported();
    internal static void GraphicsRotate(float degrees, float x, float y, float z) => throw Unsupported();
    internal static void GraphicsBeginTriangles() => throw Unsupported();
    internal static void GraphicsColor(float r, float g, float b) => throw Unsupported();
    internal static void GraphicsVertex(float x, float y, float z) => throw Unsupported();
    internal static void GraphicsEndTriangles() => throw Unsupported();
    internal static void GraphicsEndFrame() => throw Unsupported();
}
