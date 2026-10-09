using System;
using System.Collections;
using System.Collections.Generic;
namespace Dolphin.Collections;

public class List<T> : IList<T>, IReadOnlyList<T>
{
    private T[] items;
    private int count;
    private int version;
    public List():this(0){}
    public List(int capacity){if(capacity<0)throw new ArgumentOutOfRangeException("capacity");items=new T[capacity];}
    public List(IEnumerable<T> source):this(){AddRange(source);}
    public int Count=>count;
    public int Capacity {get=>items.Length;set {if(value<count)throw new ArgumentOutOfRangeException("value");if(value!=items.Length){var next=new T[value];Array.Copy(items,0,next,0,count);items=next;}}}
    public bool IsReadOnly=>false;
    public T this[int index] {get {Check(index);return items[index];}set {Check(index);items[index]=value;version++;}}
    private void Check(int index){if(index<0||index>=count)throw new ArgumentOutOfRangeException("index");}
    private void Range(int index,int length){if(index<0||length<0)throw new ArgumentOutOfRangeException();if(index>count-length)throw new ArgumentException("Invalid range.");}
    private void Ensure(int size){if(size<0)throw new OutOfMemoryException();if(size<=items.Length)return;var capacity=items.Length==0?4:items.Length>1073741823?int.MaxValue:items.Length*2;if(capacity<size)capacity=size;Capacity=capacity;}
    public void Add(T item){Ensure(count+1);items[count++]=item;version++;}
    public void Clear(){Array.Clear(items,0,count);count=0;version++;}
    public int IndexOf(T item)=>IndexOf(item,0,count);
    public int IndexOf(T item,int index)=>IndexOf(item,index,count-index);
    public int IndexOf(T item,int index,int length){Range(index,length);var eq=EqualityComparer<T>.Default;for(var i=index;i<index+length;i++)if(eq.Equals(items[i],item))return i;return -1;}
    public int LastIndexOf(T item){if(count==0)return -1;return LastIndexOf(item,count-1,count);}
    public int LastIndexOf(T item,int index)=>LastIndexOf(item,index,index+1);
    public int LastIndexOf(T item,int index,int length){if(count==0){if(index!=-1||length!=0)throw new ArgumentOutOfRangeException();return -1;}Check(index);if(length<0||length>index+1)throw new ArgumentOutOfRangeException("count");var eq=EqualityComparer<T>.Default;for(var i=index;i>index-length;i--)if(eq.Equals(items[i],item))return i;return -1;}
    public bool Contains(T item)=>IndexOf(item)>=0;
    public void Insert(int index,T item){if(index<0||index>count)throw new ArgumentOutOfRangeException("index");Ensure(count+1);Array.Copy(items,index,items,index+1,count-index);items[index]=item;count++;version++;}
    public void AddRange(IEnumerable<T> source)=>InsertRange(count,source);
    public void InsertRange(int index,IEnumerable<T> source){if(source==null)throw new ArgumentNullException("source");if(index<0||index>count)throw new ArgumentOutOfRangeException("index");var copy=new List<T>();foreach(var item in source)copy.Add(item);if(copy.count==0)return;if(copy.count>int.MaxValue-count)throw new OutOfMemoryException();Ensure(count+copy.count);Array.Copy(items,index,items,index+copy.count,count-index);Array.Copy(copy.items,0,items,index,copy.count);count+=copy.count;version++;}
    public void RemoveAt(int index){Check(index);RemoveRange(index,1);}
    public void RemoveRange(int index,int length){Range(index,length);if(length==0)return;Array.Copy(items,index+length,items,index,count-index-length);count-=length;Array.Clear(items,count,length);version++;}
    public bool Remove(T item){var index=IndexOf(item);if(index<0)return false;RemoveAt(index);return true;}
    public int RemoveAll(Predicate<T> match){if(match==null)throw new ArgumentNullException("match");var target=0;for(var i=0;i<count;i++)if(!match(items[i]))items[target++]=items[i];var removed=count-target;if(removed>0){Array.Clear(items,target,removed);count=target;version++;}return removed;}
    public void CopyTo(T[] array)=>CopyTo(array,0);
    public void CopyTo(T[] array,int arrayIndex)=>CopyTo(0,array,arrayIndex,count);
    public void CopyTo(int index,T[] array,int arrayIndex,int length){if(array==null)throw new ArgumentNullException("array");Range(index,length);if(arrayIndex<0)throw new ArgumentOutOfRangeException("arrayIndex");if(arrayIndex>array.Length-length)throw new ArgumentException("Destination is too small.");Array.Copy(items,index,array,arrayIndex,length);}
    public T[] ToArray(){var result=new T[count];CopyTo(result,0);return result;}
    public List<T> GetRange(int index,int length){Range(index,length);var result=new List<T>(length);Array.Copy(items,index,result.items,0,length);result.count=length;return result;}
    public void TrimExcess(){if(count<items.Length*9/10)Capacity=count;}
    public bool Exists(Predicate<T> match)=>FindIndex(match)>=0;
    public T Find(Predicate<T> match){var i=FindIndex(match);return i<0?default!:items[i];}
    public T FindLast(Predicate<T> match){var i=FindLastIndex(match);return i<0?default!:items[i];}
    public List<T> FindAll(Predicate<T> match){if(match==null)throw new ArgumentNullException("match");var result=new List<T>();foreach(var item in this)if(match(item))result.Add(item);return result;}
    public int FindIndex(Predicate<T> match)=>FindIndex(0,count,match);
    public int FindIndex(int startIndex,Predicate<T> match)=>FindIndex(startIndex,count-startIndex,match);
    public int FindIndex(int startIndex,int length,Predicate<T> match){if(match==null)throw new ArgumentNullException("match");Range(startIndex,length);for(var i=startIndex;i<startIndex+length;i++)if(match(items[i]))return i;return -1;}
    public int FindLastIndex(Predicate<T> match)=>FindLastIndex(count-1,count,match);
    public int FindLastIndex(int startIndex,Predicate<T> match)=>FindLastIndex(startIndex,startIndex+1,match);
    public int FindLastIndex(int startIndex,int length,Predicate<T> match){if(match==null)throw new ArgumentNullException("match");if((count==0?startIndex!=-1:startIndex<0||startIndex>=count)||length<0||length>startIndex+1)throw new ArgumentOutOfRangeException();for(var i=startIndex;i>startIndex-length;i--)if(match(items[i]))return i;return -1;}
    public bool TrueForAll(Predicate<T> match){if(match==null)throw new ArgumentNullException("match");for(var i=0;i<count;i++)if(!match(items[i]))return false;return true;}
    public void ForEach(Action<T> action){if(action==null)throw new ArgumentNullException("action");var v=version;for(var i=0;i<count;i++){if(v!=version)throw new InvalidOperationException("Collection was modified.");action(items[i]);}if(v!=version)throw new InvalidOperationException("Collection was modified.");}
    public void Reverse()=>Reverse(0,count);
    public void Reverse(int index,int length){Range(index,length);var end=index+length-1;while(index<end){var item=items[index];items[index++]=items[end];items[end--]=item;}version++;}
    public void Sort()=>Sort(0,count,null);
    public void Sort(IComparer<T>? comparer)=>Sort(0,count,comparer);
    public void Sort(Comparison<T> comparison){if(comparison==null)throw new ArgumentNullException("comparison");Sort(Comparer<T>.Create(comparison));}
    public void Sort(int index,int length,IComparer<T>? comparer){Range(index,length);comparer=comparer??Comparer<T>.Default;HeapSort(index,length,comparer);version++;}
    private void HeapSort(int start,int length,IComparer<T> comparer){for(var i=length/2-1;i>=0;i--)Sift(start,i,length,comparer);for(var end=length-1;end>0;end--){var item=items[start];items[start]=items[start+end];items[start+end]=item;Sift(start,0,end,comparer);}}
    private void Sift(int start,int root,int length,IComparer<T> comparer){while(root<length/2){var child=root*2+1;if(child+1<length&&comparer.Compare(items[start+child],items[start+child+1])<0)child++;if(comparer.Compare(items[start+root],items[start+child])>=0)return;var item=items[start+root];items[start+root]=items[start+child];items[start+child]=item;root=child;}}
    public int BinarySearch(T item)=>BinarySearch(0,count,item,null);
    public int BinarySearch(T item,IComparer<T>? comparer)=>BinarySearch(0,count,item,comparer);
    public int BinarySearch(int index,int length,T item,IComparer<T>? comparer){Range(index,length);comparer=comparer??Comparer<T>.Default;var low=index;var high=index+length-1;while(low<=high){var mid=low+(high-low)/2;var c=comparer.Compare(items[mid],item);if(c==0)return mid;if(c<0)low=mid+1;else high=mid-1;}return ~low;}
    public Enumerator GetEnumerator()=>new Enumerator(this);
    IEnumerator<T> IEnumerable<T>.GetEnumerator()=>GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
    public struct Enumerator : IEnumerator<T>
    {
        private readonly List<T> owner;
        private readonly int version;
        private int index;
        private T current;
        internal Enumerator(List<T> owner){this.owner=owner;version=owner.version;index=0;current=default!;}
        public T Current=>current;
        object IEnumerator.Current {get {if(index==0||index==owner.count+1)throw new InvalidOperationException("Enumerator is not positioned on an item.");return current!;}}
        public bool MoveNext(){if(version!=owner.version)throw new InvalidOperationException("Collection was modified.");if(index<owner.count){current=owner.items[index++];return true;}index=owner.count+1;current=default!;return false;}
        public void Reset(){if(version!=owner.version)throw new InvalidOperationException("Collection was modified.");index=0;current=default!;}
        public void Dispose(){}
    }
}
