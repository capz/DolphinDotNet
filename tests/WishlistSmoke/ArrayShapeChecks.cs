using System;
internal static class ArrayShapeChecks
{
    private struct Pair {public string Text;public int Number;}
    internal static int CheckCount;
    private static void Check(bool condition){CheckCount++;if(!condition)throw new InvalidOperationException("Multidimensional array contract failed.");}
    internal static void Run()
    {
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
        object[,] covariant=references;covariant[0,1]="safe";Check(references[0,1]=="safe");
        bool rejected=false;try{covariant[0,1]=new InvalidOperationException();}catch(Exception){rejected=true;}Check(rejected&&references[0,1]=="safe");
        var empty=new int[3,0];Check(empty.Length==0&&empty.GetLength(0)==3&&empty.GetUpperBound(1)==-1);
        try{var bad=matrix[3,0];Check(false);}catch(IndexOutOfRangeException){}
        try{matrix[-1,2]=1;Check(false);}catch(IndexOutOfRangeException){}
        try{matrix.GetLength(2);Check(false);}catch(IndexOutOfRangeException){}
        int[,] nil=null!;try{var bad=nil[0,0];Check(false);}catch(NullReferenceException){}
    }
}
