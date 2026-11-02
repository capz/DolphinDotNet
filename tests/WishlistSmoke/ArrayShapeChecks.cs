using System;
internal static class ArrayShapeChecks
{
    private struct Pair {public string Text;public int Number;}
    internal static int CheckCount;
    private static void Check(bool condition){CheckCount++;if(!condition)throw new InvalidOperationException("Multidimensional array contract failed.");}
    internal static void Run()
    {
        byte[] bytes=new byte[]{1,2,3};var byteCopy=(byte[])bytes.Clone();
        byteCopy[0]=9;Check(bytes[0]==1&&byteCopy[0]==9&&!object.ReferenceEquals(bytes,byteCopy));
        object boxedBytes=byteCopy;Check(boxedBytes is byte[]&&boxedBytes is not int[]);
        bool wrongType=false;try{var invalid=(int[])boxedBytes;}catch(InvalidCastException){wrongType=true;}Check(wrongType);
        var emptyVector=new int[0];Check(!object.ReferenceEquals(emptyVector,emptyVector.Clone()));
        int[,] matrix=new int[3,4];Check(matrix.Length==12&&matrix.LongLength==12&&matrix.Rank==2);
        Check(matrix.GetLength(0)==3&&matrix.GetLength(1)==4&&matrix.GetLowerBound(1)==0&&matrix.GetUpperBound(0)==2);
        for(int y=0;y<3;y++)for(int x=0;x<4;x++)matrix[y,x]=y*10+x;
        int sum=0;foreach(int value in matrix)sum+=value;Check(sum==138);
        ref int cell=ref matrix[1,2];cell=99;Check(matrix[1,2]==99);
        double[,,] cube=new double[2,2,2];cube[1,0,1]=1.25;Check(cube[1,0,1]==1.25&&cube.Rank==3);
        var references=new string[2,3];references[1,2]="rooted "+matrix[1,2];
        var values=new Pair[2,2];values[1,0]=new Pair{Text="embedded reference",Number=41};values[1,0].Number++;
        // Allocations force collection while shaped reference/value arrays stay live.
        for(int i=0;i<60;i++){var garbage=new object[20];garbage[0]=i;}
        Check(references[1,2]=="rooted 99"&&values[1,0].Text=="embedded reference"&&values[1,0].Number==42);
        var matrixCopy=(int[,])matrix.Clone();matrixCopy[1,2]=7;
        Check(matrix[1,2]==99&&matrixCopy[1,2]==7&&matrixCopy.Rank==2&&matrixCopy.GetLength(1)==4);
        var referenceCopy=(string[,])references.Clone();
        Check(object.ReferenceEquals(referenceCopy[1,2],references[1,2]));
        referenceCopy[1,2]="changed";Check(references[1,2]=="rooted 99");
        var valueCopy=(Pair[,])values.Clone();valueCopy[1,0].Number=8;
        Check(values[1,0].Number==42&&valueCopy[1,0].Number==8&&object.ReferenceEquals(values[1,0].Text,valueCopy[1,0].Text));
        object[,] covariant=referenceCopy;
        bool cloneRejected=false;try{covariant[0,0]=new InvalidOperationException();}catch(Exception){cloneRejected=true;}Check(cloneRejected);
        covariant=references;covariant[0,1]="safe";Check(references[0,1]=="safe");
        bool rejected=false;try{covariant[0,1]=new InvalidOperationException();}catch(Exception){rejected=true;}Check(rejected&&references[0,1]=="safe");
        var empty=new int[3,0];Check(empty.Length==0&&empty.GetLength(0)==3&&empty.GetUpperBound(1)==-1);
        try{var bad=matrix[3,0];Check(false);}catch(IndexOutOfRangeException){}
        try{matrix[-1,2]=1;Check(false);}catch(IndexOutOfRangeException){}
        try{matrix.GetLength(2);Check(false);}catch(IndexOutOfRangeException){}
        int[] nullVector=null!;try{nullVector.Clone();Check(false);}catch(NullReferenceException){}
        int[,] nil=null!;try{var bad=nil[0,0];Check(false);}catch(NullReferenceException){}
    }
}
