# Numeric runtime support

Ordinary supported `System.Math`, `System.MathF`, `System.Random`, primitive parsing and `System.Convert` calls are redirected to managed `Dolphin.Numerics` implementations and a small native libm/parsing bridge. Applications should keep using the normal System APIs. The Core assembly is not a replacement framework assembly, and a successful Roslyn build does not establish that an unlisted overload can be AOT compiled.

## Math and MathF (NM01)

Both expose floating `Abs`, `Sqrt`, `Sin`, `Cos`, `Tan`, `Asin`, `Acos`, `Atan`, `Sinh`, `Cosh`, `Tanh`, `Exp`, `Log` (one argument or explicit base), `Log10`, `Floor`, `Ceiling`, `Truncate`, `Pow`, `Atan2`, `IEEERemainder`, `Min`, `Max`, `Sign` and the four ordinary `Round` forms. `Round` supports all five `MidpointRounding` modes; precision is 0–15 for Math and 0–6 for MathF. Invalid precision/mode and Sign(NaN) raise the corresponding managed exceptions. Min/Max propagate NaN and preserve the appropriate signed zero.

Math additionally supports:

- `Abs` and `Sign` for sbyte, short, int and long. Abs of a signed minimum throws OverflowException.
- `Min`/`Max` for the eight fixed-width integer types and float/double.
- `Clamp` for int, long, float and double.
- `BigMul(int,int)` returning long and `DivRem` with an out remainder for int/long, including zero-divisor and minimum/-1 checks.

Transcendental operations use the target C math library. MathF currently computes through double and narrows to float; this is not a promise of bit-identical results to every platform's dedicated MathF implementation. The tests use numerical tolerances for these functions. Cbrt, Log2, Acosh/Asinh/Atanh, BitIncrement/BitDecrement, ILogB, CopySign, ScaleB and FusedMultiplyAdd are also supported. Float neighbor/scale/FMA operations use their float-width native functions. Decimal Abs/Sign/Min/Max/Clamp and rounding forms use the exact implementation below. Tuple-returning DivRem and complete newer overload coverage remain unavailable.

## Random (NM01)

Supported: `new Random()`, `new Random(int seed)`, `Next()`, `Next(maxValue)`, `Next(minValue,maxValue)`, `NextDouble()` `NextBytes(byte[])`, `NextSingle()` and all three `NextInt64` range forms. Explicit seeds use the classic .NET subtractive algorithm; known seeded sequences, extreme seeds and full-width integer ranges are regression tested. Sample and the public methods are virtual.

Parameterless construction uses a deterministic counter that resets each program boot, with different seeds for successive instances. It does not use wall-clock time or hardware entropy. Random is not cryptographic or thread safe. NextInt64 uses rejection sampling across the full signed range. New methods guarantee range/determinism, not the desktop runtime's exact sequence. Random.Shared and span overloads are unavailable.

## Parsing and conversion (NM02)

Supported static primitive overloads:

| Types | Methods |
| --- | --- |
| Byte, SByte, Int16, UInt16, Int32, UInt32, Int64, UInt64, Single, Double, Boolean | `Parse(string)` and `TryParse(string, out T)` |
| Single, Double | `IsNaN`, `IsInfinity`, `IsPositiveInfinity`, `IsNegativeInfinity`, `IsFinite` |

Integer parsing accepts decimal digits, an optional leading sign and surrounding ASCII numeric whitespace. It checks the complete 32/64-bit range without passing through floating point. Unsigned negative zero is accepted. Parse distinguishes null, format and overflow failures; TryParse returns false and writes zero on failure.

Single/Double parsing accepts decimal/exponent syntax, optional invariant comma grouping in the integer portion, surrounding ASCII numeric whitespace, and case-insensitive NaN/Infinity. It preserves negative zero and follows the modern .NET overflow-to-infinity behavior. The trimmed input is bounded to 4096 UTF-16 characters; larger inputs fail as a format error. Single uses a direct-width Ryu parser for short decimal input, with strtof fallback for longer inputs, to avoid double-rounding. Double uses the corresponding Ryu/strtod paths. The runtime assumes the default C locale; locale changes are unsupported. This is a bounded invariant grammar, not a complete culture-aware .NET parser. Boolean parsing accepts case-insensitive True/False with surrounding Unicode whitespace.

Typed Convert overloads:

| Destination | Source types |
| --- | --- |
| ToSByte, ToByte, ToInt16, ToUInt16, ToInt32, ToUInt32, ToInt64, ToUInt64 | The eight fixed-width integers, char, bool, float, double, string |
| ToSingle, ToDouble | The eight fixed-width integers, float, double, bool, string |
| ToBoolean | The eight fixed-width integers, float, double, bool, string |
| ToChar | The eight fixed-width integers, char, string |

Numeric range violations raise OverflowException. Floating-to-integral Convert rounds ties to even and rejects NaN/infinity. Null strings convert to numeric zero or false; ToChar requires a non-null, single-character string. String conversions use the parsing rules above.

Not implemented for parsing/numeric conversion: NumberStyles, culture/provider, span, radix/base conversion, object/IConvertible. Primitive ToString, typed/object Convert.ToString and invariant formatting providers are now covered by [the string runtime](string-runtime.md). Unsupported calls fail AOT resolution rather than falling back to the desktop BCL.

## Floating-point lowering

Single locals, addressable parameters and return values narrow to their four-byte representation. Conversion to Single explicitly narrows; unsigned integer-to-floating conversion respects unsigned magnitude. Floating remainder uses fmod. Integer widening preserves signedness, and unchecked add/subtract/multiply wrap without invoking C signed-overflow undefined behavior.

The evaluation stack still represents floating temporaries as double. This is not a complete guarantee of modern .NET JIT intermediate precision, NaN payload identity or out-of-range unchecked floating-to-integer casts. General checked CIL arithmetic/conversions remain unsupported; the typed Convert implementations perform their own explicit checks.

## Decimal and BigInteger (NM02)

System.Decimal now maps to an actual signed 96-bit coefficient with scale 0–28. Supported paths include integral/floating/bits constructors, GetBits, comparisons/equality/hash, add/subtract/multiply/divide/remainder, rounding in all five modes, Floor/Ceiling/Truncate, checked integral conversions, floating conversions and invariant Parse/TryParse. Arithmetic uses multiword intermediates and ties-to-even reduction; it is not an alias for double. G (without precision), default output and F/N/C/P formatting preserve the documented scale/precision rules. Standard literal constants and static limits are supported.

System.Numerics.BigInteger uses immutable, normalized arbitrary-length 32-bit limbs. Supported paths include integer/floating/Decimal/byte-array constructors, signed byte-array round trips, comparisons/equality/hash, arithmetic/DivRem, shifts, infinite two's-complement bit operations, Pow/GCD/ModPow, Log/Log10, checked integral conversions, floating/Decimal conversions and invariant decimal Parse/TryParse. G/D formatting and decimal padding are supported. Large values are not restricted to 64 bits. Division is a binary multiword algorithm; large workloads can allocate and run substantially slower than desktop .NET.

Convert adds Decimal input for integer, Boolean, floating and string destinations, plus selected ToDecimal constructors/string paths. Decimal-to-integer Convert rounds ties to even; explicit casts truncate. No general IConvertible or object conversion was added.

Both exact-number families have bounded invariant grammars. NumberStyles, culture-specific parsing, span constructors, generic math interfaces and complete newer overloads remain unsupported. BigInteger hexadecimal/binary formats, Decimal scientific/custom patterns and G precision are unavailable. The facade's byte[] convenience constructor does not establish support for the framework's ReadOnlySpan<byte> constructor.

## Validation

`tests/NumericSmoke` runs the same ordinary System API source under .NET 8 and DolphinDotNet AOT. It covers numeric limits, signed zero, NaN/infinity, rounding, typed exceptions, float ref/out storage, conversion overflow and known seeded Random vectors. `tests/numeric_host.c` runs the compiled code with forced GC, with ASan/UBSan on a 64-bit host and optimized 32-bit execution. Compiler integration regressions cover shared lowering changes. A GameCube DOL runs the suite in Dolphin and verifies the success word after reaching the completion symbol. These cases are wired into CI; physical hardware acceptance remains separate.

`tests/ExactNumberSmoke` adds fixed .NET 8 arithmetic/format vectors, decimal bit/scale comparisons, overflow, large signed values, boxing/unboxing and forced-GC coverage. `tests/WishlistSmoke` checks the added Math/MathF, Random and small-integer parsing paths. Both suites run on sanitized hosts, 32-bit execution and GameCube PowerPC in Dolphin through CI.
