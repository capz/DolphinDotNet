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
    assert(dnd_draw_total_bytes()==0);
}
