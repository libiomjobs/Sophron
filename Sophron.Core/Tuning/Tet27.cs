namespace Sophron.Tuning;

/// <summary>
/// The three shades of a 27-TET key. Every third step is a tone of 9-TET and is white; the step just
/// above it is light gray, and the step just below the next white is dark gray.
/// </summary>
public enum KeyShade
{
    White,
    Light,
    Dark,
}

/// <summary>
/// 27-tone equal temperament, counted in steps from A0. Step 0 is 27 Hz, so A4, four octaves and
/// 108 steps up, is 27 × 2⁴ = 432 Hz.
/// </summary>
public static class Tet27
{
    public const int StepsPerOctave = 27;

    /// <summary>The steps of 9-TET fall on every third step of 27-TET.</summary>
    public const int StepsPerShadeCycle = 3;

    /// <summary>The frequency of step 0, A0.</summary>
    public const double FundamentalHz = 27.0;

    public const double CentsPerStep = 1200.0 / StepsPerOctave;

    public static double Frequency(int step) => FundamentalHz * Math.Pow(2.0, step / (double)StepsPerOctave);

    public static double Cents(int step) => step * CentsPerStep;

    public static KeyShade Shade(int step) => (KeyShade)Mod(step, StepsPerShadeCycle);

    /// <summary>The octave a step lies in, counted from A0 as octave 0, so A4 is in octave 4.</summary>
    public static int Octave(int step) => FloorDiv(step, StepsPerOctave);

    /// <summary>Where a step lies within its octave, from 0 to 26.</summary>
    public static int Degree(int step) => Mod(step, StepsPerOctave);

    internal static int Mod(int value, int modulus) => ((value % modulus) + modulus) % modulus;

    internal static int FloorDiv(int value, int divisor) => (value - Mod(value, divisor)) / divisor;
}
