// Mock product-source change for a servicing repro test.
// Pretend fix: Math.Round(double) should round midpoints away from zero.
namespace System
{
    public static partial class MathMock
    {
        // public static double Round(double value) => Math.Round(value, MidpointRounding.AwayFromZero);
    }
}
