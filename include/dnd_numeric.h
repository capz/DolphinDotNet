#ifndef DND_NUMERIC_H
#define DND_NUMERIC_H
#include "dnd_managed.h"
bool dnd_format_provider(DndObject *);
DndString *dnd_format_integer(DndManagedHeap *,int64_t,int,DndString *);
DndString *dnd_format_float(DndManagedHeap *,double,int,DndString *);
double dnd_numeric_unary(int op,double x);
double dnd_numeric_binary(int op,double a,double b);
double dnd_numeric_round(double x,int digits,int mode);
int dnd_numeric_sign(double x);
int dnd_numeric_classify(int op,double x);
int dnd_numeric_i32(DndString *s,intptr_t out,int throwing);
int dnd_numeric_u32(DndString *s,intptr_t out,int throwing);
int dnd_numeric_i64(DndString *s,intptr_t out,int throwing);
int dnd_numeric_u64(DndString *s,intptr_t out,int throwing);
int dnd_numeric_f32(DndString *s,intptr_t out,int throwing);
int dnd_numeric_f64(DndString *s,intptr_t out,int throwing);
#endif
