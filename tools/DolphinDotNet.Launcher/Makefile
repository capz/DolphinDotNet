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
PYTHON ?= python3
CFLAGS := $(MACHDEP) -O2 -g -std=c11 -Wall -Wextra -Werror -ffunction-sections -fdata-sections -I$(LIBOGC_INC) -D_DEFAULT_SOURCE
CPPFLAGS := -DDND_LAUNCHER_PORT=$(PORT) -DDND_LAUNCHER_IP='"$(IP)"' -DDND_LAUNCHER_MASK='"$(NETMASK)"' -DDND_LAUNCHER_GATEWAY='"$(GATEWAY)"'
OBJECTS := build/gamecube.o build/elf.o build/protocol.o build/trampoline.o
.PHONY: all clean test
all: $(TARGET).dol
build:
	mkdir -p build
build/%.o: %.c elf.h protocol.h resident.h | build
	$(CC) $(CPPFLAGS) $(CFLAGS) -MMD -MP -c $< -o $@
build/trampoline.o: trampoline.S | build
	$(CC) $(MACHDEP) -c $< -o $@
build/receiver.elf: $(OBJECTS) Makefile
	$(CC) $(MACHDEP) -Wl,--gc-sections,-Map,build/receiver.map $(OBJECTS) -L$(LIBOGC_LIB) -lbba -logc -lm -o $@
build/restore.bin: build/trampoline.o
	$(OBJCOPY) -O binary --only-section=.text.dnd_launcher_stub $< $@
build/resident.bin: build/receiver.elf build/restore.bin pack_resident.py resident.h
	$(PYTHON) pack_resident.py build/receiver.elf build/restore.bin $@
build/boot.o: boot.S resident.h build/resident.bin
	$(CC) $(MACHDEP) -c $< -o $@
$(TARGET).elf: build/boot.o boot.ld Makefile
	$(DEVKITPPC)/bin/powerpc-eabi-ld -T boot.ld -Map $(TARGET).map build/boot.o -o $@
$(TARGET).dol: $(TARGET).elf ../../scripts/dol_sections.py
	elf2dol "$<" "$@"
	$(PYTHON) ../../scripts/dol_sections.py --pad "$@"
test:
	$(MAKE) -f tests/Makefile test
clean:
	rm -rf build
-include $(OBJECTS:.o=.d)
