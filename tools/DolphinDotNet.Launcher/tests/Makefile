HOST_CC ?= cc
HOST_CFLAGS ?= -std=c11 -O1 -g -Wall -Wextra -Werror
PEER_CFLAGS ?= -std=c11 -O2 -g -Wall -Wextra -Werror
.PHONY: test
test: build/core_test build/stdio_receiver
	./build/core_test
	python3 tests/resident_test.py
build:
	mkdir -p build
build/core_test: tests/core_test.c elf.c protocol.c elf.h protocol.h resident.h | build
	$(HOST_CC) $(HOST_CFLAGS) -I. tests/core_test.c elf.c protocol.c -o $@
build/stdio_receiver: tests/stdio_receiver.c elf.c protocol.c elf.h protocol.h resident.h | build
	$(HOST_CC) $(PEER_CFLAGS) -I. tests/stdio_receiver.c elf.c protocol.c -o $@
