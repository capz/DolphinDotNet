using Dolphin.Storage;
int checks=0;
void Check(bool ok){checks++;if(!ok)throw new Exception("Check "+checks);}
var media=new Card();var store=new CardRecordStore(media,true);
store.Write("town",new byte[15000]);store.Write("player",new byte[]{1,2,3});store.Write("options",new byte[]{4,5});
var before=media.Copy();
// Exhaust every byte boundary of a torn sector, for each data/checkpoint write.
for(int operation=0;operation<2;operation++)for(int cut=0;cut<=8192;cut++){
    var card=before.Copy();var s=new CardRecordStore(card);card.FaultWrite=operation;card.Cut=cut;
    try{s.Write("options",new byte[]{6,7});}catch(IOException){}
    card.FaultWrite=-1;var reopened=new CardRecordStore(card);
    var b=reopened.Read("options");Check(b.SequenceEqual(new byte[]{4,5})||b.SequenceEqual(new byte[]{6,7}));
    Check(reopened.Read("player").SequenceEqual(new byte[]{1,2,3}));Check(reopened.Read("town").Length==15000);
}
store=new CardRecordStore(media);
for(int i=0;i<500;i++){store.Write("options",new byte[]{(byte)i});Check(store.Read("options")[0]==(byte)i);}
Check(media.Writes<1100); // No rewrite of the 15 KiB town for tiny changes.
store.Delete("player");Check(!new CardRecordStore(media).Names().Contains("player"));
store.Write("empty",Array.Empty<byte>());Check(store.Read("empty").Length==0);
var exhausted=new Card(6);var tiny=new CardRecordStore(exhausted,true);tiny.Write("town",new byte[16000]);tiny.Write("second",new byte[16000]);
bool failed=false;try{tiny.Write("third",new byte[]{1});}catch(IOException){failed=true;}
Check(failed&&new CardRecordStore(exhausted).Read("town").Length==16000);
// An invalid newest checkpoint must retain its last complete predecessor.
media=before.Copy();store=new CardRecordStore(media);store.Write("options",new byte[]{9});
int newest=media.LastBlock;media.Data[newest][11]^=1;store=new CardRecordStore(media);Check(store.Read("options").SequenceEqual(new byte[]{4,5}));
Console.WriteLine(checks+" record-store checks passed: every torn-sector boundary, neighbors, packing, bounded small writes, checkpoint fallback, deletion and exhaustion.");
sealed class Card : ICardBlockStorage
{
    public byte[][] Data;public int FaultWrite=-1,Cut,Writes,LastBlock;
    public int Blocks=>Data.Length;
    public Card(int n=16){Data=new byte[n][];for(int i=0;i<n;i++)Data[i]=new byte[8192];}
    public Card Copy(){var c=new Card(Blocks);for(int i=0;i<Blocks;i++)Array.Copy(Data[i],c.Data[i],8192);return c;}
    public void Read(int block,byte[] bytes){Array.Copy(Data[block],bytes,8192);}
    public void Write(int block,byte[] bytes){LastBlock=block;Writes++;if(FaultWrite--==0){Array.Copy(bytes,Data[block],Cut);throw new IOException("Power cut");}Array.Copy(bytes,Data[block],8192);}
}
