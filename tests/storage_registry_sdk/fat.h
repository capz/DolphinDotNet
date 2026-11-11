/* The registry fixture supplies its own backend; no FAT APIs are called. */
int FAT_getAttr(const char *path);
int FAT_setAttr(const char *path,unsigned attributes);
