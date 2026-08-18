namespace LPR381.Core.Models;

/// <summary>
/// Floating point helpers. Every comparison of a tableau value in this project goes through
/// this class - pivoting introduces rounding error, so a bare <c>== 0</c> or <c>&lt; 0</c> test
/// will eventually pick the wrong pivot and silently produce a wrong table.
/// </summary>
public static class Num
{
    /// <summary>Anything closer to zero than this is treated as zero.</summary>
    public const double Epsilon = 1e-9;

    /// <summary>Number of decimals used for display, as required by the brief.</summary>
    public const int DisplayDecimals = 3;

    public static bool IsZero(double value) => Math.Abs(value) < Epsilon;

    public static bool IsNegative(double value) => value < -Epsilon;

    public static bool IsPositive(double value) => value > Epsilon;

    /// <summary>True when <paramref name="a"/> and <paramref name="b"/> are equal to within <see cref="Epsilon"/>.</summary>
    public static bool AreEqual(double a, double b) => Math.Abs(a - b) < Epsilon;

    /// <summary>True when the value is a whole number to within <see cref="Epsilon"/>.</summary>
    public static bool IsInteger(double value) => IsZero(value - Math.Round(value));

    /// <summary>The fractional part of a value, always in [0, 1). Used by Branch &amp; Bound and Cutting Plane.</summary>
    public static double FractionalPart(double value)
    {
        double fraction = value - Math.Floor(value);
        return IsZero(fraction) || IsZero(fraction - 1.0) ? 0.0 : fraction;
    }

    /// <summary>Rounds for display only. Internal arithmetic always keeps full precision.</summary>
    public static double Round3(double value)
    {
        double rounded = Math.Round(value, DisplayDecimals, MidpointRounding.AwayFromZero);

        // Avoid printing "-0.000".
        return IsZero(rounded) ? 0.0 : rounded;
    }

    /// <summary>Formats a value the way the output file wants it: three decimals.</summary>
    public static string Format(double value) =>
        Round3(value).ToString("F" + DisplayDecimals, System.Globalization.CultureInfo.InvariantCulture);
}
