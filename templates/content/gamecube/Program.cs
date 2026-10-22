using Dolphin.Graphics;
using Dolphin.Input;

// Build with dotnet build; launch Dolphin with dotnet run.
while (true)
{
    Controller.Update();
    GraphicsDevice.Default.BeginFrame(new Color32(12, 18, 31));
    GraphicsDevice.Default.EndFrame();
}
