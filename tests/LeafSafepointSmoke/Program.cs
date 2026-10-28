using System;

internal sealed class Node
{
    internal int[] Values;
    internal Node(int value){Values=new[]{value,value+1,value+2};}
}
internal static class Program
{
    private static Node root=new Node(40);
    internal static int LeafRead(Node node,int index)=>node.Values[index];
    internal static int LeafBranch(int value)=>value<0?-value:value==0?17:value+1;
    internal static int[] LeafReference(Node node)=>node.Values;
    internal static int Loop(Node node,int count)
    {int total=0;for(int i=0;i<count;i++)total+=LeafRead(node,i%3);return total;}
    internal static int LeafCalls(Node node)=>LeafRead(node,2);
    internal static int Recursive(Node node,int count)=>count==0?LeafRead(node,1):Recursive(node,count-1);
    internal static int StaticRead()=>root.Values[1];
    internal static int Allocating(Node node)=>new Node(LeafRead(node,1)).Values[1];
    internal static int Reassigned(Node node)
    {var old=node;node=new Node(100);var pressure=new Node(200);return old.Values[2]+node.Values[0]+pressure.Values[0];}
    private static void Replace(ref Node node){node=new Node(100);}
    internal static int Addressed(Node node)
    {var old=node;Replace(ref node);var pressure=new Node(200);return old.Values[2]+node.Values[0]+pressure.Values[0];}
    private static int Main()
    {
        var node=new Node(40);
        if(LeafRead(node,2)!=42||LeafBranch(-3)!=3||LeafBranch(0)!=17||LeafBranch(2)!=3)return 1;
        if(Loop(node,30)!=1230||LeafCalls(node)!=42||Recursive(node,3)!=41||StaticRead()!=41||Allocating(node)!=42)return 2;
        try{LeafRead(null,0);return 3;}catch(NullReferenceException){}
        try{LeafRead(node,-1);return 4;}catch(IndexOutOfRangeException){}
        try{LeafRead(node,3);return 5;}catch(IndexOutOfRangeException){}
        // Return a reference from an unpolled leaf, then collect on allocation
        // after its original owner is overwritten. The caller must retain it.
        var saved=LeafReference(node);node=new Node(100);
        for(int i=0;i<100;i++)node=new Node(i);
        if(saved[2]!=42||LeafRead(node,0)!=99)return 6;
        if(Reassigned(new Node(40))!=342||Addressed(new Node(40))!=342)return 7;
        return 0;
    }
}
