/* Real libogc CRT -> relocated loader -> a second libogc CRT, with overlapping
 * executable destinations. Managed API/cleanup is covered separately on host. */
#include "dnd_dol.h"
#include <gccore.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
extern const unsigned char replacement[];
extern const unsigned replacement_size;
int main(void) {
    DndArray *bytes=calloc(1,sizeof(DndArray)+replacement_size);
    if (bytes) {
        bytes->element_size=1;bytes->length=replacement_size;
        memcpy(bytes->data,replacement,replacement_size);
        int token=dnd_dol_prepare(bytes);free(bytes);
        if (token>0 && dnd_dol_request(token)==0) {
            dnd_dol_dispose(token);
            dnd_dol_finish(true);
        }
    }
    for (;;) usleep(1000);
}
/* No filesystem is used by this native handoff fixture. */
bool dnd_fs_path_utf8_native(DndString *p,char *s,unsigned n){(void)p;(void)s;(void)n;return false;}
int dnd_fs_open_utf8_native(const char *p,int m,int a,int s){(void)p;(void)m;(void)a;(void)s;return -1;}
int64_t dnd_fs_length_native(int t){(void)t;return -1;}
int dnd_fs_read_native(int t,void *p,int n){(void)t;(void)p;(void)n;return -1;}
void dnd_fs_close(int t){(void)t;}
