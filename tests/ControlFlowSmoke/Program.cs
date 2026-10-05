using System;

public static class Program
{
    public static int Main()
    {
        var i=0;
        var sum=0;
        while(i<5)
        {
            sum=sum+i;
            i=i+1;
        }
        return sum;
    }
}
