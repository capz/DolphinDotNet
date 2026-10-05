# DolphinDotNet.Compiler

Two compiler paths currently coexist:

- default: legacy bootstrap compiler emitting DND bytecode/header output;
- `--aot`: closed-world compiler importing reachable CIL into DND IR and emitting native C.

Usage:

```
dotnet run --project tools/DolphinDotNet.Compiler -- --aot Managed.dll generated_program.c
```

The AOT path currently imports constants, arguments, integer add/sub/mul, `newobj`, instance field load/store, direct calls, virtual calls and returns. Unsupported IL fails compilation deliberately rather than silently producing incorrect native code.

The next lowering step will turn stack-oriented IR operations into typed temporaries and actual runtime ABI calls.
