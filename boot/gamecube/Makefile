# Standalone SDK boot component; no managed runtime or libogc linkage.
DEVKITPPC ?= $(DEVKITPRO)/devkitPPC
CC := $(DEVKITPPC)/bin/powerpc-eabi-gcc
PYTHON ?= python3
OUTPUT ?= $(CURDIR)/apploader
DOL ?=
BOOT_DIR := $(dir $(abspath $(lastword $(MAKEFILE_LIST))))
.PHONY: all clean
all: $(OUTPUT).img
$(OUTPUT).elf: $(BOOT_DIR)apploader.c $(BOOT_DIR)link.ld $(BOOT_DIR)Makefile
	@mkdir -p "$(dir $@)"
	$(CC) -mcpu=750 -m32 -mbig-endian -msdata=none -Os -Wall -Wextra -Werror -fno-builtin -fno-pic -fno-stack-protector -fno-asynchronous-unwind-tables -ffunction-sections -nostdlib -Wl,--build-id=none,-T,"$(BOOT_DIR)link.ld" "$(BOOT_DIR)apploader.c" -lgcc -o "$@"
$(OUTPUT).img: $(OUTPUT).elf $(BOOT_DIR)pack.py $(if $(DOL),$(DOL))
	$(PYTHON) "$(BOOT_DIR)pack.py" "$<" "$@" $(if $(DOL),--dol "$(DOL)")
clean:
	$(RM) "$(OUTPUT).elf" "$(OUTPUT).img"
