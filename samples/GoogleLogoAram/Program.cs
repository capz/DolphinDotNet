using System.Threading.Tasks;
internal static class Program
{
    // HTTP relay remains necessary: this runtime does not implement HTTPS.
    private static Task<int> Main()=>SampleDownload.Run("http://192.168.1.2:18081/google-logo.png",true);
}
