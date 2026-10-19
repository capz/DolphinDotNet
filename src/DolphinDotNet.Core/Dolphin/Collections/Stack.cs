using System;
using System.Collections;
using System.Collections.Generic;
namespace Dolphin.Collections;
public class Stack<T> : IEnumerable<T>, IReadOnlyCollection<T>
{
    private T[] items;private int count,version;
    public Stack():this(0){}
    public Stack(int capacity){if(capacity<0)throw new ArgumentOutOfRangeException("capacity");items=new T[capacity];}
    public Stack(IEnumerable<T> source):this(){if(source==null)throw new ArgumentNullException("source");foreach(var item in source)Push(item);}
    public int Count=>count;
    public void Push(T item){if(count==items.Length)EnsureCapacity(count==0?4:count>int.MaxValue/2?int.MaxValue:count*2);items[count++]=item;version++;}
    public T Pop(){if(count==0)throw new InvalidOperationException("Stack is empty.");T item=items[--count];items[count]=default!;version++;return item;}
    public bool TryPop(out T item){if(count==0){item=default!;return false;}item=Pop();return true;}
    public T Peek(){if(count==0)throw new InvalidOperationException("Stack is empty.");return items[count-1];}
    public bool TryPeek(out T item){if(count==0){item=default!;return false;}item=Peek();return true;}
    public void Clear(){Array.Clear(items,0,count);count=0;version++;}
    public bool Contains(T item){var eq=EqualityComparer<T>.Default;for(int i=0;i<count;i++)if(eq.Equals(items[i],item))return true;return false;}
    public void CopyTo(T[] array,int index){Dictionary<T,byte>.CheckCopy(array,index,count);for(int i=0;i<count;i++)array[index+i]=items[count-i-1];}
    public T[] ToArray(){var result=new T[count];CopyTo(result,0);return result;}
    public int EnsureCapacity(int capacity){if(capacity<0)throw new ArgumentOutOfRangeException("capacity");if(capacity>items.Length){var copy=new T[capacity];Array.Copy(items,copy,count);items=copy;version++;}return items.Length;}
    public void TrimExcess(){if(count<items.Length*9/10){var copy=new T[count];Array.Copy(items,copy,count);items=copy;version++;}}
    public Enumerator GetEnumerator()=>new Enumerator(this);
    IEnumerator<T> IEnumerable<T>.GetEnumerator()=>GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
    public struct Enumerator : IEnumerator<T>
    {
        private readonly Stack<T> owner;private readonly int version;private int index;private T current;
        internal Enumerator(Stack<T> owner){this.owner=owner;version=owner.version;index=-1;current=default!;}
        public T Current{get{if(index<0||index>=owner.count)throw new InvalidOperationException();return current;}}
        object IEnumerator.Current=>Current!;
        public bool MoveNext(){if(version!=owner.version)throw new InvalidOperationException("Collection was modified.");if(index<owner.count)index++;if(index<owner.count){current=owner.items[owner.count-index-1];return true;}current=default!;return false;}
        public void Reset(){if(version!=owner.version)throw new InvalidOperationException();index=-1;current=default!;}
        public void Dispose(){}
    }
}
