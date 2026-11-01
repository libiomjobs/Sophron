namespace Sophron.Tuning;

/// <summary>
/// How far MIDI's pitch wheel bends, counted in steps of the temperament rather than in semitones, so
/// that a whole throw of the wheel lands exactly on a key.
/// </summary>
public static class PitchBend
{
    /// <summary>Where the wheel rests; it reaches 0 at the bottom and 16383 at the top.</summary>
    public const int Centre = 8192;

    /// <summary>
    /// The bend in cents for a wheel position. The wheel has one fewer position above the centre than
    /// below, so each half is scaled on its own and both ends reach the range exactly.
    /// </summary>
    public static double Cents(int wheel, int rangeSteps, double centsPerStep)
    {
        var offset = wheel - Centre;
        var half = offset >= 0 ? Centre - 1.0 : Centre;
        return offset / half * rangeSteps * centsPerStep;
    }
}
