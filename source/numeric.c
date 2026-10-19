#include "dnd_numeric.h"
#include <math.h>
#include <stdlib.h>
#include <string.h>
#include <limits.h>
#include <stdint.h>
#include <stdio.h>
/* Pinned Ryu parsers avoid newlib's subnormal strtod underflow. Keep
   upstream static helpers distinct when embedding both widths in this TU. */
#define s2d dnd_ryu_s2d
#define s2d_n dnd_ryu_s2d_n
#define s2f dnd_ryu_s2f
#define s2f_n dnd_ryu_s2f_n
#include "ryu/s2d.c"
#define floor_log2 dnd_ryu_floor_log2_32
#define max32 dnd_ryu_max32_32
#include "ryu/s2f.c"
#undef floor_log2
#undef max32
#define d2s dnd_ryu_d2s
#define d2s_buffered dnd_ryu_d2s_buffered
#define d2s_buffered_n dnd_ryu_d2s_buffered_n
#define f2s dnd_ryu_f2s
#define f2s_buffered dnd_ryu_f2s_buffered
#define f2s_buffered_n dnd_ryu_f2s_buffered_n
#define d2fixed dnd_ryu_d2fixed
#define d2fixed_buffered dnd_ryu_d2fixed_buffered
#define d2fixed_buffered_n dnd_ryu_d2fixed_buffered_n
#define d2exp dnd_ryu_d2exp
#define d2exp_buffered dnd_ryu_d2exp_buffered
#define d2exp_buffered_n dnd_ryu_d2exp_buffered_n
#include "ryu/d2s.c"
#define to_chars dnd_ryu_f2s_chars
#include "ryu/f2s.c"
#undef to_chars
#include "ryu/d2fixed.c"

/* Invariant standard numeric formatting. Bounded precision keeps temporary
   buffers predictable on the console stack; custom/culture formats fail. */
static int format_spec(DndString *format,char *code,int *precision) {
    *code='G';*precision=-1;
    if(!format||format->length==0)return 1;
    if(format->chars[0]>127)goto invalid;
    *code=(char)format->chars[0];
    if(format->length>1){*precision=0;for(uint32_t i=1;i<format->length;i++){unsigned c=format->chars[i];if(c<'0'||c>'9'||*precision>9||*precision*10+(int)(c-'0')>99)goto invalid;*precision=*precision*10+(int)(c-'0');}}
    return 1;
invalid:dnd_exception_throw(DND_EXCEPTION_FORMAT,"Unsupported numeric format or precision (0-99).");return 0;
}
static DndString *format_error(void){dnd_exception_throw(DND_EXCEPTION_FORMAT,"Unsupported numeric format.");return NULL;}
static void exponent_text(char *out,int exponent,char code,int width) {
    char digits[16];unsigned n=(unsigned)(exponent<0?-exponent:exponent);int used=0;
    do{digits[used++]=(char)('0'+n%10);n/=10;}while(n);
    int at=0;out[at++]=code;out[at++]=exponent<0?'-':'+';while(width>used){out[at++]='0';width--;}
    while(used){out[at++]=digits[--used];}out[at]=0;
}
/* Round a decimal coefficient without losing UInt64 precision. */
static int round_digits(char *digits,int length,int precision,int *exponent) {
    if(length<=precision)return length;
    /* Integral G/E precision uses decimal half-up rounding in .NET. */
    int up=digits[precision]>='5';
    length=precision;
    if(up){int at=length-1;while(at>=0&&digits[at]=='9')digits[at--]='0';if(at<0){digits[0]='1';(*exponent)++;}else digits[at]++;}
    return length;
}
static void decimal_general(char *out,const char *digits,int length,int exponent,int threshold,char code,int negative) {
    int at=0;if(negative)out[at++]='-';
    if(exponent < -4 || exponent>=threshold){out[at++]=digits[0];if(length>1){out[at++]='.';for(int i=1;i<length;i++)out[at++]=digits[i];}exponent_text(out+at,exponent,code,2);return;}
    if(exponent<0){out[at++]='0';out[at++]='.';for(int i=-1;i>exponent;i--)out[at++]='0';for(int i=0;i<length;i++)out[at++]=digits[i];}
    else {for(int i=0;i<=exponent;i++)out[at++]=i<length?digits[i]:'0';if(length>exponent+1){out[at++]='.';for(int i=exponent+1;i<length;i++)out[at++]=digits[i];}}
    out[at]=0;
}
static void scientific(char *out,const char *digits,int length,int exponent,int precision,char code,int negative) {
    int at=0;if(negative)out[at++]='-';out[at++]=digits[0];if(precision){out[at++]='.';for(int i=1;i<=precision;i++)out[at++]=i<length?digits[i]:'0';}exponent_text(out+at,exponent,code,3);
}
static void grouped(char *out,const char *input) {
    int at=0,start=input[0]=='-'?1:0,end=start;while(input[end]&&input[end]!='.')end++;
    if(start){out[at++]='-';}for(int i=start;i<end;i++){out[at++]=input[i];if((end-i-1)%3==0&&i+1<end)out[at++]=',';}
    while(input[end]){out[at++]=input[end++];}out[at]=0;
}
/* Invariant currency uses the currency sign, and percent keeps decimal scaling
 * in digit space: multiplying a binary float by 100 would round twice. */
static void decorated(char *out,const char *number,int currency) {
    int negative=number[0]=='-';const char *digits=number+negative;char grouped_digits[2048];grouped(grouped_digits,digits);
    if(currency){strcpy(out,negative?"(¤":"¤");strcat(out,grouped_digits);if(negative)strcat(out,")");}
    else {out[0]=0;if(negative)strcat(out,"-");strcat(out,grouped_digits);strcat(out," %");}
}
DndString *dnd_format_integer(DndManagedHeap *heap,int64_t raw,int kind,DndString *format) {
    char code;int precision;if(!format_spec(format,&code,&precision))return NULL;
    char upper=code>='a'&&code<='z'?(char)(code-32):code;
    unsigned width=(unsigned)(kind/2);int negative=!(kind&1)&&raw<0;
    uint64_t bits=(uint64_t)raw;if(width<64)bits&=(UINT64_C(1)<<width)-1;
    char digits[128],out[512],temporary[512];int length=0;
    if(upper=='X'||upper=='B') {
        unsigned radix=upper=='X'?16:2;const char *alphabet=code=='x'?"0123456789abcdef":"0123456789ABCDEF";
        do{digits[length++]=alphabet[bits%radix];bits/=radix;}while(bits);
        int at=0;while(precision>length){out[at++]='0';precision--;}while(length)out[at++]=digits[--length];out[at]=0;
    } else {
        uint64_t magnitude=negative?UINT64_C(0)-(uint64_t)raw:(uint64_t)raw;
        do{digits[length++]=(char)('0'+magnitude%10);magnitude/=10;}while(magnitude);
        for(int i=0;i<length/2;i++){char c=digits[i];digits[i]=digits[length-1-i];digits[length-1-i]=c;}digits[length]=0;
        int at=0,exponent=length-1;
        if(upper=='D'||(upper=='G'&&precision<=0)){if(negative)out[at++]='-';while(precision>length){out[at++]='0';precision--;}memcpy(out+at,digits,(size_t)length+1);}
        else if(upper=='F'||upper=='N'||upper=='C'||upper=='P'){if(upper=='P'){digits[length++]='0';digits[length++]='0';}if(precision<0)precision=2;if(negative)temporary[at++]='-';memcpy(temporary+at,digits,(size_t)length);at+=length;if(precision){temporary[at++]='.';for(int i=0;i<precision;i++)temporary[at++]='0';}temporary[at]=0;if(upper=='N')grouped(out,temporary);else if(upper=='C'||upper=='P')decorated(out,temporary,upper=='C');else memcpy(out,temporary,(size_t)at+1);}
        else if(upper=='E'){if(precision<0)precision=6;length=round_digits(digits,length,precision+1,&exponent);scientific(out,digits,length,exponent,precision,code,negative);}
        else if(upper=='G'){length=round_digits(digits,length,precision,&exponent);while(length>1&&digits[length-1]=='0')length--;decimal_general(out,digits,length,exponent,precision,code=='g'?'e':'E',negative);}
        else return format_error();
    }
    return dnd_string_from_utf8(heap,out);
}
static __attribute__((noinline)) DndString *format_float_impl(DndManagedHeap *heap,double value,int single,DndString *format) {
    char code;int precision;if(!format_spec(format,&code,&precision))return NULL;
    char upper=code>='a'&&code<='z'?(char)(code-32):code;
    if(upper!='G'&&upper!='R'&&upper!='F'&&upper!='N'&&upper!='E'&&upper!='C'&&upper!='P')return format_error();
    if(single)value=(float)value;
    if(isnan(value)){return dnd_string_from_utf8(heap,"NaN");}if(isinf(value))return dnd_string_from_utf8(heap,value<0?"-Infinity":"Infinity");
    char out[2048],temporary[1536],digits[128];int negative=signbit(value)!=0;
    if(upper=='F'||upper=='N'||upper=='C'||upper=='P'){
        if(precision<0)precision=2;
        int used=d2fixed_buffered_n(value,(uint32_t)(precision+(upper=='P'?2:0)),temporary);temporary[used]=0;
        if(upper=='P'){
            char *point=strchr(temporary,'.');
            /* Ryu emitted two extra fractional places, now move the decimal. */
            if(point){memmove(point,point+1,2);if(precision)point[2]='.';else point[2]=0;}
            char *start=temporary+(temporary[0]=='-');while(start[0]=='0'&&start[1]>='0'&&start[1]<='9')memmove(start,start+1,strlen(start));
        }
        if(upper=='N')grouped(out,temporary);else if(upper=='C'||upper=='P')decorated(out,temporary,upper=='C');else strcpy(out,temporary);
    }else {
        int used;
        if(upper=='E'){if(precision<0)precision=6;used=d2exp_buffered_n(value,(uint32_t)precision,temporary);}
        else if(upper=='R'||precision<=0)used=single?f2s_buffered_n((float)value,temporary):d2s_buffered_n(value,temporary);
        else used=d2exp_buffered_n(value,(uint32_t)(precision-1),temporary);
        temporary[used]=0;
        char *e=strchr(temporary,'e');if(!e)e=strchr(temporary,'E');if(!e)return format_error();
        int exponent=atoi(e+1),length=0;
        for(char *c=temporary;c<e;c++)if(*c!='.'&&*c!='-'&&*c!='+')digits[length++]=*c;
        if(upper=='E')scientific(out,digits,length,exponent,precision,code,negative);
        else {while(length>1&&digits[length-1]=='0')length--;decimal_general(out,digits,length,exponent,upper=='R'||precision<=0?(single?9:17):precision,code=='g'||code=='r'?'e':'E',negative);}
    }
    return dnd_string_from_utf8(heap,out);
}

double dnd_numeric_unary(int op,double x) {
    switch(op){case 0:return fabs(x);case 1:return sqrt(x);case 2:return sin(x);case 3:return cos(x);case 4:return tan(x);
    case 5:return asin(x);case 6:return acos(x);case 7:return atan(x);case 8:return sinh(x);case 9:return cosh(x);case 10:return tanh(x);
    case 11:return exp(x);case 12:return log(x);case 13:return log10(x);case 14:return floor(x);case 15:return ceil(x);case 16:return trunc(x);case 17:return cbrt(x);case 18:return log2(x);case 19:return acosh(x);case 20:return asinh(x);case 21:return atanh(x);
    case 22:return isnan(x)||x==INFINITY?x:nextafter(x,INFINITY);case 23:return isnan(x)||x==-INFINITY?x:nextafter(x,-INFINITY);
    case 24:return x==0?-2147483648.0:!isfinite(x)?2147483647.0:ilogb(x);
    case 25:return isnan(x)||x==INFINITY?x:nextafterf((float)x,INFINITY);case 26:return isnan(x)||x==-INFINITY?x:nextafterf((float)x,-INFINITY);default:return NAN;}
}
double dnd_numeric_binary(int op,double a,double b) {
    switch(op){case 0:return pow(a,b);case 1:return atan2(a,b);case 2:return remainder(a,b);
    case 3:if(isnan(a))return a;if(isnan(b))return b;return a==b?(signbit(a)?a:b):(a<b?a:b);
    case 4:if(isnan(a))return a;if(isnan(b))return b;return a==b?(signbit(a)?b:a):(a>b?a:b);
    case 5:return copysign(a,b);case 6:return scalbn(a,(int)b);case 7:return scalbnf((float)a,(int)b);default:return NAN;}
}
double dnd_numeric_fma(int op,double x,double y,double z){return op?fmaf((float)x,(float)y,(float)z):fma(x,y,z);}
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
static __attribute__((noinline)) int floating_impl(DndString *s,void *out,int single,int throwing) {
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
        buffer[used]=0;
        if(single){float result;if(s2f(buffer,&result)!=SUCCESS)result=strtof(buffer,NULL);memcpy(out,&result,4);}
        else {double result;if(s2d(buffer,&result)!=SUCCESS)result=strtod(buffer,NULL);memcpy(out,&result,8);}
        return 1;
    }
    if(single){float result=(float)value;memcpy(out,&result,4);}else memcpy(out,&value,8);return 1;
}
/* libogc enables FPSCR.NI (flush-to-zero). Formatting/parsing need IEEE
   subnormal behavior. Restore the caller's FPSCR even on managed exceptions. */
#if defined(__powerpc__) || defined(__PPC__)
static uint64_t ieee_enter(void){double saved;__asm__ volatile("mffs %0":"=f"(saved)::"memory");uint64_t bits;memcpy(&bits,&saved,8);uint64_t ieee=bits&~UINT64_C(4);double value;memcpy(&value,&ieee,8);__asm__ volatile("mtfsf 255,%0"::"f"(value):"memory");return bits;}
static void ieee_leave(uint64_t bits){double saved;memcpy(&saved,&bits,8);__asm__ volatile("mtfsf 255,%0"::"f"(saved):"memory");}
#endif
DndString *dnd_format_float(DndManagedHeap *heap,double value,int single,DndString *format){
#if defined(__powerpc__) || defined(__PPC__)
    uint64_t state=ieee_enter();DndEhFrame guard;dnd_eh_push(&guard);DndString *volatile result=NULL;
    if(setjmp(guard.environment)==0)result=format_float_impl(heap,value,single,format);
    dnd_eh_pop(&guard);ieee_leave(state);if(dnd_exception_pending())dnd_exception_rethrow();return result;
#else
    return format_float_impl(heap,value,single,format);
#endif
}
static int floating(DndString *s,void *out,int single,int throwing){
#if defined(__powerpc__) || defined(__PPC__)
    uint64_t state=ieee_enter();DndEhFrame guard;dnd_eh_push(&guard);volatile int result=0;
    if(setjmp(guard.environment)==0)result=floating_impl(s,out,single,throwing);
    dnd_eh_pop(&guard);ieee_leave(state);if(dnd_exception_pending())dnd_exception_rethrow();return result;
#else
    return floating_impl(s,out,single,throwing);
#endif
}
int dnd_numeric_f32(DndString *s,intptr_t out,int throwing){return floating(s,(void*)out,1,throwing);}
int dnd_numeric_f64(DndString *s,intptr_t out,int throwing){return floating(s,(void*)out,0,throwing);}

bool dnd_format_provider(DndObject *provider){if(!provider||strcmp(provider->type->name,"Dolphin.Globalization.CultureInfo")==0)return true;dnd_exception_throw(DND_EXCEPTION_NOT_SUPPORTED,"Only invariant formatting is available.");return false;}
