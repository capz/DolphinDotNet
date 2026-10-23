ifeq ($(strip $(DEVKITPRO)),)
$(error Set DEVKITPRO and DEVKITPPC, or build with the documented pinned container)
endif
include $(DEVKITPRO)/libogc2/gamecube_rules
.DELETE_ON_ERROR:
PORT ?= 4299
IP ?=
NETMASK ?= 255.255.255.0
GATEWAY ?= 0.0.0.0
TARGET := build/DolphinDotNet.Launcher
CFLAGS := $(MACHDEP) -O2 -g -std=c11 -Wall -Wextra -Werror -ffunction-sections -fdata-sections -I$(LIBOGC_INC) -D_DEFAULT_SOURCE
CPPFLAGS := -DDND_LAUNCHER_PORT=$(PORT) -DDND_LAUNCHER_IP='"$(IP)"' -DDND_LAUNCHER_MASK='"$(NETMASK)"' -DDND_LAUNCHER_GATEWAY='"$(GATEWAY)"'
OBJECTS := build/gamecube.o build/elf.o build/protocol.o build/trampoline.o
.PHONY: all clean test
all: $(TARGET).dol
build:
	mkdir -p build
build/%.o: %.c elf.h protocol.h | build
	$(CC) $(CPPFLAGS) $(CFLAGS) -MMD -MP -c $< -o $@
build/trampoline.o: trampoline.S | build
	$(CC) $(MACHDEP) -c $< -o $@
$(TARGET).elf: $(OBJECTS)
	$(CC) $(MACHDEP) -Wl,--gc-sections,-Map,$(TARGET).map $^ -L$(LIBOGC_LIB) -lbba -logc -lm -o $@
test:
	$(MAKE) -f tests/Makefile test
clean:
	rm -rf build
-include $(OBJECTS:.o=.d)
