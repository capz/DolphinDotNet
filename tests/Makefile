CC ?= cc
CFLAGS ?= -std=c11 -O2 -Wall -Wextra -Werror

.PHONY: test clean

test: runtime_tests
	./runtime_tests

runtime_tests: runtime_tests.c ../source/runtime.c
	$(CC) $(CFLAGS) -I../include $^ -o $@

clean:
	rm -f runtime_tests
