using Dolphin.IO;
internal static class Program {
 static int Main(){
  using var executable=DolLauncher.Prepare(ArgvPayload.Data);
  executable.SetArguments("swiss-gc.dol","Autoload=gcldr:/games/Fixture Game.iso","AutoBoot=Yes","Force PAL Video Mode=576p","caption=\u00fc");
  DolLauncher.RequestLaunch(executable);
  return 0;
 }
}
