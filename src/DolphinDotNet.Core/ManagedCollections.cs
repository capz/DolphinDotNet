using System;

namespace DolphinDotNet.Collections;

public sealed class ManagedList<T>
{
    private T[] items; public int Count { get; private set; }
    public ManagedList(int capacity=4) => items=new T[capacity<1?1:capacity];
    public T this[int index] { get { Check(index); return items[index]; } set { Check(index); items[index]=value; } }
    public void Add(T item) { Ensure(Count+1); items[Count++]=item; }
    public T RemoveLast() { if(Count==0)throw new InvalidOperationException();var i=--Count;var value=items[i];items[i]=default!;return value; }
    public void Clear() { for(var i=0;i<Count;i++)items[i]=default!;Count=0; }
    private void Check(int index) { if((uint)index>=(uint)Count)throw new IndexOutOfRangeException(); }
    private void Ensure(int count) { if(count<=items.Length)return;var next=new T[items.Length*2];for(var i=0;i<Count;i++)next[i]=items[i];items=next; }
}

public sealed class ManagedQueue<T>
{
    private T[] items=new T[4]; private int head,tail; public int Count{get;private set;}
    public void Enqueue(T value){if(Count==items.Length)Grow();items[tail]=value;tail=(tail+1)%items.Length;Count++;}
    public T Dequeue(){if(Count==0)throw new InvalidOperationException();var value=items[head];items[head]=default!;head=(head+1)%items.Length;Count--;return value;}
    private void Grow(){var next=new T[items.Length*2];for(var i=0;i<Count;i++)next[i]=items[(head+i)%items.Length];items=next;head=0;tail=Count;}
}

public sealed class ManagedStack<T>
{
    private readonly ManagedList<T> list=new();
    public int Count=>list.Count;
    public void Push(T value)=>list.Add(value);
    public T Pop()=>list.RemoveLast();
}
