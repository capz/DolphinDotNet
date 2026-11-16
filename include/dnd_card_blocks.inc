/* Included after storage_gamecube.c's identity/card_errno helpers. Sector I/O
 * deliberately bypasses whole-file replacement only for DNDREC01 containers. */
static uint32_t record_u32(const unsigned char *p){return (uint32_t)p[0]<<24|(uint32_t)p[1]<<16|(uint32_t)p[2]<<8|p[3];}
static void record_put(unsigned char *p,uint32_t n){p[0]=n>>24;p[1]=n>>16;p[2]=n>>8;p[3]=n;}
int dnd_gc_card_blocks_io(int device,const char *name,int operation,int page,int expected,void *bytes,int length){
  int slot=device-4;if(slot<0||slot>1||!workareas[slot])return -ENODEV;
  identity(slot);u32 sector=0;int rc=CARD_GetSectorSize(slot,&sector);
  if(rc<0)return card_errno(rc);
  if(sector!=8192)return -ENOTSUP;
  if(operation<0||operation>4)return -EINVAL;
  card_file file;rc=CARD_Open(slot,name,&file);
  if(rc==CARD_ERROR_NOFILE&&operation==0)return 0;
  bool creating=operation==3;
  if(creating){
    if(rc>=0){CARD_Close(&file);return -EEXIST;}
    if(rc!=CARD_ERROR_NOFILE)return card_errno(rc);
    if(page<6||page>256||length!=16384||!bytes)return -EINVAL;
    rc=CARD_Create(slot,name,(page+2)*8192,&file);
  }
  if(rc<0)return card_errno(rc);
  int physical=file.len;
  unsigned char *block=memalign(32,8192);if(!block){CARD_Close(&file);return -ENOMEM;}
  if(creating){
    /* A new file is never substituted for an existing item. Initialization
     * is streamed; until a valid checkpoint exists it cannot be loaded. */
    for(int p=0;p<page+2;p++){
      if(p<2)memcpy(block,(unsigned char*)bytes+p*8192,8192);else memset(block,0,8192);
      if(p==0){memcpy(block,"DNDSAVE1",8);record_put(block+8,page*8192);record_put(block+12,16384);memcpy(block+16,"DNDREC01",8);record_put(block+24,(uint32_t)expected);record_put(block+28,page);}
      rc=CARD_Write(&file,block,8192,p*8192);if(rc<0)break;
    }
    if(rc>=0){
      card_stat status;rc=CARD_GetStatus(slot,file.filenum,&status);
      if(rc>=0){status.comment_addr=32;status.banner_fmt=CARD_BANNER_RGB;status.icon_fmt=CARD_ICON_RGB;status.icon_speed=1;status.icon_addr=96;rc=CARD_SetStatus(slot,file.filenum,&status);}
    }
  }else{
    rc=CARD_Read(&file,block,8192,0);
    if(rc>=0){
      int count=(int)record_u32(block+28),id=(int)record_u32(block+24);
      if(memcmp(block,"DNDSAVE1",8)||memcmp(block+16,"DNDREC01",8)||count<6||count>256||physical!=(count+2)*8192||record_u32(block+12)!=16384)rc=CARD_ERROR_IOERROR;
      else if(operation==0){free(block);CARD_Close(&file);return count;}
      else if(operation==4){free(block);CARD_Close(&file);return id;}
      else if(id!=expected||page<0||page>=count||length!=8192||!bytes){free(block);CARD_Close(&file);return -EINVAL;}
      else if(operation==1){rc=CARD_Read(&file,block,8192,(page+2)*8192);if(rc>=0)memcpy(bytes,block,8192);}
      else {memcpy(block,bytes,8192);rc=CARD_Write(&file,block,8192,(page+2)*8192);}
    }
  }
  int close_rc=CARD_Close(&file);free(block);return rc<0?card_errno(rc):card_errno(close_rc);
}
