# Architecture

## Goal

Run a useful, intentionally constrained C#/.NET programming model on Nintendo GameCube hardware.

The GameCube has a 486 MHz PowerPC Gekko CPU and 24 MB of main 1T-SRAM, so DolphinDotNet treats desktop .NET compatibility as an API/source-compatibility goal rather than attempting to port CoreCLR.

## Layers

### Platform
The libogc-facing layer owns video/console, controller input, timing and eventually filesystem/network services.

### Runtime
Portable C implements managed object layout, allocation, strings, arrays and later exception/GC support. Keeping this layer portable makes it testable on a normal development machine.

### Execution
The MVP contains a tiny interpreter to validate managed-runtime mechanics. The intended production path is offline AOT:

```
C# project
   |
   v
.NET assembly (restricted IL)
   |
   v
DolphinDotNet compiler
   |
   +--> generated PowerPC/native C or objects
   +--> metadata tables
   |
   v
devkitPPC + libogc
   |
   v
GameCube .dol
```

AOT avoids carrying a JIT compiler and full metadata machinery on the console.

## Compatibility strategy

Implement APIs according to usefulness, not breadth. Initial candidates:

- System.Object
- System.ValueType
- System.String
- System.Array
- primitive numeric types
- System.Console mapped to the GameCube console
- System.Math
- basic generic collections where AOT specialization is practical

Unsupported reflection/dynamic-code APIs should fail at build time rather than unpredictably at runtime.

## Memory

The MVP uses a bump allocator because it is tiny and deterministic. The next meaningful allocator milestone should introduce explicit roots and a simple mark/sweep collector. Large native resources such as textures and audio buffers should remain native handles instead of managed heap payloads.
