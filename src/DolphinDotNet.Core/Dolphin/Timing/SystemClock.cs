using System;
using System.IO;

namespace Dolphin.Timing;

/// <summary>Local console calendar time, as displayed by the GameCube IPL.
/// No timezone or UTC conversion is implied.</summary>
public readonly struct CalendarTime
{
    public int Year {get;} public int Month {get;} public int Day {get;}
    public int Hour {get;} public int Minute {get;} public int Second {get;}
    public CalendarTime(int year,int month,int day,int hour,int minute,int second=0)
    {
        if(year<2000||year>2099||month<1||month>12||day<1||day>DaysInMonth(year,month)||hour<0||hour>23||minute<0||minute>59||second<0||second>59)
            throw new ArgumentOutOfRangeException("calendar");
        Year=year;Month=month;Day=day;Hour=hour;Minute=minute;Second=second;
    }
    public static int DaysInMonth(int year,int month)
    {
        if(year<2000||year>2099||month<1||month>12)throw new ArgumentOutOfRangeException();
        return month==2?(year%4==0?29:28):month==4||month==6||month==9||month==11?30:31;
    }
    internal long Seconds
    {
        get{
            if(Year<2000)throw new ArgumentOutOfRangeException("calendar");
            int days=0;for(int y=2000;y<Year;y++)days+=y%4==0?366:365;
            for(int m=1;m<Month;m++)days+=DaysInMonth(Year,m);
            return 946684800L+(days+Day-1)*86400L+Hour*3600+Minute*60+Second;
        }
    }
    internal static CalendarTime FromSeconds(long seconds)
    {
        if(seconds<946684800L||seconds>=4102444800L)throw new IOException("Console clock is outside 2000–2099.");
        long relative=seconds-946684800L;int days=(int)(relative/86400),remaining=(int)(relative%86400),year=2000,month=1;
        while(days>=(year%4==0?366:365)){days-=year%4==0?366:365;year++;}
        while(days>=DaysInMonth(year,month)){days-=DaysInMonth(year,month);month++;}
        return new CalendarTime(year,month,days+1,remaining/3600,remaining/60%60,remaining%60);
    }
}

/// <summary>The hardware RTC plus persistent IPL SRAM counter bias. Writes change
/// console wall time, not the monotonic Clock or the host machine's clock.</summary>
public static class SystemClock
{
    public static CalendarTime Read()
    {
        long value=NativeTiming.CalendarRead();
        if(value<0)throw new IOException("The console RTC could not be read.");
        return CalendarTime.FromSeconds(value);
    }
    public static void Write(CalendarTime value)
    {
        int result=NativeTiming.CalendarWrite(value.Seconds);
        if(result<0)throw new IOException(result==-2?"The console clock changed, but its SRAM write has not completed. Read the clock again before retrying.":"The console clock could not be written.");
    }
}
