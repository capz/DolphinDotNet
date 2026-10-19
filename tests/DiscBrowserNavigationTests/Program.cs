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
