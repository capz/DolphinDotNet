#include "dnd_managed.h"
#include <assert.h>
#include <stdlib.h>
#include <stdio.h>
#include <string.h>

static DndArray *array(unsigned stride) {
 DndArray *a=calloc(1,sizeof(DndArray)+3*stride);assert(a);a->length=3;a->element_size=stride;return a;
}
static void exception(DndExceptionKind kind){assert(dnd_exception_pending()&&dnd_exception_kind()==kind);dnd_exception_clear();}
int main(void) {
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
