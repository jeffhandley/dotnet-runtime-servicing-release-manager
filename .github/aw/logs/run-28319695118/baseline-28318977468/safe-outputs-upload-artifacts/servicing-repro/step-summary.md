## Servicing repro — PR #1 (`[release/9.0] Fix Math.Round to round .5 midpoints away from zero`)

**Classification:** INCLUDE — base `release/9.0`; labels `Servicing-approved` + product area `area-System.Runtime`; product source `src/libraries/System.Private.CoreLib/src/System/Math.cs`; Customer Impact + `Fixes #1`.

**Repro form:** minimal **csproj console app** targeting `net9.0` (the preferred xunit unit test cannot restore offline; file-based `dotnet run app.cs` requires .NET 10+). Built and run on the **baseline GA .NET 9 SDK 9.0.315** (runtime 9.0.17).

**Isolating snippet** (`repro/Program.cs`):

```csharp
double input = 2.5;
double expected = 3;                 // PR Expected Result: Math.Round(2.5) == 3
double actual = Math.Round(input);   // isolated call site under test
// prints Expected/Actual; exits 1 when actual != expected
```

**Expected Result:** `Math.Round(2.5)` returns `3`

**Actual Result** (quoted from `output.log`):

```
=== dotnet run ===
Call:     Math.Round(2.5)
Expected: 3
Actual:   2
REPRO CONFIRMED: Math.Round(2.5) returned 2, expected 3.
exit=1
```

**Verdict:** Bug **reproduces** on the baseline GA .NET 9 SDK — `Math.Round(2.5)` returns `2` (banker's rounding / MidpointRounding.ToEven) instead of `3`.

**Artifact:** `servicing-repro-pr-1` (repro sources + `output.log`).
