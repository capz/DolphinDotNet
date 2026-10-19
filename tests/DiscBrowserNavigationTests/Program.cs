using System;
var navigation=new BrowserNavigation();
void Check(int expected,bool up=false,bool down=false,float stick=0,long now=0,bool connected=true)
{
    int actual=navigation.Update(connected,up,down,stick,now);
    if(actual!=expected)throw new Exception($"Navigation at {now}: expected {expected}, got {actual}");
}
Check(0,stick:.4f);Check(1,stick:-1);
Check(0,stick:-1,now:349999);Check(1,stick:-1,now:350000);
Check(0,stick:-1,now:449999);Check(1,stick:-1,now:450000);
Check(0,stick:-.4f,now:450001); // Hysteresis retains direction, not a fresh press.
Check(0,now:450002);Check(1,stick:-1,now:450003);
Check(-1,up:true,stick:-1,now:450004); // D-pad overrides the stick.
Check(0,up:true,down:true,now:450005); // Opposing buttons cancel.
Check(1,down:true,now:450006);Check(-1,up:true,now:450007);
Check(0,up:true,now:450008,connected:false);Check(-1,up:true,now:450009);
Check(-1,up:true,now:9000000);Check(0,up:true,now:9000001); // No repeat burst after a stall.
navigation.Reset();Check(-1,up:true,now:9000002);
Console.WriteLine("DiscBrowser stick, D-pad, repeat timing and disconnect navigation passed.");

if(BrowserPath.Relative(true,"gcloader:/Games/PSO.ISO")!="Games/PSO.ISO")throw new Exception("GC Loader path");
if(BrowserPath.Relative(false,"dvd:/dir/file.bin")!="dir/file.bin")throw new Exception("DVD path");
if(BrowserPath.Relative(true,"gcloader:/")!="")throw new Exception("GC Loader root");
if(!BrowserPath.IsDiscImage("gcloader:/GAMES/Game.GCM") ||
   !BrowserPath.IsDiscImage("gcloader:/games/image.iso") ||
   BrowserPath.IsDiscImage("gcloader:/games/readme.txt"))throw new Exception("ISO recognition");
bool rejected=false;
try{BrowserPath.Relative(true,"dvd:/wrong-device");}
catch(ArgumentException){rejected=true;}
if(!rejected)throw new Exception("Cross-device path was accepted");
Console.WriteLine("DiscBrowser GC Loader/DVD paths and ISO selection passed.");
