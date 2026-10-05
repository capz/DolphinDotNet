# API surface coverage

The API scanner compares decoded declaration signatures rather than assembly-local metadata tokens. It includes public/protected members of accessible types, nested type names, generic parameters, arrays, pointers, byrefs, custom modifiers, static/instance methods and field storage, and event handler types. Properties and events require an accessible accessor; private properties and nested types inside inaccessible parents do not count.

Run the regression fixtures:

```
dotnet run --project tests/ApiCompat/ApiCompat.Tests.csproj -c Release
```

Scan a contract and an implementation:

```
dotnet run --project tools/DolphinDotNet.ApiCompat -- contract-directory implementation.dll
```

Missing declaration keys go to standard output and the summary goes to standard error. Exit codes are 0 for no missing declarations, 1 for missing declarations, and 2 for input/usage errors. Empty directories are input errors.

An optional third argument names a text file with one scanner declaration key per line. Blank lines and lines beginning with `#` are ignored. Runtime-characterized coverage counts only declarations present in the reference, implementation, and characterization file. These keys are now readable decoded signatures; previous raw-hex keys must be regenerated. A characterization file records external test evidence, and the scanner does not verify that evidence itself.

This is a declaration inventory, not a conformance verifier. It does not compare behavior, inheritance obligations, generic constraints, attributes, optional parameter defaults, constant values, or type-forwarded declarations. Assembly scopes are intentionally omitted so facade and implementation assemblies can be compared by full type name; unrelated assemblies defining the same full type name will collide. Scanner coverage cannot prove a library executes on GameCube.

DolphinDotNet.Core currently exposes DolphinDotNet-namespaced helpers. Those helpers are groundwork for a BCL, but do not implement the corresponding `System.*` contracts merely by providing similar algorithms. Report actual contract coverage separately from host tests and AOT/runtime execution tests.