#ifndef DND_SYSTEM_H
#define DND_SYSTEM_H
#include <stddef.h>
#include <stdint.h>
#include <stdbool.h>

typedef struct { uint8_t bytes[16]; } DndGuid;

typedef enum {
 DND_EX_NONE=0,DND_EX_EXCEPTION,DND_EX_ARGUMENT,DND_EX_ARGUMENT_NULL,DND_EX_INVALID_OPERATION,
 DND_EX_NOT_SUPPORTED,DND_EX_PLATFORM_NOT_SUPPORTED,DND_EX_IO,DND_EX_FILE_NOT_FOUND,
 DND_EX_DIRECTORY_NOT_FOUND,DND_EX_UNAUTHORIZED,DND_EX_FORMAT
} DndSystemException;

void dnd_system_throw(DndSystemException kind,const char *message);
void dnd_system_clear_exception(void);
DndSystemException dnd_system_exception(void);
const char *dnd_system_exception_message(void);

bool dnd_guid_new(DndGuid *value);
bool dnd_guid_parse(const char *text,DndGuid *value);
void dnd_guid_to_string(const DndGuid *value,char output[37]);

void dnd_console_write(const char *text);
void dnd_console_write_line(const char *text);
void dnd_console_write_i32(int32_t value);

bool dnd_file_exists(const char *path);
bool dnd_file_read_all_bytes(const char *path,uint8_t **data,size_t *length);
bool dnd_file_write_all_bytes(const char *path,const uint8_t *data,size_t length);

bool dnd_path_is_separator(char c);
const char *dnd_path_get_file_name(const char *path);
bool dnd_path_combine(const char *left,const char *right,char *output,size_t output_size);

const char *dnd_appcontext_base_directory(void);
bool dnd_appcontext_try_get_switch(const char *name,bool *enabled);
#endif
