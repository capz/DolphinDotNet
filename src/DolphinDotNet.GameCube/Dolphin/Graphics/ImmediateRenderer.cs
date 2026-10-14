namespace Dolphin.Graphics;

/// <summary>Immediate-mode graphics operations backed by native GX. Calls run on the application thread.</summary>
/// <remarks>Begin a frame, set its projection and transform, then submit complete coloured triangles.
/// End every triangle batch before ending the frame. EndFrame renders the diagnostic overlay and presents.</remarks>
public static class ImmediateRenderer
{
    public static void BeginFrame(float red, float green, float blue, float alpha) =>
        NativeGraphics.GraphicsBeginFrame(red, green, blue, alpha);
    public static void SetFrustum(float left, float right, float bottom, float top, float near, float far) =>
        NativeGraphics.GraphicsFrustum(left, right, bottom, top, near, far);
    public static void Translate(float x, float y, float z) => NativeGraphics.GraphicsTranslate(x, y, z);
    public static void Rotate(float degrees, float x, float y, float z) => NativeGraphics.GraphicsRotate(degrees, x, y, z);
    public static void BeginTriangles() => NativeGraphics.GraphicsBeginTriangles();
    public static void Color(float red, float green, float blue) => NativeGraphics.GraphicsColor(red, green, blue);
    public static void Vertex(float x, float y, float z) => NativeGraphics.GraphicsVertex(x, y, z);
    public static void EndTriangles() => NativeGraphics.GraphicsEndTriangles();
    public static void EndFrame() => NativeGraphics.GraphicsEndFrame();
}
