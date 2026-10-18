# Ryu numeric conversion

Upstream: https://github.com/ulfjack/ryu
Pinned commit: 4c0618b0e44f7ef027ebae05d2cc7812048f7c8f
License selected: Boost Software License 1.0 (LICENSE-Boost).

Unmodified d2s/f2s/d2fixed/s2d/s2f sources and required headers are embedded by
source/numeric.c, with macros isolating symbols and duplicate private helper
names. Integer-based shortest/fixed/scientific output avoids newlib's subnormal
precision bugs. The upstream decimal parsers are marked experimental and limited
to 17/9 significant digits and decimal syntax. DolphinDotNet validates/normalizes
its bounded grammar first and falls back to libc for unsupported lengths.

Fixed .NET reference vectors, sanitizer/32-bit tests and PowerPC Dolphin tests
cover the integrated paths. This is not exhaustive parser conformance. Standalone
bundles copy this directory, including its license and provenance.
