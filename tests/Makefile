CC ?= cc
CFLAGS ?= -std=c11 -Wall -Wextra -Werror -I../include

.PHONY: test clean

test: runtime_tests managed_runtime_tests fst_test card_transaction_test
	./runtime_tests
	./managed_runtime_tests
	./fst_test
	./card_transaction_test

runtime_tests: runtime_tests.c ../source/runtime.c
	$(CC) $(CFLAGS) $^ -o $@

managed_runtime_tests: managed_runtime_tests.c ../source/managed_runtime.c ../source/bcl.c
	$(CC) $(CFLAGS) $^ -o $@

fst_test: fst_test.c ../source/fst.c
	$(CC) $(CFLAGS) $^ -o $@

card_transaction_test: card_transaction_test.c ../source/card_transaction.c
	$(CC) $(CFLAGS) $^ -o $@

clean:
	rm -f runtime_tests managed_runtime_tests fst_test card_transaction_test
