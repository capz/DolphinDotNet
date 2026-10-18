#include "png_texture_fixtures.h"
extern const unsigned char *graphics_expected_rgba;
extern int graphics_expected_width,graphics_expected_height;
static DndArray *png_array(const unsigned char *bytes,size_t n) {
    DndArray *a=calloc(1,sizeof(*a)+n);assert(a);
    a->element_size=1;a->length=(uint32_t)n;
    memcpy(a->data,bytes,n);return a;
}
static void png_case(const unsigned char *png,size_t n,const unsigned char *rgba,int w,int h) {
    DndArray *a=png_array(png,n);
    int baseline=dnd_draw_total_bytes();
    int token=dnd_texture_png(a,1);assert(token>0);
    assert(dnd_texture_dimension(token,0)==w&&dnd_texture_dimension(token,1)==h);
    assert(dnd_texture_dimension(token,2)==6);
    assert(dnd_draw_bytes(token)==((w+3)/4)*((h+3)/4)*64);
    graphics_expected_rgba=rgba;graphics_expected_width=w;graphics_expected_height=h;
    assert(dnd_device_begin(0,0,0,1)==0);
    assert(dnd_texture_png(a,0)==0); /* Never allocate during a frame. */
    assert(dnd_draw_copy_texture(token,0,0,w,h)==0);
    assert(dnd_device_end()==0);
    graphics_expected_rgba=NULL;
    assert(dnd_draw_release(token)==0);
    assert(dnd_draw_total_bytes()==baseline);
    /* Every truncation and every one-byte corruption must fail, without leaks. */
    for(size_t i=0;i<n;i++) {
        a->length=(uint32_t)i;assert(dnd_texture_png(a,0)==0);
        a->length=(uint32_t)n;a->data[i]^=1;
        assert(dnd_texture_png(a,0)==0);a->data[i]^=1;
        assert(dnd_draw_total_bytes()==baseline);
    }
    free(a);
}
static void png_texture_tests(void) {
#define PNG_CASE(name,w,h) png_case(png_##name##_png,sizeof(png_##name##_png),png_##name##_rgba,w,h)
    PNG_CASE(filters,5,5);PNG_CASE(adam7,5,5);PNG_CASE(palette,3,1);PNG_CASE(gray,3,1);PNG_CASE(rgba16,1,1);
#undef PNG_CASE
    /* A successful decode followed by graphics-budget failure frees scratch. */
    int buffer=dnd_buffer_create(16*1024*1024,32);assert(buffer>0);
    DndArray *full=png_array(png_gray_png,sizeof(png_gray_png));
    assert(dnd_texture_png(full,0)==0);
    assert(dnd_draw_release(buffer)==0);
    int token=dnd_texture_png(full,0);assert(token>0);free(full);
    assert(dnd_draw_release(token)==0&&dnd_draw_total_bytes()==0);
    assert(dnd_texture_png(NULL,0)==0);
    DndArray *a=png_array(png_gray_png,sizeof(png_gray_png));
    a->elements_are_references=1;assert(dnd_texture_png(a,0)==0);
    a->elements_are_references=0;a->element_size=2;assert(dnd_texture_png(a,0)==0);
    a->element_size=1;a->length=1024*1024+1;assert(dnd_texture_png(a,0)==0);free(a);
    /* Valid chunk CRCs still cannot authorize invalid dimensions/deflate. */
    a=png_array(png_oversized,sizeof(png_oversized));assert(dnd_texture_png(a,0)==0);free(a);
    a=png_array(png_inflate_budget,sizeof(png_inflate_budget));assert(dnd_texture_png(a,0)==0);free(a);
    a=png_array(png_invalid_deflate,sizeof(png_invalid_deflate));assert(dnd_texture_png(a,0)==0);free(a);
}
