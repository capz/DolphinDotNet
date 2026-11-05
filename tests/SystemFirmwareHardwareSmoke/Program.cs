using System;
using System.IO;
using Dolphin.Audio;
using Dolphin.Diagnostics;
using Dolphin.Graphics;
using Dolphin.Input;
using Dolphin.Storage;
using Dolphin.System;
using Dolphin.Timing;

/// <summary>Original-firmware acceptance fixture. No firmware or extracted assets
/// are packaged. Each frame presents only the selected texture, using the existing
/// debugger automation frame boundary. A selects textures/models/animation snapshots; X animates; START plays
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
            Model? model=null;ModelInstance? instance=null;SystemStartupRenderer? startup=null;
            var renderer=new Renderer(device);var camera=new Camera(40,0,1,2000);
            camera.LookAt(new Vector3(300,200,500),new Vector3(0,0,0),new Vector3(0,1,0));
            var transform=new Transform();var clock=new FrameClock();bool playing=false;
            var times=new double[]{0,.8,2,3.4,4.2,5,6.2};
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
                        texture?.Dispose();texture=null;instance?.Dispose();instance=null;model?.Dispose();model=null;
                        selected=(selected+1)%22;playing=false;
                        if(selected<6)
                        {
                            startup?.Dispose();startup=null;
                            texture=SystemResources.CreateTexture(catalog,(SystemTexture)selected);
                            DebugOverlay.WriteLine("FIRMWARE TEXTURE "+selected+" READY");
                        }
                        else if(selected<15)
                        {
                            model=SystemResources.CreateModelAsync(catalog,(SystemModel)(selected-6)).Result;
                            instance=model.CreateInstance();
                            DebugOverlay.WriteLine("FIRMWARE MODEL "+(selected-6)+" READY "+model.NativeBytes);
                        }
                        else
                        {
                            if(startup==null)startup=SystemStartupRenderer.Load(catalog);
                            startup.Seek(times[selected-15]);
                            DebugOverlay.WriteLine("FIRMWARE ANIMATION "+(selected-15)+" READY "+startup.NativeBytes);
                        }
                    }
                    if(pad.X.WasPressed&&startup!=null){playing=!playing;clock.Reset();}
                    float delta=clock.Tick();if(playing&&startup!=null)startup.Update(delta);
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
                    if(texture!=null)
                    {
                        batch.Begin();
                        float scale=Math.Min(540f/texture.Width,400f/texture.Height);
                        float width=texture.Width*scale,height=texture.Height*scale;
                        batch.DrawTexture(texture,new RectF((640-width)/2,(480-height)/2,width,height),Color32.White);
                        batch.End();
                    }
                    else if(instance!=null)
                    {renderer.Begin3D(camera);renderer.DrawModel(instance,transform);renderer.End3D();}
                    else startup?.Draw();
                    device.EndFrame();
                }
            }
            finally
            {
                voice?.Dispose();sound?.Dispose();texture?.Dispose();instance?.Dispose();model?.Dispose();startup?.Dispose();
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
