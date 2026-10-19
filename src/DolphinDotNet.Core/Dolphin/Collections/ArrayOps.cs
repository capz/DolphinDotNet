using System;
using System.Collections.Generic;
namespace Dolphin.Collections;
/// <summary>Typed array algorithms sharing the collection equality/order contracts.</summary>
public static class ArrayOps
{
    private static void Range<T>(T[] array,int index,int count){if(array==null)throw new ArgumentNullException("array");if(index<0||count<0)throw new ArgumentOutOfRangeException();if(index>array.Length-count)throw new ArgumentException("Invalid range.");}
    private static int Length<T>(T[] array){if(array==null)throw new ArgumentNullException("array");return array.Length;}
    public static int IndexOf<T>(T[] array,T value)=>IndexOf(array,value,0,Length(array));
    public static int IndexOf<T>(T[] array,T value,int startIndex)=>IndexOf(array,value,startIndex,Length(array)-startIndex);
    public static int IndexOf<T>(T[] array,T value,int startIndex,int count){Range(array,startIndex,count);var eq=EqualityComparer<T>.Default;for(int i=startIndex;i<startIndex+count;i++)if(eq.Equals(array[i],value))return i;return -1;}
    public static int LastIndexOf<T>(T[] array,T value)=>LastIndexOf(array,value,Length(array)-1,Length(array));
    public static int LastIndexOf<T>(T[] array,T value,int startIndex)=>LastIndexOf(array,value,startIndex,startIndex+1);
    public static int LastIndexOf<T>(T[] array,T value,int startIndex,int count){int n=Length(array);if(n==0){if(startIndex!=-1&&startIndex!=0||count!=0)throw new ArgumentOutOfRangeException();return -1;}if(startIndex<0||startIndex>=n||count<0||count>startIndex+1)throw new ArgumentOutOfRangeException();var eq=EqualityComparer<T>.Default;for(int i=startIndex;i>startIndex-count;i--)if(eq.Equals(array[i],value))return i;return -1;}
    public static void Reverse<T>(T[] array)=>Reverse(array,0,Length(array));
    public static void Reverse<T>(T[] array,int index,int length){Range(array,index,length);int end=index+length-1;while(index<end){T v=array[index];array[index++]=array[end];array[end--]=v;}}
    public static void Fill<T>(T[] array,T value)=>Fill(array,value,0,Length(array));
    public static void Fill<T>(T[] array,T value,int startIndex,int count){Range(array,startIndex,count);for(int i=startIndex;i<startIndex+count;i++)array[i]=value;}
    public static void Resize<T>(ref T[]? array,int newSize){if(newSize<0)throw new ArgumentOutOfRangeException("newSize");if(array!=null&&array.Length==newSize)return;var result=new T[newSize];if(array!=null)Array.Copy(array,result,array.Length<newSize?array.Length:newSize);array=result;}
    public static int BinarySearch<T>(T[] array,T value)=>BinarySearch(array,0,Length(array),value,null);
    public static int BinarySearch<T>(T[] array,T value,IComparer<T>? comparer)=>BinarySearch(array,0,Length(array),value,comparer);
    public static int BinarySearch<T>(T[] array,int index,int length,T value)=>BinarySearch(array,index,length,value,null);
    public static int BinarySearch<T>(T[] array,int index,int length,T value,IComparer<T>? comparer){Range(array,index,length);comparer=comparer??Comparer<T>.Default;int lo=index,hi=index+length-1;try{while(lo<=hi){int mid=lo+(hi-lo)/2,c=comparer.Compare(array[mid],value);if(c==0)return mid;if(c<0)lo=mid+1;else hi=mid-1;}}catch(Exception){throw new InvalidOperationException("Failed to compare array elements.");}return ~lo;}
    public static void Sort<T>(T[] array)=>Sort(array,0,Length(array),null);
    public static void Sort<T>(T[] array,IComparer<T>? comparer)=>Sort(array,0,Length(array),comparer);
    public static void Sort<T>(T[] array,Comparison<T> comparison){if(comparison==null)throw new ArgumentNullException("comparison");Sort(array,Comparer<T>.Create(comparison));}
    public static void Sort<T>(T[] array,int index,int length)=>Sort(array,index,length,null);
    public static void Sort<T>(T[] array,int index,int length,IComparer<T>? comparer)=>Sort<T,byte>(array,null,index,length,comparer);
    public static void Sort<TKey,TValue>(TKey[] keys,TValue[]? items)=>Sort(keys,items,0,Length(keys),null);
    public static void Sort<TKey,TValue>(TKey[] keys,TValue[]? items,IComparer<TKey>? comparer)=>Sort(keys,items,0,Length(keys),comparer);
    public static void Sort<TKey,TValue>(TKey[] keys,TValue[]? items,int index,int length)=>Sort(keys,items,index,length,null);
    public static void Sort<TKey,TValue>(TKey[] keys,TValue[]? items,int index,int length,IComparer<TKey>? comparer)
    {
        Range(keys,index,length);if(items!=null&&index>items.Length-length)throw new ArgumentException("Items range is too small.");comparer=comparer??Comparer<TKey>.Default;
        try{for(int i=length/2-1;i>=0;i--)Sift(keys,items,index,i,length,comparer);for(int end=length-1;end>0;end--){Swap(keys,items,index,index+end);Sift(keys,items,index,0,end,comparer);}}
        catch(Exception){throw new InvalidOperationException("Failed to compare array elements.");}
    }
    private static void Swap<TKey,TValue>(TKey[] keys,TValue[]? items,int a,int b){TKey key=keys[a];keys[a]=keys[b];keys[b]=key;if(items!=null){TValue value=items[a];items[a]=items[b];items[b]=value;}}
    private static void Sift<TKey,TValue>(TKey[] keys,TValue[]? items,int start,int root,int count,IComparer<TKey> comparer){while(root<count/2){int child=root*2+1;if(child+1<count&&comparer.Compare(keys[start+child],keys[start+child+1])<0)child++;if(comparer.Compare(keys[start+root],keys[start+child])>=0)return;Swap(keys,items,start+root,start+child);root=child;}}
    public static bool Exists<T>(T[] array,Predicate<T> match)=>FindIndex(array,match)>=0;
    public static T Find<T>(T[] array,Predicate<T> match){int i=FindIndex(array,match);return i<0?default!:array[i];}
    public static T FindLast<T>(T[] array,Predicate<T> match){int i=FindLastIndex(array,match);return i<0?default!:array[i];}
    public static T[] FindAll<T>(T[] array,Predicate<T> match){int n=Length(array);if(match==null)throw new ArgumentNullException("match");var result=new List<T>();for(int i=0;i<n;i++)if(match(array[i]))result.Add(array[i]);return result.ToArray();}
    public static int FindIndex<T>(T[] array,Predicate<T> match)=>FindIndex(array,0,Length(array),match);
    public static int FindIndex<T>(T[] array,int startIndex,Predicate<T> match)=>FindIndex(array,startIndex,Length(array)-startIndex,match);
    public static int FindIndex<T>(T[] array,int startIndex,int count,Predicate<T> match){Range(array,startIndex,count);if(match==null)throw new ArgumentNullException("match");for(int i=startIndex;i<startIndex+count;i++)if(match(array[i]))return i;return -1;}
    public static int FindLastIndex<T>(T[] array,Predicate<T> match)=>FindLastIndex(array,Length(array)-1,Length(array),match);
    public static int FindLastIndex<T>(T[] array,int startIndex,Predicate<T> match)=>FindLastIndex(array,startIndex,startIndex+1,match);
    public static int FindLastIndex<T>(T[] array,int startIndex,int count,Predicate<T> match){int n=Length(array);if(startIndex<0||startIndex>=n){if(n!=0||startIndex!=-1)throw new ArgumentOutOfRangeException();}if(count<0||count>startIndex+1)throw new ArgumentOutOfRangeException();if(match==null)throw new ArgumentNullException("match");for(int i=startIndex;i>startIndex-count;i--)if(match(array[i]))return i;return -1;}
    public static bool TrueForAll<T>(T[] array,Predicate<T> match){int n=Length(array);if(match==null)throw new ArgumentNullException("match");for(int i=0;i<n;i++)if(!match(array[i]))return false;return true;}
    public static void ForEach<T>(T[] array,Action<T> action){int n=Length(array);if(action==null)throw new ArgumentNullException("action");for(int i=0;i<n;i++)action(array[i]);}
    public static TOutput[] ConvertAll<TInput,TOutput>(TInput[] array,Converter<TInput,TOutput> converter){int n=Length(array);if(converter==null)throw new ArgumentNullException("converter");var output=new TOutput[n];for(int i=0;i<n;i++)output[i]=converter(array[i]);return output;}
}
