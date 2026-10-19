using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Collections.ObjectModel;
using System.Text;
internal static class Program
{
    private static int check;
    private static void Check(bool value){check++;if(!value)throw new InvalidOperationException("Wishlist contract failed.");}
    private sealed class Collision : IEqualityComparer<string>
    {
        public bool Equals(string? a,string? b)=>StringComparer.OrdinalIgnoreCase.Equals(a,b);
        public int GetHashCode(string value)=>int.MinValue;
    }
    private static void Collections()
    {
        var d=new Dictionary<string,string>(0,new Collision());
        for(int i=0;i<70;i++)d.Add("k"+i,"v"+i);
        Check(d.Count==70&&d["K45"]=="v45"&&!d.TryAdd("K45","bad"));
        for(int i=0;i<70;i+=2)Check(d.Remove("k"+i));
        for(int i=0;i<70;i+=2)d["k"+i]="new";
        Check(d.Count==70&&!d.Remove("absent")&&!d.TryGetValue("absent",out var missing)&&missing==null);
        int count=0;foreach(var p in d){Check(p.Key!=null&&p.Value!=null);count++;}Check(count==70);
        Check(d.Keys.Count==70&&((ICollection<string>)d.Values).Contains("new"));
        var copied=new KeyValuePair<string,string>[70];((ICollection<KeyValuePair<string,string>>)d).CopyTo(copied,0);
        d.TrimExcess();Check(d["K45"]=="v45");d.EnsureCapacity(150);Check(d.Count==70);
        var e=d.GetEnumerator();Check(e.MoveNext());d.Add("extra","value");
        try{e.MoveNext();Check(false);}catch(InvalidOperationException){}
        try{var v=d["missing"];Check(false);}catch(KeyNotFoundException){}
        try{d.Add(null!,"bad");Check(false);}catch(ArgumentNullException){}
        d.Clear();Check(d.Count==0);d.Add("again","ok");Check(d["again"]=="ok");
        var set=new HashSet<string?>(StringComparer.OrdinalIgnoreCase);Check(set.Add(null)&&!set.Add(null));
        set.UnionWith(new[]{"A","a","B"});Check(set.Count==3&&set.Contains("b"));
        set.SymmetricExceptWith(new[]{"b","b","C"});Check(!set.Contains("B")&&set.Contains("c")&&set.Count==3);
        set.IntersectWith(new[]{"c",null});Check(set.SetEquals(new[]{"C",null,null}));
        Check(set.IsProperSubsetOf(new[]{"C",null,"D"})&&set.IsProperSupersetOf(new[]{"c"}));
        Check(set.TryGetValue("C",out var actual)&&actual=="C");set.ExceptWith(set);Check(set.Count==0);
        var q=new Queue<int>(3);q.Enqueue(1);q.Enqueue(2);q.Enqueue(3);Check(q.Dequeue()==1);q.Enqueue(4);q.Enqueue(5);
        Check(q.ToArray()[0]==2&&q.ToArray()[3]==5&&q.Contains(4));int sum=0;foreach(int i in q)sum=sum*10+i;Check(sum==2345);
        var qe=q.GetEnumerator();Check(qe.MoveNext());q.Enqueue(6);try{qe.MoveNext();Check(false);}catch(InvalidOperationException){}
        q.Clear();Check(!q.TryDequeue(out _)&&!q.TryPeek(out _));q.TrimExcess();q.Enqueue(7);Check(q.Peek()==7);
        var stack=new Stack<string>(new[]{"a","b","c"});Check(stack.Peek()=="c"&&stack.Pop()=="c");
        string joined="";foreach(var item in stack)joined+=item;Check(joined=="ba");
        stack.TrimExcess();Check(stack.TryPop(out var top)&&top=="b");stack.Clear();Check(!stack.TryPop(out _));
    }
    private static void ArraysAndViews()
    {
        int[] a={8,2,7,2,9};Array.Sort(a);Check(a[0]==2&&a[4]==9&&Array.BinarySearch(a,8)==3&&Array.BinarySearch(a,6)==~2);
        Check(Array.IndexOf(a,2)==0&&Array.LastIndexOf(a,2)==1);Array.Reverse(a,1,3);Check(a[1]==8&&a[3]==2);
        Array.Fill(a,4,1,2);Check(a[1]==4&&a[2]==4);Array.Resize(ref a,8);Check(a.Length==8&&a[5]==0);Array.Resize(ref a,2);Check(a[0]==2&&a[1]==4);
        Check(Array.Exists(a,v=>v==4)&&Array.FindAll(a,v=>v>2).Length==1&&Array.TrueForAll(a,v=>v%2==0));
        Check(Array.ConvertAll(a,v=>v.ToString())[1]=="4");
        int[] keys={3,1,2};string[] values={"three","one","two"};Array.Sort(keys,values);Check(values[0]=="one"&&values[2]=="three");
        var l=new List<string>{"a","b"};var ro=l.AsReadOnly();l.Add("c");Check(ro.Count==3&&ro[2]=="c");
        try{((IList<string>)ro).Add("bad");Check(false);}catch(NotSupportedException){}
        IDictionary<int,string> d=new Dictionary<int,string>();d.Add(2,"two");d[1]="one";
        Check(d.ContainsKey(2)&&d.TryGetValue(1,out string? one)&&one=="one"&&d.Count==2);
        var copy=new Dictionary<int,string>(d);Check(copy[1]=="one");
        IReadOnlyDictionary<int,string> read=new ReadOnlyDictionary<int,string>(d);d.Add(3,"three");Check(read.Count==3&&read[3]=="three");
        var sorted=new SortedList<int,string>();sorted.Add(3,"c");sorted.Add(1,"a");sorted.Add(2,"b");Check(sorted.Keys[0]==1&&sorted.Values[2]=="c");
        int order=0;foreach(var pair in sorted)order=order*10+pair.Key;Check(order==123);
        var tree=new SortedDictionary<int,string>();tree[9]="nine";tree[2]="two";order=0;foreach(var pair in tree)order=order*10+pair.Key;Check(order==29);
        ISet<int> set=new HashSet<int>();set.Add(4);set.UnionWith(new[]{4,6});Check(set.SetEquals(new[]{6,4}));
        object boxed=new KeyValuePair<int,string>(7,"rooted");var pairCopy=(KeyValuePair<int,string>)boxed;Check(pairCopy.Key==7&&pairCopy.Value=="rooted");
    }
    private static int Main(){int stage=1;try{Collections();stage=2;check=0;ArraysAndViews();stage=3;ArrayShapeChecks.Run();stage=4;TextNumberChecks.Run();return 0;}catch(Exception error){Console.WriteLine(error.Message);return stage*1000+(stage==3?ArrayShapeChecks.CheckCount:stage==4?TextNumberChecks.CheckCount:check);}}
}
