using System;
using System.Collections;
using System.Collections.Generic;
namespace Dolphin.Collections;
public class Queue<T> : IEnumerable<T>, IReadOnlyCollection<T>
{
    private T[] items;private int head,count,version;
    public Queue():this(0){}
    public Queue(int capacity){if(capacity<0)throw new ArgumentOutOfRangeException("capacity");items=new T[capacity];}
    public Queue(IEnumerable<T> source):this(){if(source==null)throw new ArgumentNullException("source");foreach(var item in source)Enqueue(item);}
    public int Count=>count;
    public void Enqueue(T item){if(count==items.Length)Grow(count==0?4:count>int.MaxValue/2?int.MaxValue:count*2);items[(head+count)%items.Length]=item;count++;version++;}
    public T Dequeue(){if(count==0)throw new InvalidOperationException("Queue is empty.");T item=items[head];items[head]=default!;head=(head+1)%items.Length;count--;version++;return item;}
    public bool TryDequeue(out T item){if(count==0){item=default!;return false;}item=Dequeue();return true;}
    public T Peek(){if(count==0)throw new InvalidOperationException("Queue is empty.");return items[head];}
    public bool TryPeek(out T item){if(count==0){item=default!;return false;}item=Peek();return true;}
    public void Clear(){Array.Clear(items,0,items.Length);head=count=0;version++;}
    public bool Contains(T item){var eq=EqualityComparer<T>.Default;for(int i=0;i<count;i++)if(eq.Equals(items[(head+i)%items.Length],item))return true;return false;}
    public void CopyTo(T[] array,int index){Dictionary<T,byte>.CheckCopy(array,index,count);for(int i=0;i<count;i++)array[index+i]=items[(head+i)%items.Length];}
    public T[] ToArray(){var result=new T[count];CopyTo(result,0);return result;}
    private void Grow(int capacity){var copy=new T[capacity];CopyTo(copy,0);items=copy;head=0;version++;}
    public int EnsureCapacity(int capacity){if(capacity<0)throw new ArgumentOutOfRangeException("capacity");if(capacity>items.Length)Grow(capacity);return items.Length;}
    public void TrimExcess(){if(count<items.Length*9/10)Grow(count);}
    public Enumerator GetEnumerator()=>new Enumerator(this);
    IEnumerator<T> IEnumerable<T>.GetEnumerator()=>GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
    public struct Enumerator : IEnumerator<T>
    {
        private readonly Queue<T> owner;private readonly int version;private int index;private T current;
        internal Enumerator(Queue<T> owner){this.owner=owner;version=owner.version;index=-1;current=default!;}
        public T Current{get{if(index<0||index>=owner.count)throw new InvalidOperationException();return current;}}
        object IEnumerator.Current=>Current!;
        public bool MoveNext(){if(version!=owner.version)throw new InvalidOperationException("Collection was modified.");if(index<owner.count)index++;if(index<owner.count){current=owner.items[(owner.head+index)%owner.items.Length];return true;}current=default!;return false;}
        public void Reset(){if(version!=owner.version)throw new InvalidOperationException();index=-1;current=default!;}
        public void Dispose(){}
    }
}
