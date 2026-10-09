#ifndef DND_CARD_TRANSACTION_H
#define DND_CARD_TRANSACTION_H
#include <stdbool.h>
#define DND_CARD_JOURNAL ".__dnd_transaction"
typedef struct {
  void *context;
  int (*read)(void *, const char *, void *, int);
  int (*write)(void *, const char *, const void *, int);
  int (*remove)(void *, const char *);
  /* Query (false) or durably publish (true) the verified journal marker. */
  int (*ready)(void *, bool);
  int (*metadata)(void *, const char *, bool, bool, bool);
} DndCardTransaction;
int dnd_card_recover(const DndCardTransaction *);
int dnd_card_replace(const DndCardTransaction *, const char *, const void *,
                     int, bool, bool, bool);
#endif
