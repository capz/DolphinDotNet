#include "dnd_text.h"
#include <limits.h>
#include <stdlib.h>
#include <string.h>
static bool range(DndArray *a,int offset,int count,uint32_t size){
 if(!a){dnd_exception_throw(DND_EXCEPTION_ARGUMENT_NULL,"Null buffer.");return false;}
 if(offset<0||count<0){dnd_exception_throw(DND_EXCEPTION_ARGUMENT_OUT_OF_RANGE,"Negative range.");return false;}
 if(a->element_size!=size||(uint32_t)offset>a->length||(uint32_t)count>a->length-(uint32_t)offset){dnd_exception_throw(DND_EXCEPTION_ARGUMENT,"Invalid buffer range.");return false;}return true;
}
DndString *dnd_text_from_chars(DndManagedHeap *h,DndArray *a,int offset,int count){
 if(!range(a,offset,count,2))return NULL;
 DndObject **slots[]={ (DndObject**)&a };DndGcFrame frame;dnd_gc_frame_push(&frame,slots,1);
 DndString *s=dnd_string_from_utf16(h,(uint16_t*)a->data+offset,(uint32_t)count);dnd_gc_frame_pop(&frame);return s;
}
static bool invalid(bool encode){dnd_exception_throw(encode?DND_EXCEPTION_ENCODER_FALLBACK:DND_EXCEPTION_DECODER_FALLBACK,"Invalid text encoding sequence.");return true;}
DndArray *dnd_text_encode(DndManagedHeap *h,DndString *s,int code,bool strict){
 if(!s){dnd_exception_throw(DND_EXCEPTION_ARGUMENT_NULL,"Null text.");return NULL;}
 if(code!=65001&&code!=1200&&code!=1201&&code!=20127&&code!=28591){dnd_exception_throw(DND_EXCEPTION_NOT_SUPPORTED,"Encoding is unavailable.");return NULL;}
 if(s->length>INT_MAX/4){dnd_exception_throw(DND_EXCEPTION_OUT_OF_MEMORY,"Text is too large.");return NULL;}
 size_t capacity=(size_t)s->length*4+1;uint8_t *bytes=malloc(capacity);if(!bytes){dnd_exception_throw(DND_EXCEPTION_OUT_OF_MEMORY,"Encoding buffer allocation failed.");return NULL;}
 size_t n=0;
 for(uint32_t i=0;i<s->length;i++){
  uint32_t cp=s->chars[i];
  if(cp>=0xd800&&cp<=0xdbff){if(i+1<s->length&&s->chars[i+1]>=0xdc00&&s->chars[i+1]<=0xdfff){cp=0x10000+((cp-0xd800)<<10)+(s->chars[++i]-0xdc00);}else{if(strict){free(bytes);invalid(true);return NULL;}cp=0xfffd;}}
  else if(cp>=0xdc00&&cp<=0xdfff){if(strict){free(bytes);invalid(true);return NULL;}cp=0xfffd;}
  if(code==65001){if(cp<0x80)bytes[n++]=(uint8_t)cp;else if(cp<0x800){bytes[n++]=(uint8_t)(0xc0|(cp>>6));bytes[n++]=(uint8_t)(0x80|(cp&63));}else if(cp<0x10000){bytes[n++]=(uint8_t)(0xe0|(cp>>12));bytes[n++]=(uint8_t)(0x80|((cp>>6)&63));bytes[n++]=(uint8_t)(0x80|(cp&63));}else{bytes[n++]=(uint8_t)(0xf0|(cp>>18));bytes[n++]=(uint8_t)(0x80|((cp>>12)&63));bytes[n++]=(uint8_t)(0x80|((cp>>6)&63));bytes[n++]=(uint8_t)(0x80|(cp&63));}}
  else if(code==1200||code==1201){uint16_t units[2];unsigned c=1;if(cp>0xffff){cp-=0x10000;units[0]=(uint16_t)(0xd800+(cp>>10));units[1]=(uint16_t)(0xdc00+(cp&1023));c=2;}else units[0]=(uint16_t)cp;for(unsigned j=0;j<c;j++){bytes[n++]=(uint8_t)(code==1200?units[j]:units[j]>>8);bytes[n++]=(uint8_t)(code==1200?units[j]>>8:units[j]);}}
  else {uint32_t max=code==20127?127:255;if(cp>max){if(strict){free(bytes);invalid(true);return NULL;}cp='?';}bytes[n++]=(uint8_t)cp;}
 }
 DndObject **slots[]={ (DndObject**)&s };DndGcFrame frame;dnd_gc_frame_push(&frame,slots,1);
 DndArray *a=dnd_managed_array_new_typed(h,(uint32_t)n,1,&DND_TYPE_BYTE,false);if(a&&n)memcpy(a->data,bytes,n);dnd_gc_frame_pop(&frame);free(bytes);return a;
}
DndString *dnd_text_decode(DndManagedHeap *h,DndArray *a,int offset,int count,int code,bool strict){
 if(!range(a,offset,count,1))return NULL;
 if(code!=65001&&code!=1200&&code!=1201&&code!=20127&&code!=28591){dnd_exception_throw(DND_EXCEPTION_NOT_SUPPORTED,"Encoding is unavailable.");return NULL;}
 uint16_t *chars=malloc(((size_t)count+1)*2);if(!chars){dnd_exception_throw(DND_EXCEPTION_OUT_OF_MEMORY,"Decoding buffer allocation failed.");return NULL;}
 const uint8_t *p=a->data+offset;size_t pos=0,n=0;
 while(pos<(size_t)count){
  uint32_t cp=p[pos++];bool bad=false;
  if(code==65001&&cp>=128){unsigned need=cp>=0xc2&&cp<=0xdf?1:cp>=0xe0&&cp<=0xef?2:cp>=0xf0&&cp<=0xf4?3:0;uint32_t min=need==1?0x80:need==2?0x800:0x10000;uint8_t first=(uint8_t)cp;cp&=need==1?31:need==2?15:7;if(!need)bad=true;else for(unsigned j=0;j<need;j++){if(pos>=(size_t)count||p[pos]<0x80||p[pos]>0xbf||(j==0&&((first==0xe0&&p[pos]<0xa0)||(first==0xed&&p[pos]>0x9f)||(first==0xf0&&p[pos]<0x90)||(first==0xf4&&p[pos]>0x8f)))){bad=true;break;}cp=(cp<<6)|(p[pos++]&63);}if(!bad&&(cp<min||cp>0x10ffff||(cp>=0xd800&&cp<=0xdfff)))bad=true;}
  else if(code==1200||code==1201){if(pos>=(size_t)count)bad=true;else{uint32_t second=p[pos++];cp=code==1200?cp|(second<<8):(cp<<8)|second;if(cp>=0xd800&&cp<=0xdbff){if(pos+1<(size_t)count){uint32_t low=code==1200?p[pos]|((uint32_t)p[pos+1]<<8):((uint32_t)p[pos]<<8)|p[pos+1];if(low>=0xdc00&&low<=0xdfff){cp=0x10000+((cp-0xd800)<<10)+(low-0xdc00);pos+=2;}else bad=true;}else bad=true;}else if(cp>=0xdc00&&cp<=0xdfff)bad=true;}}
  else if(code==20127&&cp>127){bad=true;}
  if(bad){if(strict){free(chars);invalid(false);return NULL;}cp=code==20127?'?':0xfffd;}
  if(cp<=0xffff)chars[n++]=(uint16_t)cp;else{cp-=0x10000;chars[n++]=(uint16_t)(0xd800+(cp>>10));chars[n++]=(uint16_t)(0xdc00+(cp&1023));}
 }
 DndString *s=dnd_string_from_utf16(h,chars,(uint32_t)n);free(chars);return s;
}
