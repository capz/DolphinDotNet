using System;
using System.Collections;
using System.Collections.Generic;
namespace Dolphin.Text;
public sealed class CharEnumerator : IEnumerator<char>, ICloneable
{
    private readonly string text;private int index=-1;
    internal CharEnumerator(string text){this.text=text;}
    public char Current {get{if(index<0||index>=text.Length)throw new InvalidOperationException();return text[index];}}
    object IEnumerator.Current=>Current;
    public bool MoveNext(){if(index<text.Length)index++;return index<text.Length;}
    public void Reset(){index=-1;}
    public void Dispose(){}
    public object Clone(){var copy=new CharEnumerator(text);copy.index=index;return copy;}
}
