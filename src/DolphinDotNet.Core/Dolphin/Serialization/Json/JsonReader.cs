using System;
namespace Dolphin.Serialization.Json;
public enum JsonTokenType { None,StartObject,EndObject,StartArray,EndArray,PropertyName,String,Number,True,False,Null }
/// <summary>Bounded, reflection-free pull reader. Call Read per token to control parsing work.</summary>
public sealed class JsonReader
{
    private readonly string text;private readonly byte[] stack=new byte[32];private int position,depth;private bool root;
    public JsonTokenType TokenType {get;private set;}
    public string Value {get;private set;}="";
    public JsonReader(string text){this.text=text??throw new ArgumentNullException("text");if(text.Length>65536)throw new ArgumentException("JSON input exceeds limit.");}
    private void White(){while(position<text.Length&&(text[position]==' '||text[position]=='\n'||text[position]=='\r'||text[position]=='\t'))position++;}
    private void Error(){throw new FormatException("Invalid JSON document.");}
    public bool Read()
    {
        White();Value="";if(depth==0&&root){if(position!=text.Length)Error();TokenType=JsonTokenType.None;return false;}if(position==text.Length)Error();
        if(depth>0){int state=stack[depth-1];if(state==2||state==4){char close=state==2?'}':']';if(text[position]==close){position++;depth--;TokenType=state==2?JsonTokenType.EndObject:JsonTokenType.EndArray;return true;}if(text[position++]!=',')Error();White();stack[depth-1]=(byte)(state==2?5:6);state=stack[depth-1];}
            if(state==0||state==5){if(state==0&&text[position]=='}'){position++;depth--;TokenType=JsonTokenType.EndObject;return true;}if(text[position]!='"')Error();Value=String();TokenType=JsonTokenType.PropertyName;stack[depth-1]=1;return true;}
            if(state==1){if(text[position++]!=':')Error();White();stack[depth-1]=2;}
            else if(state==3||state==6){if(state==3&&text[position]==']'){position++;depth--;TokenType=JsonTokenType.EndArray;return true;}stack[depth-1]=4;}
        }
        if(position>=text.Length)Error();root=true;char next=text[position++];if(next=='{'||next=='['){if(depth==stack.Length)throw new FormatException("JSON nesting exceeds limit.");stack[depth++]=(byte)(next=='{'?0:3);TokenType=next=='{'?JsonTokenType.StartObject:JsonTokenType.StartArray;return true;}
        if(next=='"'){position--;Value=String();TokenType=JsonTokenType.String;return true;}if(next=='t'){Literal("rue");TokenType=JsonTokenType.True;return true;}if(next=='f'){Literal("alse");TokenType=JsonTokenType.False;return true;}if(next=='n'){Literal("ull");TokenType=JsonTokenType.Null;return true;}position--;Value=Number();TokenType=JsonTokenType.Number;return true;
    }
    private void Literal(string suffix){for(int i=0;i<suffix.Length;i++)if(position>=text.Length||text[position++]!=suffix[i])Error();}
    private string String()
    {
        if(text[position++]!='"')Error();var chars=new char[4096];int n=0;bool high=false;while(position<text.Length){char c=text[position++];if(c=='"'){if(high)Error();return new string(chars,0,n);}if(c<' ')Error();if(c=='\\'){if(position==text.Length)Error();char escape=text[position++];if(escape=='u'){if(position>text.Length-4)Error();int value=0;for(int i=0;i<4;i++){char h=text[position++];int d=h>='0'&&h<='9'?h-'0':h>='a'&&h<='f'?h-'a'+10:h>='A'&&h<='F'?h-'A'+10:-1;if(d<0)Error();value=(value<<4)|d;}c=(char)value;}else if(escape=='"'||escape=='\\'||escape=='/')c=escape;else if(escape=='b')c='\b';else if(escape=='f')c='\f';else if(escape=='n')c='\n';else if(escape=='r')c='\r';else if(escape=='t')c='\t';else Error();}
            if(high){if(c<'\udc00'||c>'\udfff')Error();high=false;}else if(c>='\ud800'&&c<='\udbff')high=true;else if(c>='\udc00'&&c<='\udfff')Error();if(n==chars.Length)throw new FormatException("JSON string exceeds limit.");chars[n++]=c;}Error();return "";
    }
    private string Number()
    {
        int start=position;if(position<text.Length&&text[position]=='-')position++;if(position>=text.Length)Error();if(text[position]=='0')position++;else{if(text[position]<'1'||text[position]>'9')Error();Digits();}if(position<text.Length&&text[position]=='.'){position++;int p=position;Digits();if(p==position)Error();}if(position<text.Length&&(text[position]=='e'||text[position]=='E')){position++;if(position<text.Length&&(text[position]=='+'||text[position]=='-'))position++;int p=position;Digits();if(p==position)Error();}if(position-start>128)throw new FormatException("JSON number exceeds limit.");return text.Substring(start,position-start);
    }
    private void Digits(){while(position<text.Length&&text[position]>='0'&&text[position]<='9')position++;}
    public int GetInt32(){if(TokenType!=JsonTokenType.Number)throw new InvalidOperationException();bool minus=Value[0]=='-';int p=minus?1:0;long n=0;for(;p<Value.Length;p++){int d=Value[p]-'0';if(d<0||d>9)throw new FormatException("Number is not an integer.");n=n*10+d;if(n>(minus?2147483648L:2147483647L))throw new OverflowException();}return minus?(int)-n:(int)n;}
}
