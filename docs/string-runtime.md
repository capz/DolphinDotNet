# Strings and formatting (ST01–ST04)

Normal supported System.String, primitive ToString, System.Convert.ToString, System.Text.StringBuilder and compiler-generated interpolation calls now have compiled implementations. The compiler uses managed Dolphin.Text facades, UTF-16 runtime primitives and invariant native numeric formatting. Applications should keep using the System APIs and reference DolphinDotNet.Core for the managed facades; a generated GameCube bundle includes the required text/numeric sources automatically.

This is a broad console-oriented subset, not complete .NET String/globalization conformance. Formatting defaults are invariant. Parameterless StartsWith/EndsWith/IndexOf and comparison helpers use ordinal behavior, including paths retained from the earlier native runtime. Desktop .NET uses culture-sensitive defaults for some of these APIs. Use explicit StringComparison.Ordinal or OrdinalIgnoreCase for portable intent. Explicit culture comparison modes throw NotSupportedException; invalid enum values throw ArgumentException.

## ST01: storage, construction and manipulation

Supported families and overloads:

| API | Implemented forms |
| --- | --- |
| Length, character indexer, Empty | UTF-16 code-unit semantics, static empty string, null/index checks |
| Constructors | char[], char[] plus start/count, repeated char plus count; null char[] produces an empty string |
| Equality | Operators, static Equals with two strings or comparison, instance Equals(string/object) and Equals(string,comparison) |
| Substring | start and start/count; null and invalid-range exceptions |
| Concat | Two/three/four strings; string[]; one/two/three objects and object[]; IEnumerable<string> and generic IEnumerable<T> |
| Join | String separator with string[], array slice, object[], IEnumerable<string>/IEnumerable<T>; char separator with string[]/object[]/IEnumerable<T> |
| Insert, Remove, Replace | Insert string; Remove start or start/count; char replacement; ordinal string replacement with optional comparison |
| Trim, TrimStart, TrimEnd | Whitespace, char or char[]; Unicode whitespace classification |
| PadLeft, PadRight | Width with optional padding char |
| CopyTo, ToCharArray | Character copy with ranges; full string or start/count array conversion |
| Split | char/char[]/string/string[] separators, count/options combinations listed in StringOps.cs; None, RemoveEmptyEntries and TrimEntries |
| ToUpperInvariant, ToLowerInvariant | Simple Unicode casing, including supplementary characters and preserved unmatched surrogates |
| Compare/CompareOrdinal/CompareTo | Whole-string and substring/range ordinal comparisons, explicit comparison option; CompareTo(string/object) |
| GetEnumerator | CharEnumerator with Current/MoveNext/Reset/Clone/Dispose |
| Copy, Clone, ToString | Selected legacy copy/identity paths |

Concat/Join treat null entries as empty. Generic enumerable overloads work with managed lists/custom enumerables and have a dedicated path for typed scalar/reference/struct arrays, so they do not depend on general array-as-interface enumeration. Untyped or unmaterialized array element types remain unavailable. Object concatenation/joining uses virtual ToString; composite formatting and interpolation additionally honor IFormattable.

Casing and ordinal-ignore-case use the pinned Unicode 16 simple case tables in include/dnd_unicode_upper.h and include/dnd_unicode_lower.h. They do not perform culture-specific or expanding case mappings. Dotted/dotless Turkish I retain invariant/ordinal behavior. Unicode-version differences from a particular desktop .NET installation are possible for newer characters.

## ST02: searches

- StartsWith/EndsWith: string, string plus comparison, char.
- Contains: string or char, including comparison forms.
- IndexOf: string or char, start and start/count ranges; string comparison forms, plus char/comparison.
- LastIndexOf: char and string, start/start-count variants; string comparison forms listed in StringOps.cs.
- IndexOfAny/LastIndexOfAny: char[] with full string, start, or start/count ranges.

Indices and ranges are UTF-16 code-unit positions. OrdinalIgnoreCase compares Unicode simple uppercase units, including surrogate pairs within the selected range; it does not expand characters into multiple letters. Search/replacement implementations are direct scans, not a claim of the desktop runtime's SIMD or long-pattern performance.

## ST03: primitive and object formatting

Supported ToString receivers: sbyte, byte, short, ushort, int, uint, long, ulong, float, double, bool and char. Parameterless and invariant-provider forms are supported; numeric types also accept a standard format string and optional provider. Boxed primitive calls and user class/struct ToString overrides use runtime dispatch, including inherited overrides and class base calls. Convert.ToString supports these primitives, string and object, with optional invariant provider. A null object converts to empty text; a null string remains null for the string overload.

| Numeric types | Format codes |
| --- | --- |
| Fixed-width integers | D/d, X/x, B/b, G/g, F/f, N/n, E/e, C/c, P/p |
| Single/Double | G/g, R/r, F/f, N/n, E/e, C/c, P/p |

Precision is bounded to 0–99. Integer hexadecimal/binary output preserves the receiver's two's-complement width. Integer G/E precision uses .NET's decimal half-up behavior; floating precision uses round-to-nearest-even. N uses invariant comma grouping and a period decimal separator. G/R use Ryu shortest conversion (up to 9 Single or 17 Double significant digits) and normalize exponent notation. F/N/E also use integer-based Ryu conversion. Negative zero, NaN and infinity have explicit output. The pinned Ryu source and Boost license are included under include/ryu. Its experimental 9/17-digit decimal parsers handle the short forms used for round trips; longer inputs fall back to libc after grammar validation. Fixed reference vectors establish tested cases, not exhaustive floating-bit-pattern conformance.

CultureInfo.InvariantCulture is supported as a small provider marker. CurrentCulture always represents the invariant culture; assigning the invariant marker is allowed. No culture database or NumberFormatInfo surface is implemented. Null providers also select invariant behavior. Other provider objects are rejected by built-in numeric/composite formatting. User IFormattable implementations receive the supplied supported provider.

On PowerPC, managed entry and managed async dispatch select IEEE subnormal behavior rather than libogc's flush-to-zero default. Entry restores the native caller's FPSCR on normal return; native formatter/parser guards also restore it during managed exception unwinding. This prevents Single.Epsilon from disappearing before ToString reads it.

## ST04: builder, composite formatting and interpolation

StringBuilder uses a managed growable char[] and returns immutable string snapshots. Implemented members include:

- Constructors: default, capacity, capacity/maxCapacity, string, string/capacity, string/start/length/capacity.
- Length, Capacity, MaxCapacity, character indexer, EnsureCapacity, Clear; ToString and ToString(start,length).
- Append: string and slice, char/repeat, char[] and slice, primitive values, object, StringBuilder and builder slice. Self-appending is supported.
- AppendLine with optional string; AppendFormat one/two/three/params object arguments and provider variants; AppendJoin string/char separators with string[] or object[].
- Insert: string/repeat, primitives/object, char[] and slice; Remove; Replace char/string with optional range; CopyTo; content-based Equals(StringBuilder).

Capacity growth is geometric; this implementation is not the desktop chunked builder. MaxCapacity is enforced strictly, and AppendLine emits LF for the console. Generic AppendJoin(string/char, IEnumerable<T>) is also supported. Pointer/span/chunk enumeration remains unavailable.

String.Format and AppendFormat implement numbered items, repeated arguments, positive/negative alignment, optional numeric format strings and escaped literal braces. Null arguments become empty text. Missing arguments, malformed braces, invalid alignment/item syntax and unsupported numeric formats throw FormatException. Item indices/alignment are bounded below one million. Existing virtual ToString and IFormattable implementations are honored.

Modern C# default interpolated strings, aligned/formatted items, String.Create(invariantProvider, $"…") and StringBuilder.Append/AppendLine interpolated handlers are supported. Handler values retain GC roots for their builders/providers. Span-based formatted items, arbitrary custom handler types remain outside this slice. FormattableString, its compiler factory, argument access and invariant composite rendering are supported.

## Remaining gaps

Full culture-sensitive collation/casing, normalization, complete String interface enumeration, unsafe/span overloads and every newer framework overload remain incomplete. Enum/date/time formatting, custom numeric patterns (such as "0.00"), custom IFormatProvider/ICustomFormatter services and precision above 99 remain unsupported. Nullable integer/Boolean/Char ToString returns empty for missing values and formats present values. Floating and aggregate nullable payloads remain unavailable. Decimal/BigInteger have the selected formats described in [numerics](numerics.md). These gaps are not counted as full ST01–ST04 conformance.

## Evidence

tests/StringSmoke exercises the same ordinary System API source on .NET 8 and AOT. GoldenFormats.cs contains fixed invariant .NET output vectors for subnormals, limits, shortest/general notation, precision and grouping. The suite checks null/range/format errors, Unicode searches/casing, split limits, builder mutations/self-appends, custom formatting, interpolation and byte/char byrefs. It runs with forced GC, optimized ASan/UBSan, optimized 32-bit execution and GameCube PowerPC execution in Dolphin. CI verifies the target completion symbol and success word. Compiler integration and native managed-runtime regressions cover the shared lowering/runtime changes; physical hardware acceptance remains separate.

`tests/WishlistSmoke` adds differential coverage for explicit CharEnumerator, nullable text, substring comparison, generic AppendJoin, FormattableString and invariant currency/percent output.
