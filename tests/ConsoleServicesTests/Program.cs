using System;
using Dolphin.Timing;
namespace Dolphin.Timing {
 internal static class NativeTiming {
  internal static long Now=946684800;internal static int Result,Writes;
  internal static long CalendarRead()=>Now;
  internal static int CalendarWrite(long value){Writes++;Now=value;return Result;}
  internal static int Sound;
  internal static int ConsoleSoundRead()=>Sound;
  internal static int ConsoleSoundWrite(int mode){Sound=mode;return Result;}
 }
}
internal static class Program {
 static void Check(bool value){if(!value)throw new Exception("System calendar test failed");}
 static void Main(){
  for(int year=2000;year<2100;year++)for(int month=1;month<=12;month++){
   int day=DateTime.DaysInMonth(year,month);var clock=new CalendarTime(year,month,day,23,59,59);
   long seconds=new DateTimeOffset(year,month,day,23,59,59,TimeSpan.Zero).ToUnixTimeSeconds();
   Check(clock.Seconds==seconds);var decoded=CalendarTime.FromSeconds(seconds);Check(decoded.Year==year&&decoded.Month==month&&decoded.Day==day&&decoded.Second==59);
  }
  bool invalid=false;try{new CalendarTime(2025,2,29,12,0);}catch(ArgumentOutOfRangeException){invalid=true;}Check(invalid);
  SystemClock.Write(new CalendarTime(2026,11,6,14,30,12));var read=SystemClock.Read();Check(read.Hour==14&&read.Minute==30&&NativeTiming.Writes==1);
  NativeTiming.Result=-2;bool pending=false;try{SystemClock.Write(read);}catch(System.IO.IOException e){pending=e.Message.Contains("pending")||e.Message.Contains("not completed");}Check(pending);
  NativeTiming.Now=-1;bool missing=false;try{SystemClock.Read();}catch(System.IO.IOException){missing=true;}Check(missing);
  NativeTiming.Result=0;Dolphin.System.ConsolePreferences.SoundMode=Dolphin.System.ConsoleSoundMode.Mono;
  Check(Dolphin.System.ConsolePreferences.SoundMode==Dolphin.System.ConsoleSoundMode.Mono);
  bool badSound=false;try{Dolphin.System.ConsolePreferences.SoundMode=(Dolphin.System.ConsoleSoundMode)2;}catch(ArgumentOutOfRangeException){badSound=true;}Check(badSound);
  Console.WriteLine("System calendar: 1200 month-end epoch round trips, leap-day validation, read/write, pending SRAM and missing RTC passed.");
 }
}
