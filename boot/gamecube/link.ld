ENTRY(entry)
SECTIONS {
  . = 0x81200000;
  .text : { *(.text.entry) *(.text*) *(.rodata*) }
  .data : { *(.data*) *(.sdata*) }
  .bss : { *(.bss*) *(COMMON) *(.sbss*) }
  /DISCARD/ : { *(.comment) *(.eh_frame*) }
}
