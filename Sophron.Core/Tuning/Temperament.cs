namespace Sophron.Tuning;

/// <summary>
/// An equal temperament: the octave cut into <see cref="StepsPerOctave"/> equal steps, counted from A0,
/// which sounds at <see cref="FundamentalHz"/>. A4 lies four octaves up, at sixteen times the fundamental.
/// </summary>
public sealed record Temperament(int StepsPerOctave, double FundamentalHz)
{
    /// <summary>A0 for A4 at 432 Hz.</summary>
    public const double Fundamental432 = 27.0;

    /// <summary>A0 for A4 at 440 Hz, concert pitch.</summary>
    public const double Fundamental440 = 27.5;

    /// <summary>The lowest A0 the tuner reaches: A4 at 400 Hz.</summary>
    public const double LowestFundamentalHz = 25.0;

    /// <summary>The highest A0 the tuner reaches: A4 at 480 Hz.</summary>
    public const double HighestFundamentalHz = 30.0;

    private const int A4Octave = 4;

    public static Temperament Twelve(double fundamentalHz = Fundamental440) => new(StandardKeyMap.KeysPerOctave, fundamentalHz);

    public static Temperament TwentySeven(double fundamentalHz = Fundamental432) => new(Tet27.StepsPerOctave, fundamentalHz);

    /// <summary>Whether this is the temperament a standard keyboard was made for, one key to a step.</summary>
    public bool IsStandard => StepsPerOctave == StandardKeyMap.KeysPerOctave;

    public double CentsPerStep => 1200.0 / StepsPerOctave;

    public double A4Hz => Frequency(A4Octave * StepsPerOctave);

    public double Frequency(int step) => FundamentalHz * Math.Pow(2.0, step / (double)StepsPerOctave);

    public double Cents(int step) => step * CentsPerStep;

    /// <summary>The octave a step lies in, counted from A0 as octave 0.</summary>
    public int Octave(int step) => Tet27.FloorDiv(step, StepsPerOctave);

    /// <summary>Where a step lies within its octave, from 0 up to one short of <see cref="StepsPerOctave"/>.</summary>
    public int Degree(int step) => Tet27.Mod(step, StepsPerOctave);

    /// <summary>
    /// The step a standard keyboard's key plays: itself, counted from A0, in 12-TET; its nearest step
    /// by <paramref name="map"/> in 27-TET.
    /// </summary>
    public int StepFor(int midiNote, StandardKeyMap map) =>
        IsStandard ? midiNote - StandardKeyMap.A0MidiNote : map.StepFor(midiNote);

    /// <summary>The standard key that plays a step, or null if none does.</summary>
    public int? MidiNoteFor(int step, StandardKeyMap map) =>
        IsStandard ? (StandardKeyMap.A0MidiNote + step is >= 0 and <= 127 and var note ? note : null) : map.MidiNoteFor(step);

    /// <summary>The A0 that puts A4 at <paramref name="a4Hz"/>.</summary>
    public static double FundamentalFor(double a4Hz) => a4Hz / Math.Pow(2.0, A4Octave);
}
