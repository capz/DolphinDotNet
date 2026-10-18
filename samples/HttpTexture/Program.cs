using System.Threading.Tasks;
internal static class Program
{
    private static Task<int> Main()=>SampleDownload.Run("http://dlptest.com/dlp-test-logo.png",false);
}
