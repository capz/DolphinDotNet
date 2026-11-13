using System;
using Dolphin.System;

// Invented bank metadata only. No retail bank, sequences or wave data.
internal static class MenuBankFixture
{
    internal static byte[] Create(int optionRelease=60)
    {
        var bank=new byte[1024];
        void Big(int at,int value)=>FirmwareMenuRecipe.Put(bank,at,value);
        void Tag(int at,string value){for(int i=0;i<value.Length;i++)bank[at+i]=(byte)value[i];}
        Tag(0,"IBNK");Big(4,bank.Length);Tag(32,"BANK");
        int[] programs={3,7,9},ids={6,9,11};
        for(int i=0;i<3;i++)
        {
            int instrument=0x240+i*64,key=0x300+i*16,velocity=0x340+i*16,oscillator=0x380+i*16,release=0x3c0+i*8;
            Big(36+programs[i]*4,instrument);Tag(instrument,"INST");
            Big(instrument+8,0x3f800000);Big(instrument+12,0x3f800000);Big(instrument+16,oscillator);
            Big(instrument+40,1);Big(instrument+44,key);bank[key]=127;Big(key+4,1);Big(key+8,velocity);
            bank[velocity]=127;Big(velocity+4,ids[i]);Big(velocity+8,0x3f800000);Big(velocity+12,0x3f800000);
            Big(oscillator+12,release);bank[release+1]=0; // Linear release in generated vectors.
            int time=i==0?40:i==1?60:optionRelease;bank[release+2]=(byte)(time>>8);bank[release+3]=(byte)time;
        }
        return bank;
    }
}
