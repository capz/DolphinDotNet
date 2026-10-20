using System;
using System.Collections.Generic;
using System.IO;
using Dolphin.Diagnostics;
using Dolphin.Graphics;
using Dolphin.Input;
using Dolphin.Storage;
using Dolphin.Timing;

internal static class Program
{
    private static readonly List<string> paths=new List<string>();
    private static readonly BrowserNavigation navigation=new BrowserNavigation();
    private static int directories,selection,top;
    private static string current="",status="",returnDirectory="";
    private static bool disc,loader,viewingImage,preferLoader=true;
    private static StorageDevice browsing=StorageDevice.GcLoader;
    private static Texture2D? banner;
    private static string title="DISC BROWSER";

    private static void Listing()
    {
        paths.Clear();selection=top=0;navigation.Reset();
        var folders=new List<string>();var files=new List<string>();
        foreach(var path in Storage.EnumerateDirectories(browsing,current))
        {if(folders.Count>=256)break;folders.Add(path);}
        foreach(var path in Storage.EnumerateFiles(browsing,current))
        {if(files.Count>=256)break;files.Add(path);}
        folders.Sort(StringComparer.OrdinalIgnoreCase);files.Sort(StringComparer.OrdinalIgnoreCase);
        directories=folders.Count;paths.AddRange(folders);paths.AddRange(files);
        status=$"{paths.Count:D3} ENTRIES  {(browsing==StorageDevice.GcLoader?"GC LOADER SD":"DVD")}:/{current}";
        for(int i=0;i<paths.Count&&i<3;i++)DebugOverlay.WriteLine(paths[i]);
    }

    private static void ClearBanner(){banner?.Dispose();banner=null;}

    private static void Release()
    {
        ClearBanner();
        if(disc){Storage.Unmount(StorageDevice.DvdDrive);disc=false;}
        if(loader){Storage.Unmount(StorageDevice.GcLoader);loader=false;}
        viewingImage=false;returnDirectory="";
    }

    private static void ShowDisc()
    {
        browsing=StorageDevice.DvdDrive;current="";title="GAME DISC";
        ClearBanner();
        // A missing or damaged banner must not make the directory tree unusable.
        try
        {
            var identity=Storage.GetMountedDiscIdentity();
            if(identity.Title.Length>0)title=identity.Title;
            DebugOverlay.WriteLine("Title: "+title);
            if(identity.Banner!=null)
            {
                var pixels=identity.Banner.Rgba32;banner=Texture2D.Create(96,32,pixels);
                DebugOverlay.WriteLine($"Banner RGBA size: {pixels.Length}");
            }
        }
        catch(Exception error){DebugOverlay.WriteLine("Disc metadata: "+error.Message);}
        Listing();
    }

    private static bool TryLoader()
    {
        try
        {
            loader=Storage.Mount(StorageDevice.GcLoader);
            if(!loader)return false;
            browsing=StorageDevice.GcLoader;current="";title="GC LOADER SD";
            Listing();return true;
        }
        catch(Exception error)
        {
            DebugOverlay.WriteLine("GC Loader SD: "+error.Message);
            if(loader){Storage.Unmount(StorageDevice.GcLoader);loader=false;}
            return false;
        }
    }

    private static bool TryPhysicalDisc()
    {
        try
        {
            disc=Storage.MountDisc();
            if(!disc)return false;
            ShowDisc();return true;
        }
        catch(Exception error)
        {
            DebugOverlay.WriteLine("Game disc: "+error.Message);
            if(disc){Storage.Unmount(StorageDevice.DvdDrive);disc=false;}
            return false;
        }
    }

    private static void Refresh()
    {
        Release();paths.Clear();title="DISC BROWSER";current="";
        // GC Loader exposes a separate SD filesystem. A presented DVD is not its
        // file browser: do not automatically pick the DVD ahead of the card.
        if(preferLoader)
        {
            if(TryLoader())return;
            status="GC LOADER SD UNAVAILABLE; CHECK FIRMWARE / Y FOR DVD";
        }
        else
        {
            if(TryPhysicalDisc())return;
            status="GAME DISC UNAVAILABLE; Y FOR GC LOADER SD";
        }
        DebugOverlay.WriteLine(status);
    }

    private static void OpenSelected()
    {
        if(selection>=paths.Count)return;
        string path=paths[selection];
        if(selection<directories)
        {
            current=BrowserPath.Relative(browsing==StorageDevice.GcLoader,path);
            Listing();
            return;
        }
        if(browsing!=StorageDevice.GcLoader || !BrowserPath.IsDiscImage(path))
        {
            status="FILE SELECTED (BROWSE ONLY): "+Path.GetFileName(path);
            return;
        }
        // Keep the backing SD card mounted while its ISO is exposed as dvd:/.
        if(!Storage.MountDisc(path))
        {
            status="CANNOT MOUNT IMAGE: "+Path.GetFileName(path);
            return;
        }
        disc=true;viewingImage=true;returnDirectory=current;
        try{ShowDisc();}
        catch(Exception error)
        {
            Storage.Unmount(StorageDevice.DvdDrive);disc=false;viewingImage=false;
            browsing=StorageDevice.GcLoader;current=returnDirectory;title="GC LOADER SD";
            Listing();status="IMAGE BROWSE FAILED: "+error.Message;
        }
    }

    private static void Back()
    {
        if(current.Length>0)
        {
            current=Path.GetDirectoryName(current)??"";
            Listing();return;
        }
        if(viewingImage&&loader)
        {
            // B at the image root goes back to the card directory it came from.
            Storage.Unmount(StorageDevice.DvdDrive);disc=false;viewingImage=false;
            ClearBanner();browsing=StorageDevice.GcLoader;
            current=returnDirectory;title="GC LOADER SD";returnDirectory="";
            Listing();
        }
    }

    private static int Main()
    {
        using var draw=new Draw2D(GraphicsDevice.Default,new Draw2DOptions {Width=640,Height=480});
        using var text=new SampleText();
        Refresh();
        try
        {
            while(true)
            {
                Controller.Update();var pad=Controller.GetState();
                if(pad.Start.WasPressed)return 0;
                if(pad.Y.WasPressed){preferLoader=!preferLoader;Refresh();}
                else if(pad.X.WasPressed)Refresh();
                int move=navigation.Update(pad.IsConnected,pad.DPadUp.IsHeld,pad.DPadDown.IsHeld,pad.Stick.Y,Clock.Timestamp);
                if(loader||disc)
                {
                    try
                    {
                        if(move>0&&selection+1<paths.Count)selection++;
                        if(move<0&&selection>0)selection--;
                        if(pad.A.WasPressed)OpenSelected();
                        if(pad.B.WasPressed)Back();
                    }
                    catch(Exception error){status=error.Message;}
                }
                if(selection<top)top=selection;
                if(selection>=top+8)top=selection-7;
                var device=GraphicsDevice.Default;device.BeginFrame(new Color32(15,26,40));draw.Begin();
                text.Draw(draw,"DISC BROWSER",24,140,4,Color32.White);
                string label=title.Length>40?title.Substring(0,40):title;
                text.Draw(draw,label,24,180,2,new Color32(112,225,240));
                if(banner!=null)draw.DrawTexture(banner,new RectF(424,140,192,64),Color32.White);
                text.Draw(draw,status.Length>70?status.Substring(0,70):status,24,220,2,Color32.White);
                for(int row=0;row<8&&top+row<paths.Count;row++)
                {
                    int index=top+row;string name=Path.GetFileName(paths[index]);if(name.Length>52)name=name.Substring(0,52);
                    text.Draw(draw,(index==selection?"> ":"  ")+(index<directories?"DIR ":"FILE ")+name,24,250+row*21,2,index==selection?new Color32(255,210,104):Color32.White);
                }
                text.Draw(draw,pad.IsConnected?"D-PAD / STICK SELECT  A OPEN  B BACK":"CONNECT CONTROLLER TO PORT 1",24,430,2,Color32.White);
                text.Draw(draw,"X REFRESH  Y SOURCE  START EXIT  Z STATS",24,450,2,Color32.White);
                draw.End();device.EndFrame();
            }
        }
        finally{Release();}
    }
}
