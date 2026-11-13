/* Original generated recipes only in CI. The optional output argument is for
 * explicitly requested private qualification; no firmware/output is bundled. */
#define main unused_audio_formats_main
#include "audio_formats_test.c"
#undef main
int main(int argc,char **argv) {
  assert(argc==2||argc==3);
  FILE *f=fopen(argv[1],"rb");assert(f);assert(!fseek(f,0,SEEK_END));long bytes=ftell(f);assert(bytes>64);rewind(f);
  DndArray *a=calloc(1,sizeof(*a)+(size_t)bytes);assert(a);a->length=(unsigned)bytes;a->element_size=1;
  assert(fread(a->data,1,(size_t)bytes,f)==(size_t)bytes);fclose(f);
  assert(!dnd_audio_begin_menu(a,1024));
  unsigned corrupt_at[]={0,4,8,12,16,20,24,28,32,48};
  for(unsigned i=0;i<sizeof(corrupt_at)/sizeof(corrupt_at[0]);i++) {
    unsigned at=corrupt_at[i];uint8_t saved[4];memcpy(saved,a->data+at,4);memset(a->data+at,255,4);
    assert(!dnd_audio_begin_menu(a,1024*1024));memcpy(a->data+at,saved,4);
  }
  int measured=dnd_audio_begin_menu(a,1024*1024);assert(measured);
  int peak=dnd_audio_stat(measured,10);assert(peak>1024);assert(!dnd_audio_dispose(measured));
  assert(!dnd_audio_begin_menu(a,peak-1));
  measured=dnd_audio_begin_menu(a,peak);assert(measured);assert(!dnd_audio_dispose(measured));
  /* Dispose during source decoding and rendering, repeatedly, without losing
   * a clip slot. A managed cancellation takes exactly this cleanup path. */
  for(int step=0;step<16;step++) {
    int token=dnd_audio_begin_menu(a,1024*1024);assert(token);
    for(int n=0;n<step;n++)assert(dnd_audio_step(token)>=0);
    assert(!dnd_audio_dispose(token));assert(dnd_audio_stat(token,0)==-1);
  }
  int clips[16];for(int i=0;i<16;i++){clips[i]=dnd_audio_begin_menu(a,1024*1024);assert(clips[i]);}
  assert(!dnd_audio_begin_menu(a,1024*1024));
  memset(a->data,0,(size_t)bytes);free(a);
  for(int i=0;i<16;i++)assert(complete(clips[i])==1);
  int token=clips[0],frames=dnd_audio_stat(token,4);assert(frames>0);
  assert(dnd_audio_stat(token,2)==32000&&dnd_audio_stat(token,3)==2&&!dnd_audio_stat(token,5));
  int voices[32];for(int i=0;i<32;i++){voices[i]=dnd_audio_play(token,0,1,1);assert(voices[i]);}
  assert(!dnd_audio_play(token,0,1,1));
  for(int i=1;i<32;i++)assert(resident_bytes[i]==resident_bytes[0]&&!memcmp(resident_pcm[i],resident_pcm[0],resident_bytes[0]));
  if(argc==3){FILE *out=fopen(argv[2],"wb");assert(out);assert(fwrite(resident_pcm[0],1,resident_bytes[0],out)==resident_bytes[0]);fclose(out);}
  printf("Menu recipe frames=%d native-retained=%d preparation-peak=%d slices-and-lifetime passed\n",frames,dnd_audio_stat(token,8),dnd_audio_stat(token,10));
  for(int i=0;i<16;i++)assert(!dnd_audio_dispose(clips[i]));
  for(unsigned i=0;i<32;i++)assert(!active[i]);
  dnd_audio_shutdown();return 0;
}
