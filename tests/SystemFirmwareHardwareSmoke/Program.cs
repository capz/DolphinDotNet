using System;
using System.IO;
using Dolphin.Audio;
using Dolphin.Diagnostics;
using Dolphin.Graphics;
using Dolphin.Input;
using Dolphin.Storage;
using Dolphin.System;

/// <summary>Original-firmware acceptance fixture. No firmware or extracted assets
/// are packaged. Each frame presents only the selected texture, using the existing
/// debugger automation frame boundary. A selects the next texture, START plays
/// the stereo source, and B exits and releases all resources.</summary>
internal static class Program
{
    private static FirmwareImage Acquire()
    {
#if FIRMWARE_EXPLICIT_FILE
        // Deliberately separate source acceptance: this build never substitutes
        // console data for the explicitly selected file under test.
        if(!Storage.Mount(StorageDevice.SdSerialPort2))
            throw new IOException("Cannot mount the explicit test volume.");
        return FirmwareImage.Load("sd:/ipl.bin");
#else
        // Default build must prove the EXI path. No implicit file fallback.
        return SystemFirmware.ReadConsole();
#endif
    }

    private static int Main()
    {
        try
        {
            using var image=Acquire();
            DebugOverlay.WriteLine("FIRMWARE SOURCE "+(int)image.Source);
            DebugOverlay.WriteLine(image.Revision);
            using var catalog=image.CreateCatalog();
            DebugOverlay.WriteLine("FIRMWARE RECORDS "+catalog.Count);
            DebugOverlay.WriteLine("FIRMWARE WAVE SYSTEMS "+catalog.WaveSystemCount);
            image.Dispose();

            var device=GraphicsDevice.Default;
            using var batch=new Draw2D(device,new Draw2DOptions{
                Width=640,Height=480,VertexCapacity=6
            });
            Texture2D? texture=null;
            Sound? sound=null;
            SoundChannel? voice=null;
            try
            {
                int selected=0;
                texture=SystemResources.CreateTexture(catalog,SystemTexture.Environment);
                DebugOverlay.WriteLine("FIRMWARE TEXTURE 0 READY");
                DebugOverlay.TextVisible=false;
                while(true)
                {
                    Controller.Update();
                    var pad=Controller.GetState(0);
                    if(pad.B.WasPressed)break;
                    if(pad.A.WasPressed)
                    {
                        // Publication/disposal occurs between complete frames.
                        texture.Dispose();texture=null;
                        selected=(selected+1)%6;
                        texture=SystemResources.CreateTexture(catalog,(SystemTexture)selected);
                        DebugOverlay.WriteLine("FIRMWARE TEXTURE "+selected+" READY");
                    }
                    if(pad.Start.WasPressed)
                    {
                        voice?.Dispose();voice=null;
                        if(sound==null)
                        {
                            var source=catalog.GetStartupAudio();
                            DebugOverlay.WriteLine("FIRMWARE STEREO "+source.SampleRate+" "+source.SampleCount);
                            sound=SystemResources.CreateStartupSound(catalog);
                        }
                        voice=sound.Play();
                        DebugOverlay.WriteLine("FIRMWARE AUDIO STARTED");
                    }
                    device.BeginFrame(Color32.Black);
                    batch.Begin();
                    // Fixed presentation independent of frame rate. This is a
                    // texture acceptance image, not a startup reconstruction.
                    float scale=Math.Min(540f/texture.Width,400f/texture.Height);
                    float width=texture.Width*scale,height=texture.Height*scale;
                    batch.DrawTexture(texture,new RectF((640-width)/2,(480-height)/2,width,height),Color32.White);
                    batch.End();
                    device.EndFrame();
                }
            }
            finally
            {
                voice?.Dispose();sound?.Dispose();texture?.Dispose();
            }
            DebugOverlay.WriteLine("FIRMWARE HARDWARE RESOURCES RELEASED");
            return 0;
        }
        catch(Exception error)
        {
            DebugOverlay.TextVisible=true;
            DebugOverlay.WriteLine("FIRMWARE HARDWARE FAIL");
            DebugOverlay.WriteLine(error.Message);
            return 1;
        }
        finally
        {
#if FIRMWARE_EXPLICIT_FILE
            if(Storage.IsMounted(StorageDevice.SdSerialPort2))
                Storage.Unmount(StorageDevice.SdSerialPort2);
#endif
        }
    }
}
