/* Exercise the native trust boundary independently of the managed constructors. */
static void effect_package_tests(void) {
    DndArray *a=calloc(1,sizeof(*a)+184);assert(a);a->element_size=1;a->length=184;
    unsigned char *p=a->data;memcpy(p,"DNEF",4);p[4]=1;p[8]=1;
    p[176]=p[177]=p[178]=15;p[179]=10;
    int token=dnd_effect_create(a);assert(token>0);assert(dnd_draw_release(token)==0);
    for(int i=0;i<184;i++){
        unsigned char before=p[i];
        if(i<32||i>=176){p[i]=255;int invalid=dnd_effect_create(a);if(invalid)assert(dnd_draw_release(invalid)==0);p[i]=before;}
    }
    p[179]=0;assert(dnd_effect_create(a)==0);p[179]=10; // Read before initialize.
    p[181]=1;assert(dnd_effect_create(a)==0);p[181]=0; // Missing PREV output.
    p[182]=255;assert(dnd_effect_create(a)==0);p[182]=0;
    p[179]=8;assert(dnd_effect_create(a)==0);p[179]=10; // Missing texture flag.
    p[179]=14;assert(dnd_effect_create(a)==0);p[179]=10; // Unbound uniform.
    a->length=183;assert(dnd_effect_create(a)==0);a->length=184;
    p[8]=17;assert(dnd_effect_create(a)==0);p[8]=1;
    a->elements_are_references=1;assert(dnd_effect_create(a)==0);free(a);
    a=calloc(1,sizeof(*a)+192);assert(a);a->element_size=1;a->length=192;p=a->data;
    memcpy(p,"DNEF",4);p[4]=2;p[8]=2;p[12]=2;
    p[176]=p[177]=p[178]=15;p[179]=8;p[183]=1;
    p[184]=p[185]=p[186]=15; // RGB passthrough, primary opacity epilogue
    token=dnd_effect_create(a);assert(token>0);assert(dnd_draw_release(token)==0);
    p[12]=0;assert(dnd_effect_create(a)==0);p[12]=2;
    p[183]=2;assert(dnd_effect_create(a)==0);p[183]=1;
    p[191]=1;assert(dnd_effect_create(a)==0);p[191]=0;
    p[4]=1;assert(dnd_effect_create(a)==0);p[4]=3;assert(dnd_effect_create(a)==0);
    free(a);assert(dnd_draw_total_bytes()==0);
    a=calloc(1,sizeof(*a)+200);assert(a);a->element_size=1;a->length=200;p=a->data;
    memcpy(p,"DNEF",4);p[4]=3;p[8]=3;p[12]=3;p[20]=76;
    p[176]=p[177]=p[178]=15;p[179]=8;
    p[184]=p[185]=p[186]=15;p[187]=8;p[191]=2;
    p[192]=p[193]=p[194]=15;
    token=dnd_effect_create(a);assert(token>0);assert(dnd_draw_release(token)==0);
    p[20]=0;assert(dnd_effect_create(a)==0);p[20]=76;
    p[20]=76;p[21]=1;assert(dnd_effect_create(a)==0);p[21]=0;
    p[12]=1;assert(dnd_effect_create(a)==0);p[12]=3;
    p[191]=1;assert(dnd_effect_create(a)==0);p[191]=2;
    p[199]=2;assert(dnd_effect_create(a)==0);p[199]=0;
    free(a);assert(dnd_draw_total_bytes()==0);
}
