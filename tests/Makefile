CC ?= cc
CFLAGS ?= -std=c11 -Wall -Wextra -Werror -I../include

.PHONY: test clean

test: managed_runtime_tests fst_test card_transaction_test aram_test overlay_test worker_test stw_test input_native_test input_gamecube_test
	./managed_runtime_tests
	./fst_test
	./card_transaction_test
	./aram_test
	./overlay_test
	./worker_test
	./stw_test
	./input_native_test
	./input_gamecube_test

input_gamecube_test: input_gamecube_test.c ../source/input_gamecube.c
	$(CC) $(CFLAGS) -Iinput_sdk $^ -o $@

input_native_test: input_native_test.c input_test_backend.c ../source/input.c
	$(CC) $(CFLAGS) $^ -lm -o $@

managed_runtime_tests: managed_runtime_tests.c ../source/managed_runtime.c ../source/bcl.c ../source/stw.c ../source/gc_stw.c
	$(CC) $(CFLAGS) -pthread $^ -o $@

fst_test: fst_test.c ../source/fst.c
	$(CC) $(CFLAGS) $^ -o $@

card_transaction_test: card_transaction_test.c ../source/card_transaction.c
	$(CC) $(CFLAGS) $^ -o $@

aram_test: aram_test.c ../source/aram.c
	$(CC) $(CFLAGS) -DDND_ARAM_TEST $^ -o $@

stw_test: stw_test.c ../source/stw.c
	$(CC) $(CFLAGS) -pthread $^ -o $@

worker_test: worker_test.c ../source/worker.c
	$(CC) $(CFLAGS) -pthread $^ -o $@

overlay_test: overlay_test.c ../source/performance.c ../source/console_overlay.c
	$(CC) $(CFLAGS) $^ -lm -o $@

clean:
	rm -f managed_runtime_tests fst_test card_transaction_test aram_test overlay_test worker_test stw_test input_native_test input_gamecube_test
