// Minimum repro for jeffhandley/dotnet-runtime-servicing-release-manager PR #1
// "[release/9.0] Fix Math.Round to round .5 midpoints away from zero"
//
// Issue: Math.Round(double) uses banker's rounding (MidpointRounding.ToEven) by
// default, so Math.Round(2.5) returns 2 instead of 3. The PR's expected behavior
// is that .5 midpoints round away from zero, i.e. Math.Round(2.5) == 3.

double input = 2.5;
double expected = 3;                 // PR Expected Result: Math.Round(2.5) == 3
double actual = Math.Round(input);   // isolated call site under test

Console.WriteLine($"Call:     Math.Round({input})");
Console.WriteLine($"Expected: {expected}");
Console.WriteLine($"Actual:   {actual}");

if (actual != expected)
{
    Console.WriteLine($"REPRO CONFIRMED: Math.Round({input}) returned {actual}, expected {expected}.");
    return 1;
}

Console.WriteLine("NO REPRO: behavior already matches the expected result.");
return 0;
