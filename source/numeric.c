#include "dnd_numeric.h"
#include <math.h>
#include <stdlib.h>
#include <string.h>
#include <limits.h>
#include <stdint.h>

double dnd_numeric_unary(int op,double x) {
    switch(op){case 0:return fabs(x);case 1:return sqrt(x);case 2:return sin(x);case 3:return cos(x);case 4:return tan(x);
    case 5:return asin(x);case 6:return acos(x);case 7:return atan(x);case 8:return sinh(x);case 9:return cosh(x);case 10:return tanh(x);
    case 11:return exp(x);case 12:return log(x);case 13:return log10(x);case 14:return floor(x);case 15:return ceil(x);case 16:return trunc(x);default:return NAN;}
}
double dnd_numeric_binary(int op,double a,double b) {
    switch(op){case 0:return pow(a,b);case 1:return atan2(a,b);case 2:return remainder(a,b);
    case 3:if(isnan(a))return a;if(isnan(b))return b;return a==b?(signbit(a)?a:b):(a<b?a:b);
    case 4:if(isnan(a))return a;if(isnan(b))return b;return a==b?(signbit(a)?b:a):(a>b?a:b);default:return NAN;}
}
int dnd_numeric_classify(int op,double x) {
    switch(op){case 0:return isnan(x)!=0;case 1:return isinf(x)!=0;case 2:return isinf(x)&&x>0;case 3:return isinf(x)&&x<0;case 4:return isfinite(x)!=0;default:return 0;}
}
int dnd_numeric_sign(double x) {
    if(isnan(x)){dnd_exception_throw(DND_EXCEPTION_ARITHMETIC,"NaN has no sign.");return 0;}return x<0?-1:x>0?1:0;
}
double dnd_numeric_round(double x,int digits,int mode) {
    if(digits<0||digits>15){dnd_exception_throw(DND_EXCEPTION_ARGUMENT_OUT_OF_RANGE,"Invalid rounding precision.");return 0;}
    if(mode<0||mode>4){dnd_exception_throw(DND_EXCEPTION_ARGUMENT,"Invalid rounding mode.");return 0;}
    static const double powers[]={1,10,100,1000,10000,100000,1000000,10000000,100000000,1000000000,10000000000.,100000000000.,1000000000000.,10000000000000.,100000000000000.,1000000000000000.};
    if(!isfinite(x)||fabs(x)>=1e16)return x;
    double scale=powers[digits],value=x*scale,result;
    if(mode==2)result=trunc(value);else if(mode==3)result=floor(value);else if(mode==4)result=ceil(value);
    else {double absolute=fabs(value),whole=floor(absolute),fraction=absolute-whole;
        if(fraction>.5||(fraction==.5&&(mode==1||fmod(whole,2)!=0)))whole++;
        result=copysign(whole,value);
    }
    return result/scale;
}
static int whitespace(uint16_t c) {return (c>=9&&c<=13)||c==32;}
static int failure(int throwing,DndExceptionKind kind) {if(throwing)dnd_exception_throw(kind,"Invalid numeric input or range.");return 0;}
static int integer(DndString *s,void *out,int kind,int throwing) {
    int wide=kind>=2,uns=kind&1;memset(out,0,wide?8:4);
    if(!s)return failure(throwing,DND_EXCEPTION_ARGUMENT_NULL);
    uint32_t start=0,end=s->length;while(start<end&&whitespace(s->chars[start]))start++;while(end>start&&whitespace(s->chars[end-1]))end--;
    int negative=0;if(start<end&&(s->chars[start]=='+'||s->chars[start]=='-'))negative=s->chars[start++]=='-';
    if(start==end)return failure(throwing,DND_EXCEPTION_FORMAT);
    uint64_t limit=wide?(uns?UINT64_MAX:negative?UINT64_C(9223372036854775808):INT64_MAX):(uns?UINT32_MAX:negative?UINT64_C(2147483648):INT32_MAX);
    uint64_t value=0;int overflow=0;
    for(uint32_t i=start;i<end;i++) {
        unsigned c=s->chars[i];if(c<'0'||c>'9')return failure(throwing,DND_EXCEPTION_FORMAT);
        unsigned digit=c-'0';if(value>limit/10||(value==limit/10&&digit>limit%10))overflow=1;
        if(!overflow)value=value*10+digit;
    }
    if(overflow||(uns&&negative&&value!=0))return failure(throwing,DND_EXCEPTION_OVERFLOW);
    if(negative)value=UINT64_C(0)-value;
    if(wide)memcpy(out,&value,8);else {uint32_t small=(uint32_t)value;memcpy(out,&small,4);}return 1;
}
int dnd_numeric_i32(DndString *s,intptr_t out,int throwing){return integer(s,(void*)out,0,throwing);}
int dnd_numeric_u32(DndString *s,intptr_t out,int throwing){return integer(s,(void*)out,1,throwing);}
int dnd_numeric_i64(DndString *s,intptr_t out,int throwing){return integer(s,(void*)out,2,throwing);}
int dnd_numeric_u64(DndString *s,intptr_t out,int throwing){return integer(s,(void*)out,3,throwing);}
static int ascii_equal(const uint16_t *s,uint32_t n,const char *word) {
    if(n!=strlen(word))return 0;
    for(uint32_t i=0;i<n;i++){unsigned c=s[i];if(c>='A'&&c<='Z')c+=32;if(c!=(unsigned char)word[i])return 0;}return 1;
}
static int floating(DndString *s,void *out,int single,int throwing) {
    memset(out,0,single?4:8);if(!s)return failure(throwing,DND_EXCEPTION_ARGUMENT_NULL);
    uint32_t start=0,end=s->length;while(start<end&&whitespace(s->chars[start]))start++;while(end>start&&whitespace(s->chars[end-1]))end--;
    if(start==end||end-start>4096)return failure(throwing,DND_EXCEPTION_FORMAT);
    uint32_t p=start;int neg=0;if(s->chars[p]=='+'||s->chars[p]=='-')neg=s->chars[p++]=='-';
    double value;
    if(ascii_equal(s->chars+p,end-p,"nan"))value=NAN;
    else if(ascii_equal(s->chars+p,end-p,"infinity"))value=neg?-INFINITY:INFINITY;
    else {
        char buffer[4097];int used=0,digits=0;
        if(neg)buffer[used++]='-';
        while(p<end&&((s->chars[p]>='0'&&s->chars[p]<='9')||(s->chars[p]==','&&digits))){if(s->chars[p]!=','){buffer[used++]=(char)s->chars[p];digits++;}p++;}
        if(p<end&&s->chars[p]=='.'){buffer[used++]='.';p++;while(p<end&&s->chars[p]>='0'&&s->chars[p]<='9'){buffer[used++]=(char)s->chars[p++];digits++;}}
        if(!digits)return failure(throwing,DND_EXCEPTION_FORMAT);
        if(p<end&&(s->chars[p]=='e'||s->chars[p]=='E')){
            buffer[used++]='e';p++;if(p<end&&(s->chars[p]=='+'||s->chars[p]=='-'))buffer[used++]=(char)s->chars[p++];
            int exponent=0;while(p<end&&s->chars[p]>='0'&&s->chars[p]<='9'){buffer[used++]=(char)s->chars[p++];exponent++;}
            if(!exponent)return failure(throwing,DND_EXCEPTION_FORMAT);
        }
        if(p!=end)return failure(throwing,DND_EXCEPTION_FORMAT);
        buffer[used]=0;value=single?(double)strtof(buffer,NULL):strtod(buffer,NULL);
    }
    if(single){float result=(float)value;memcpy(out,&result,4);}else memcpy(out,&value,8);return 1;
}
int dnd_numeric_f32(DndString *s,intptr_t out,int throwing){return floating(s,(void*)out,1,throwing);}
int dnd_numeric_f64(DndString *s,intptr_t out,int throwing){return floating(s,(void*)out,0,throwing);}
