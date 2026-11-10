using System;

namespace Dolphin.IO;

/// <summary>Boot GameCube images through an installed Swiss DOL. Swiss owns
/// NKit interpretation, ODE selection, apploader execution and disc patches.</summary>
public static class DiscLauncher
{
    /// <summary>Validate paths and stage Swiss before changing any image files.
    /// imagePath is a full SDK SD-volume path; the image need not exist yet.
    /// RequestLaunch and return successfully from Main to complete handoff.</summary>
    public static PreparedDol PrepareWithSwiss(string swissDolPath,string imagePath)
    {
        string target=GetSwissImagePath(imagePath);
        var executable=DolLauncher.Prepare(swissDolPath);
        try
        {
            executable.SetArguments(swissDolPath,"Autoload="+target);
            return executable;
        }
        catch {executable.Dispose();throw;}
    }

    /// <summary>Map SDK SD volume names to Swiss's device names. Does not mount
    /// media or modify Swiss configuration. Only canonical, full ISO/GCM paths
    /// are accepted to avoid injecting another Swiss configuration argument.</summary>
    public static string GetSwissImagePath(string imagePath)
    {
        if(imagePath==null)throw new ArgumentNullException("imagePath");
        string prefix;int start;
        if(imagePath.StartsWith("gcloader:/")){prefix="gcldr:/";start=10;}
        else if(imagePath.StartsWith("carda:/")){prefix="sda:/";start=7;}
        else if(imagePath.StartsWith("cardb:/")){prefix="sdb:/";start=7;}
        else if(imagePath.StartsWith("sd:/")){prefix="sdc:/";start=4;}
        else throw new ArgumentException("Swiss image boot requires a full mounted SD-volume path.");
        if(imagePath.Length<start+5||imagePath.Length>900)throw new ArgumentException("Invalid Swiss image path.");
        int component=start;
        for(int i=start;i<=imagePath.Length;i++)
        {
            if(i<imagePath.Length){char c=imagePath[i];if(c<32||c==127||c=='\\'||c==':'||c=='='||c=='*'||c=='?')throw new ArgumentException("Invalid Swiss image path.");if(c!='/')continue;}
            int length=i-component;
            if(length==0||length==1&&imagePath[component]=='.'||length==2&&imagePath[component]=='.'&&imagePath[component+1]=='.')throw new ArgumentException("Use canonical Swiss image paths.");
            component=i+1;
        }
        string extension=imagePath.Substring(imagePath.Length-4).ToLowerInvariant();
        if(extension!=".iso"&&extension!=".gcm")throw new ArgumentException("Swiss image boot requires an ISO or GCM.");
        return prefix+imagePath.Substring(start);
    }
}
