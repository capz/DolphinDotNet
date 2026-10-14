using Dolphin.Graphics;

/// <summary>The sample owns its scene, colours, geometry and frame composition.</summary>
internal static class DemoScene
{
    internal static void PresentDemoFrame(int rotationDegrees)
    {
        ImmediateRenderer.BeginFrame(0.025f, 0.035f, 0.06f, 1.0f);
        ImmediateRenderer.SetFrustum(-0.8f, 0.8f, -0.6f, 0.6f, 1.0f, 100.0f);
        ImmediateRenderer.Translate(0, 0, -4);
        ImmediateRenderer.Rotate(rotationDegrees, 0.4f, 1.0f, 0.2f);
        ImmediateRenderer.BeginTriangles();
        ImmediateRenderer.Color(1, 0.25f, 0.15f);
        ImmediateRenderer.Vertex(0, 1, 0);
        ImmediateRenderer.Color(0.15f, 0.8f, 0.35f);
        ImmediateRenderer.Vertex(-1, -1, 0);
        ImmediateRenderer.Color(0.2f, 0.45f, 1);
        ImmediateRenderer.Vertex(1, -1, 0);
        ImmediateRenderer.EndTriangles();
        ImmediateRenderer.EndFrame();
    }
}
