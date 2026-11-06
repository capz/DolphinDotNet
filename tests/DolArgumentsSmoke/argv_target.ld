ENTRY(_start)
SECTIONS {
 . = 0x80004000;
 .text : { *(.text.entry) *(.text*) }
 .rodata : { *(.rodata*) }
 .data : { *(.data*) }
 .bss (NOLOAD) : { *(.bss*) *(COMMON) }
 /DISCARD/ : { *(.comment) *(.eh_frame*) }
}
