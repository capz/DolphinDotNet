OUTPUT_FORMAT("elf32-powerpc")
OUTPUT_ARCH(powerpc:common)
ENTRY(dnd_launcher_boot)
SECTIONS {
    . = 0x80003100;
    .text : { *(.text.boot) *(.rodata.resident) }
    /DISCARD/ : { *(.comment) *(.note*) }
}
