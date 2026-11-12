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
    a=calloc(1,sizeof(*a)+208);assert(a);a->element_size=1;a->length=208;p=a->data;
    memcpy(p,"DNEF",4);p[4]=4;p[8]=2;p[12]=3;p[21]=1;
    p[24]=3;p[25]=33;p[26]=1;p[27]=6;p[28]=235;
    const unsigned char first[]={15,15,15,10,0,0,0,0,7,7,7,4,0,193,0,0};
    const unsigned char second[]={15,15,15,0,0,0,0,1,7,4,6,1,0,240,0,0};
    memcpy(p+176,first,16);memcpy(p+192,second,16);
    token=dnd_effect_create(a);assert(token>0);
    const DndDrawEffect *v4=NULL;assert(dnd_effect_resolve(token,&v4)==0&&v4&&v4->alpha&&v4->count==2);
    assert(v4->alpha->compare0==3&&v4->alpha->reference0==33&&v4->alpha->operation==1&&v4->alpha->reference1==235);
    assert(v4->stages[1].reserved==1&&v4->alpha->stages[1].control==240);
    assert(dnd_draw_release(token)==0);
    const int corrupt_at[]={21,22,24,26,27,29,184,188,189,190,191,205};
    const unsigned char corrupt_value[]={2,1,8,4,8,1,0,2,205,16,1,241};
    for(unsigned i=0;i<sizeof(corrupt_at)/sizeof(*corrupt_at);i++){
        int at=corrupt_at[i];unsigned char old=p[at];p[at]=corrupt_value[i];assert(dnd_effect_create(a)==0);p[at]=old;
    }
    p[12]=1;assert(dnd_effect_create(a)==0);p[12]=3; // Alpha-only TEX1 still requires its binding.
    p[21]=0;assert(dnd_effect_create(a)==0);p[21]=1; // Disabled compare cannot conceal state.
    p[189]=0;assert(dnd_effect_create(a)==0);p[189]=193; // Legacy record must be entirely zero.
    a->length=207;assert(dnd_effect_create(a)==0);a->length=208;
    free(a);assert(dnd_draw_total_bytes()==0);
    a=calloc(1,sizeof(*a)+208);assert(a);a->element_size=1;a->length=208;p=a->data;
    memcpy(p,"DNEF",4);p[4]=5;p[8]=2;p[12]=3;p[22]=1;p[29]=4;p[30]=2;
    const unsigned char rgb_first[]={15,15,15,9,0,129,0,0,7,7,7,4,0,193,0,0};
    const unsigned char rgb_second[]={15,9,3,2,1,148,0,1,7,7,7,1,0,192,0,0};
    memcpy(p+176,rgb_first,16);memcpy(p+192,rgb_second,16);
    token=dnd_effect_create(a);assert(token>0);const DndDrawEffect *v5=NULL;
    assert(dnd_effect_resolve(token,&v5)==0&&v5&&v5->alpha->blend_enabled&&v5->alpha->blend_source==4&&v5->alpha->blend_destination==2);
    assert(v5->stages[1].dest==148&&v5->stages[1].op==1);assert(dnd_draw_release(token)==0);
    const int rgb_corrupt_at[]={4,22,29,30,31,179,181,197,198};
    const unsigned char rgb_corrupt_value[]={4,2,8,8,1,3,141,156,255};
    for(unsigned i=0;i<sizeof(rgb_corrupt_at)/sizeof(*rgb_corrupt_at);i++){
        int at=rgb_corrupt_at[i];unsigned char old=p[at];p[at]=rgb_corrupt_value[i];assert(dnd_effect_create(a)==0);p[at]=old;
    }
    p[22]=0;assert(dnd_effect_create(a)==0);p[22]=1;
    p[12]=1;assert(dnd_effect_create(a)==0);p[12]=3;
    // Same-stage alpha writes cannot satisfy an RGB read from that register.
    p[179]=3;assert(dnd_effect_create(a)==0);p[179]=9;
    free(a);assert(dnd_draw_total_bytes()==0);
    a=calloc(1,sizeof(*a)+220);assert(a);a->element_size=1;a->length=220;p=a->data;
    memcpy(p,"DNEF",4);p[4]=5;p[8]=1;p[12]=3;p[20]=76;p[23]=1;p[31]=7;
    p[176]=p[177]=p[178]=15;p[179]=8;
    const float coefficients[]={0,0,0,0,.001f,.015f};
    for(int i=0;i<6;i++){uint32_t bits;memcpy(&bits,&coefficients[i],4);for(int b=0;b<4;b++)p[192+i*4+b]=(unsigned char)(bits>>(b*8));}
    p[216]=2;token=dnd_effect_create(a);assert(token>0);
    assert(dnd_effect_resolve(token,&v5)==0&&v5->alpha->indirect_enabled&&v5->alpha->indirect_bias==7&&v5->alpha->indirect_exponent==2);
    assert(v5->alpha->indirect_matrix[4]==.001f&&v5->alpha->indirect_matrix[5]==.015f);assert(dnd_draw_release(token)==0);
    const int matrix_corrupt_at[]={20,23,31,216,216,217,218,219};
    const unsigned char matrix_corrupt_value[]={0,2,8,47,238,1,1,1};
    for(unsigned i=0;i<sizeof(matrix_corrupt_at)/sizeof(*matrix_corrupt_at);i++){
        int at=matrix_corrupt_at[i];unsigned char old=p[at];p[at]=matrix_corrupt_value[i];assert(dnd_effect_create(a)==0);p[at]=old;
    }
    const uint32_t invalid_floats[]={0x7f800001u,0x7f800000u,0xff800000u,0x3f800000u,0xbf800001u};
    for(unsigned i=0;i<sizeof(invalid_floats)/sizeof(*invalid_floats);i++){
        for(int b=0;b<4;b++)p[192+b]=(unsigned char)(invalid_floats[i]>>(b*8));
        assert(dnd_effect_create(a)==0);
    }
    memset(p+192,0,4);a->length=219;assert(dnd_effect_create(a)==0);a->length=220;
    p[23]=0;assert(dnd_effect_create(a)==0);
    free(a);assert(dnd_draw_total_bytes()==0);

}
