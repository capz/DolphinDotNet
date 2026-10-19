// .NET 8 bit-pattern digests for a fixed seeded sequence, including overflow.
using System;
internal static class DecimalStress
{
    private static readonly uint[] Expected={
        2534818690U,
        2291791276U,
        2445342463U,
        1957009673U,
        323385674U,
        1286479840U,
        698248354U,
        104525789U,
        1075549236U,
        1357913302U,
        2671190738U,
        2138511860U,
        3685260340U,
        3232751500U,
        2874180346U,
        1105371380U,
        1338438360U,
        464095747U,
        2107605962U,
        3754302030U,
        2186700704U,
        2344475564U,
        2442866825U,
        1168207899U,
        2895441814U,
        987898572U,
        2850463256U,
        946651730U,
        2395803977U,
        959750658U,
        894218233U,
        1011750190U,
    };
    internal static void Run()
    {
        var random=new Random(125713);
        for(int row=0;row<Expected.Length;row++){
            decimal a=new decimal(random.Next(),random.Next(),random.Next(),random.Next(2)==1,(byte)random.Next(29));
            decimal b=new decimal(random.Next(),random.Next(),random.Next(),random.Next(2)==1,(byte)random.Next(29));
            uint hash=2166136261;
            for(int op=0;op<5;op++)try{
                decimal n=op==0?a+b:op==1?a-b:op==2?a*b:op==3?a/b:a%b;
                foreach(int bit in decimal.GetBits(n))hash=unchecked((hash^(uint)bit)*16777619);
            }catch(OverflowException){hash=unchecked((hash^0xdeadbeef)*16777619);}
            if(hash!=Expected[row])throw new InvalidOperationException("Decimal stress vector "+row);
        }
    }
}
