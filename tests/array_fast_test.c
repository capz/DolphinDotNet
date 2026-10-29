#include "dnd_managed.h"
#include <assert.h>
#include <stdlib.h>
#include <stdio.h>
#include <string.h>

static DndArray *array(unsigned stride) {
 DndArray *a=calloc(1,sizeof(DndArray)+3*stride);assert(a);a->length=3;a->element_size=stride;return a;
}
static void exception(DndExceptionKind kind){assert(dnd_exception_pending()&&dnd_exception_kind()==kind);dnd_exception_clear();}
/* Compare independently implemented checked operations, including cold paths.
 * Non-power-of-two strides exercise unaligned elements and untouched padding. */
#define INTEGER_DIFFERENTIAL(SUFFIX,TYPE) do { \
 uint64_t observed=dnd_array_load_##SUFFIX##_fast(a,index); \
 DndExceptionKind kind=dnd_exception_kind();dnd_exception_clear(); \
 uint64_t expected=(TYPE)dnd_array_load_scalar(a,index,sizeof(TYPE),false); \
 assert(observed==expected&&kind==dnd_exception_kind());dnd_exception_clear(); \
 bool stored=dnd_array_store_##SUFFIX##_fast(a,index,(TYPE)value); \
 kind=dnd_exception_kind();dnd_exception_clear(); \
 bool reference=dnd_array_store_scalar(b,index,(TYPE)value,sizeof(TYPE)); \
 assert(stored==reference&&kind==dnd_exception_kind());dnd_exception_clear(); \
 if(a)assert(!memcmp(a->data,b->data,3*stride)); \
} while(0)
#define FLOAT_DIFFERENTIAL(SUFFIX,TYPE,BITS) do { \
 TYPE input;BITS bits=(BITS)value;memcpy(&input,&bits,sizeof input); \
 bool stored=dnd_array_store_##SUFFIX##_fast(a,index,input); \
 DndExceptionKind kind=dnd_exception_kind();dnd_exception_clear(); \
 bool reference=dnd_array_store_scalar(b,index,bits,sizeof input); \
 assert(stored==reference&&kind==dnd_exception_kind());dnd_exception_clear(); \
 if(a)assert(!memcmp(a->data,b->data,3*stride)); \
 TYPE output=dnd_array_load_##SUFFIX##_fast(a,index);BITS observed;memcpy(&observed,&output,sizeof output); \
 kind=dnd_exception_kind();dnd_exception_clear(); \
 BITS expected=(BITS)dnd_array_load_scalar(a,index,sizeof input,false); \
 assert(observed==expected&&kind==dnd_exception_kind());dnd_exception_clear(); \
} while(0)
static void differential(void){
 const uint64_t patterns[]={0,UINT64_MAX,UINT64_C(0x8000000080000000),
  UINT64_C(0x7ff000007f800000),UINT64_C(0xfff00000ff800000),
  UINT64_C(0x7ff800017fc00001),UINT64_C(0x0000000000000001),UINT64_C(0x123456789abcdef0)};
 const unsigned strides[]={1,2,3,4,5,8,9,16};
 const uint32_t indices[]={0,1,2,3,UINT32_MAX};
 dnd_exception_clear();
 for(unsigned st=0;st<sizeof strides/sizeof strides[0];st++){
  unsigned stride=strides[st];DndArray *a=array(stride),*b=array(stride);
  for(int refs=0;refs<2;refs++){
   a->elements_are_references=b->elements_are_references=refs;
   for(unsigned i=0;i<sizeof indices/sizeof indices[0];i++)for(unsigned p=0;p<sizeof patterns/sizeof patterns[0];p++){
    uint32_t index=indices[i];uint64_t value=patterns[p];
    memset(a->data,0xa5,3*stride);memset(b->data,0xa5,3*stride);
    INTEGER_DIFFERENTIAL(u1,uint8_t);INTEGER_DIFFERENTIAL(u2,uint16_t);INTEGER_DIFFERENTIAL(u4,uint32_t);
    FLOAT_DIFFERENTIAL(r4,float,uint32_t);FLOAT_DIFFERENTIAL(r8,double,uint64_t);
   }
  }
  free(a);free(b);
 }
 {unsigned stride=0;DndArray *a=NULL,*b=NULL;uint32_t index=UINT32_MAX;uint64_t value=UINT64_MAX;
  INTEGER_DIFFERENTIAL(u1,uint8_t);INTEGER_DIFFERENTIAL(u2,uint16_t);INTEGER_DIFFERENTIAL(u4,uint32_t);
  FLOAT_DIFFERENTIAL(r4,float,uint32_t);FLOAT_DIFFERENTIAL(r8,double,uint64_t);
 }
}
#undef INTEGER_DIFFERENTIAL
#undef FLOAT_DIFFERENTIAL

int main(void) {
 differential();
 for(unsigned stride=1;stride<=16;stride*=2) {
  DndArray *a=array(stride),*b=array(stride);
  for(unsigned i=0;i<3*stride;i++)a->data[i]=b->data[i]=(unsigned char)(i*37+128);
  for(unsigned i=0;i<3;i++) {
   assert(dnd_array_load_u1_fast(a,i)==dnd_array_load_scalar(a,i,1,false));
   assert(dnd_array_load_u2_fast(a,i)==dnd_array_load_scalar(a,i,2,false));
   assert(dnd_array_load_u4_fast(a,i)==dnd_array_load_scalar(a,i,4,false));
   assert((int8_t)dnd_array_load_u1_fast(a,i)==(int64_t)dnd_array_load_scalar(a,i,1,true));
   if(stride>=2)assert((int16_t)dnd_array_load_u2_fast(a,i)==(int64_t)dnd_array_load_scalar(a,i,2,true));
   if(stride>=4)assert((int32_t)dnd_array_load_u4_fast(a,i)==(int64_t)dnd_array_load_scalar(a,i,4,true));
   bool fast=dnd_array_store_u4_fast(a,i,0xff807f01u),old=dnd_array_store_scalar(b,i,0xff807f01u,4);
   assert(fast==old&&!memcmp(a->data,b->data,3*stride));
   if(stride>=4) {
    uint32_t bits=0x80000000u;float single;memcpy(&single,&bits,4);
    assert(dnd_array_store_r4_fast(a,i,single));assert(dnd_array_load_scalar(a,i,4,false)==bits);
    single=dnd_array_load_r4_fast(a,i);uint32_t observed;memcpy(&observed,&single,4);assert(observed==bits);
   }
   if(stride>=8) {
    uint64_t bits=UINT64_C(0xc0123456789abcde);double real;memcpy(&real,&bits,8);
    assert(dnd_array_store_r8_fast(a,i,real));real=dnd_array_load_r8_fast(a,i);
    uint64_t observed;memcpy(&observed,&real,8);assert(observed==bits);
   }
   memcpy(b->data,a->data,3*stride);
  }
  dnd_array_load_u1_fast(a,UINT32_MAX);exception(DND_EXCEPTION_INDEX_OUT_OF_RANGE);
  dnd_array_store_r8_fast(a,3,1);exception(DND_EXCEPTION_INDEX_OUT_OF_RANGE);
  a->elements_are_references=1;
  dnd_array_load_u4_fast(a,3);exception(DND_EXCEPTION_INVALID_CAST);
  dnd_array_store_r4_fast(a,3,1);exception(DND_EXCEPTION_INVALID_CAST);
  free(a);free(b);
 }
 DndArray *references=array(sizeof(void*));references->elements_are_references=1;
 DndObject object={&DND_TYPE_OBJECT,0};DndObject *pointer=&object;memcpy(references->data,&pointer,sizeof pointer);
 assert(dnd_array_load_ref_fast(references,0)==pointer);
 dnd_array_load_ref_fast(references,3);exception(DND_EXCEPTION_INDEX_OUT_OF_RANGE);
 references->elements_are_references=0;dnd_array_load_ref_fast(references,3);exception(DND_EXCEPTION_INVALID_CAST);free(references);
 dnd_array_load_u4_fast(NULL,0);exception(DND_EXCEPTION_NULL_REFERENCE);
 dnd_array_load_ref_fast(NULL,0);exception(DND_EXCEPTION_NULL_REFERENCE);
 assert(!dnd_require_object_fast(NULL));exception(DND_EXCEPTION_NULL_REFERENCE);
 assert(dnd_array_length_fast(NULL)==0);exception(DND_EXCEPTION_NULL_REFERENCE);
 puts("Typed array access: widths/sign, native strides, floating bit patterns, references, bounds, nulls and error precedence passed.");
}
