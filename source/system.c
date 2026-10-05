#include "dnd_system.h"
#include "dnd_platform.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

static DndSystemException current_exception; static const char *current_message;
void dnd_system_throw(DndSystemException k,const char*m){current_exception=k;current_message=m;}
void dnd_system_clear_exception(void){current_exception=DND_EX_NONE;current_message=NULL;}
DndSystemException dnd_system_exception(void){return current_exception;}
const char*dnd_system_exception_message(void){return current_message?current_message:"";}

static int hex(char c){if(c>='0'&&c<='9')return c-'0';if(c>='a'&&c<='f')return c-'a'+10;if(c>='A'&&c<='F')return c-'A'+10;return -1;}
bool dnd_guid_parse(const char*t,DndGuid*g){if(!t||!g)return false;int n=0,hi=-1;for(;*t;t++){if(*t=='-'||*t=='{'||*t=='}')continue;int v=hex(*t);if(v<0)return false;if(hi<0)hi=v;else{if(n>=16)return false;g->bytes[n++]=(uint8_t)((hi<<4)|v);hi=-1;}}return n==16&&hi<0;}
void dnd_guid_to_string(const DndGuid*g,char o[37]){static const char h[]="0123456789abcdef";int p=0;for(int i=0;i<16;i++){if(i==4||i==6||i==8||i==10)o[p++]='-';o[p++]=h[g->bytes[i]>>4];o[p++]=h[g->bytes[i]&15];}o[p]=0;}
bool dnd_guid_new(DndGuid*g){if(!g)return false;static uint32_t state=0;if(!state)state=(uint32_t)time(NULL)^0x9e3779b9u;for(int i=0;i<16;i++){state^=state<<13;state^=state>>17;state^=state<<5;g->bytes[i]=(uint8_t)state;}g->bytes[6]=(g->bytes[6]&0x0f)|0x40;g->bytes[8]=(g->bytes[8]&0x3f)|0x80;return true;}

void dnd_system_console_write(const char*t){dnd_platform_write(t?t:"");}
void dnd_system_console_write_line(const char*t){dnd_platform_write(t?t:"");dnd_platform_write("\n");}
void dnd_console_write_i32(int32_t v){dnd_platform_write_int(v);}

bool dnd_file_exists(const char*p){if(!p)return false;FILE*f=fopen(p,"rb");if(!f)return false;fclose(f);return true;}
bool dnd_file_read_all_bytes(const char*p,uint8_t**data,size_t*length){if(!p||!data||!length)return false;FILE*f=fopen(p,"rb");if(!f){dnd_system_throw(DND_EX_FILE_NOT_FOUND,"File not found.");return false;}if(fseek(f,0,SEEK_END)!=0){fclose(f);return false;}long n=ftell(f);rewind(f);if(n<0){fclose(f);return false;}uint8_t*b=(uint8_t*)malloc((size_t)n);if(n&&(!b||fread(b,1,(size_t)n,f)!=(size_t)n)){free(b);fclose(f);dnd_system_throw(DND_EX_IO,"File read failed.");return false;}fclose(f);*data=b;*length=(size_t)n;return true;}
bool dnd_file_write_all_bytes(const char*p,const uint8_t*d,size_t n){FILE*f=fopen(p,"wb");if(!f){dnd_system_throw(DND_EX_IO,"File open failed.");return false;}bool ok=!n||fwrite(d,1,n,f)==n;fclose(f);if(!ok)dnd_system_throw(DND_EX_IO,"File write failed.");return ok;}

bool dnd_path_is_separator(char c){return c=='/'||c=='\\';}
const char*dnd_path_get_file_name(const char*p){if(!p)return NULL;const char*last=p;for(const char*s=p;*s;s++)if(dnd_path_is_separator(*s))last=s+1;return last;}
bool dnd_path_combine(const char*a,const char*b,char*o,size_t cap){if(!a||!b||!o||!cap)return false;if(b[0]&&dnd_path_is_separator(b[0])){if(strlen(b)+1>cap)return false;strcpy(o,b);return true;}size_t al=strlen(a),bl=strlen(b);bool sep=al&&!dnd_path_is_separator(a[al-1]);if(al+bl+(sep?2:1)>cap)return false;memcpy(o,a,al);size_t p=al;if(sep)o[p++]='/';memcpy(o+p,b,bl+1);return true;}

const char*dnd_appcontext_base_directory(void){return "/";}
bool dnd_appcontext_try_get_switch(const char*n,bool*enabled){(void)n;if(enabled)*enabled=false;return false;}
