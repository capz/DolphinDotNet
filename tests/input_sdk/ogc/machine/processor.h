#ifndef INPUT_TEST_PROCESSOR_H
#define INPUT_TEST_PROCESSOR_H
extern unsigned input_test_interrupt_depth;
#define _CPU_ISR_Disable(level) do { (level)=input_test_interrupt_depth; input_test_interrupt_depth++; } while(0)
#define _CPU_ISR_Restore(level) do { input_test_interrupt_depth=(level); } while(0)
#endif
