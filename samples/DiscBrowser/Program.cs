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
    private static string current="",status="";
    private static bool disc,loader;
    private static Texture2D? banner;
    private static string title="DISC BROWSER";
    private static void Listing()
    {
        paths.Clear();selection=top=0;navigation.Reset();
        var folders=new List<string>();var files=new List<string>();
        foreach(var path in Storage.EnumerateDirectories(StorageDevice.DvdDrive,current))
        {if(folders.Count>=256)break;folders.Add(path);}
        foreach(var path in Storage.EnumerateFiles(StorageDevice.DvdDrive,current))
        {if(files.Count>=256)break;files.Add(path);}
        folders.Sort(StringComparer.OrdinalIgnoreCase);files.Sort(StringComparer.OrdinalIgnoreCase);
        directories=folders.Count;paths.AddRange(folders);paths.AddRange(files);
        status=$"{paths.Count:D3} ENTRIES  {current}";
        for(int i=0;i<paths.Count&&i<3;i++)DebugOverlay.WriteLine(paths[i]);
    }
    private static void Release()
    {
        banner?.Dispose();banner=null;
        if(disc){Storage.Unmount(StorageDevice.DvdDrive);disc=false;}
        if(loader){Storage.Unmount(StorageDevice.GcLoader);loader=false;}
    }
    private static void Refresh()
    {
        Release();paths.Clear();title="DISC BROWSER";current="";
        try
        {
            // Probe physical/ODE media before touching GC Loader sectors.
            disc=Storage.MountDisc();
            if(!disc)
            {
                loader=Storage.Mount(StorageDevice.GcLoader);
                if(loader&&Storage.FileExists(StorageDevice.GcLoader,"games/demo.iso"))disc=Storage.MountDisc("gcloader:/games/demo.iso");
            }
            if(!disc){status="NO GAMECUBE DISC OR DEMO ISO AVAILABLE";DebugOverlay.WriteLine(status);return;}
            var identity=Storage.GetMountedDiscIdentity();title=identity.Title;
            DebugOverlay.WriteLine("Title: "+title);
            if(identity.Banner!=null)
            {
                var pixels=identity.Banner.Rgba32;banner=Texture2D.Create(96,32,pixels);
                DebugOverlay.WriteLine($"Banner RGBA size: {pixels.Length}");
            }
            Listing();
        }
        catch(Exception error){status=error.Message;Release();}
    }
    private static int Main()
    {
        using var draw=new Draw2D(GraphicsDevice.Default,new Draw2DOptions {Width=640,Height=480});
        using var text=new SampleText();Refresh();
        try
        {
            while(true)
            {
                Controller.Update();var pad=Controller.GetState();
                if(pad.Start.WasPressed)return 0;
                if(pad.X.WasPressed)Refresh();
                int move=navigation.Update(pad.IsConnected,pad.DPadUp.IsHeld,pad.DPadDown.IsHeld,pad.Stick.Y,Clock.Timestamp);
                if(disc)
                {
                    try
                    {
                        if(move>0&&selection+1<paths.Count)selection++;
                        if(move<0&&selection>0)selection--;
                        if(pad.A.WasPressed&&selection<directories)
                        {current=paths[selection];if(current.StartsWith("dvd:/"))current=current.Substring(5);Listing();}
                        if(pad.B.WasPressed&&current.Length>0){current=Path.GetDirectoryName(current)??"";Listing();}
                    }
                    catch(Exception error){status=error.Message;}
                }
                if(selection<top)top=selection;if(selection>=top+8)top=selection-7;
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
                text.Draw(draw,pad.IsConnected?"D-PAD / STICK SELECT  A OPEN DIR  B PARENT":"CONNECT CONTROLLER TO PORT 1",24,430,2,Color32.White);
                text.Draw(draw,"X REFRESH  START EXIT  Z STATS",24,450,2,Color32.White);
                draw.End();device.EndFrame();
            }
        }
        finally{Release();}
    }
}
