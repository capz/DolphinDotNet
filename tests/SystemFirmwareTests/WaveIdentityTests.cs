using Dolphin.System;
using System.Text;

internal static class WaveIdentityTests
{
    private static void Check(bool value){if(!value)throw new Exception("WSYS identity assertion");}
    private static void Reject(byte[] data)
    {try{FirmwareWaveSystem.Inspect(data);throw new Exception("Accepted invalid WSYS identity table");}catch(InvalidDataException){}}
    private static void Big(byte[] data,int at,int value)=>FirmwareMenuRecipe.Put(data,at,value);
    private static byte[] Fixture(bool compact)
    {
        var data=new byte[2048];
        void Tag(int at,string tag)=>Encoding.ASCII.GetBytes(tag).CopyTo(data,at);
        Tag(0,"WSYS");Big(data,4,1024);Big(data,16,32);Big(data,20,800);
        Tag(32,"WINF");Big(data,36,2);Big(data,40,64);Big(data,44,256);
        Tag(800,"WBCT");Big(data,808,2);Big(data,812,840);Big(data,816,864);
        for(int bank=0;bank<2;bank++)
        {
            int at=64+bank*192,names=compact?68:112,scene=840+bank*24,cdf=896+bank*24;
            Tag(at,"invented.aw");Big(data,at+names,2);Tag(scene,"SCNE");Big(data,scene+12,cdf);
            Tag(cdf,"C-DF");Big(data,cdf+4,2);
            for(int wave=0;wave<2;wave++)
            {
                int flat=bank*2+wave,descriptor=512+flat*(compact?36:44),record=960+flat*4;
                Big(data,at+names+4+wave*4,descriptor);data[descriptor+1]=3;data[descriptor+2]=(byte)(48+wave*12);
                Big(data,descriptor+4,0x46fa0000);Big(data,descriptor+8,flat*4);Big(data,descriptor+12,4);Big(data,descriptor+28,2);
                Big(data,cdf+8+wave*4,record);Big(data,record,wave==0?37:bank==0?9:8191);
            }
        }
        return data;
    }
    internal static void Run()
    {
        foreach(bool compact in new[]{false,true})
        {
            var data=Fixture(compact);var waves=FirmwareWaveSystem.Inspect(data);
            Check(waves.Length==4&&waves.Select(w=>w.WaveId).SequenceEqual(new[]{37,9,37,8191}));
            Check(waves[1].BaseKey==60&&waves[2].BankIndex==1&&waves[2].BankLocalIndex==0);
            // Duplicate IDs across banks remain bank-qualified; duplicates
            // within one bank are ambiguous and must be rejected.
            var bad=(byte[])data.Clone();Big(bad,964,37);Reject(bad);
            foreach(int pointer in new[]{20,812,852,904})
            {bad=(byte[])data.Clone();Big(bad,pointer,1050);Reject(bad);}
            bad=(byte[])data.Clone();Big(bad,808,1);Reject(bad);
            bad=(byte[])data.Clone();Big(bad,900,3);Reject(bad);
            bad=(byte[])data.Clone();bad[896]=0;Reject(bad);
            bad=(byte[])data.Clone();bad[514]=128;Reject(bad);
            Big(data,20,0);Check(FirmwareWaveSystem.Inspect(data).All(w=>w.WaveId==-1));
        }
        Console.WriteLine("WSYS IDs: compact/extended, bank-qualified reordered IDs, missing tables and bounded malformed maps passed");
    }
}
