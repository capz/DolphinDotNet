#!/bin/sh
set -eu
repo_root=$1
out=$2
mkdir -p "$out"
/opt/devkitpro/devkitPPC/bin/powerpc-eabi-gcc -Os -g -mcpu=750 -meabi -mhard-float -msdata=none -ffreestanding -fno-builtin -fno-stack-protector -fno-asynchronous-unwind-tables -nostdlib -Wl,-n -Wl,-T,"$repo_root/tests/DolArgumentsSmoke/argv_target.ld" "$repo_root/tests/DolArgumentsSmoke/argv_target.c" -o "$out/argv_target.elf"
/opt/devkitpro/tools/bin/elf2dol "$out/argv_target.elf" "$out/argv_target.dol"
