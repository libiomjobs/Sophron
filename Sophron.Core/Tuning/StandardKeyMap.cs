namespace Sophron.Tuning;

/// <summary>Which way a standard key goes when it lies exactly halfway between two steps.</summary>
public enum Tie
{
    Lower,
    Upper,
}

/// <summary>
/// Plays 27-TET from an ordinary twelve-key keyboard by sending each key to the nearest step.
/// </summary>
/// <remarks>
/// A semitone is 2.25 steps, so A, C♯ and F land exactly, the other keys are 11.1 cents out, and
/// B, D♯ and G lie exactly halfway between two steps (22.2 cents either way). Those three are not
/// decided by "nearest", so each has its own <see cref="Tie"/>.
/// </remarks>
public sealed record StandardKeyMap(Tie B = Tie.Upper, Tie DSharp = Tie.Upper, Tie G = Tie.Upper)
{
    /// <summary>MIDI's A0, the lowest key of a piano, which plays step 0.</summary>
    public const int A0MidiNote = 21;

    public const int KeysPerOctave = 12;

    /// <summary>The standard keys, starting from A as the steps do.</summary>
    public static IReadOnlyList<string> KeyNames { get; } = ["A", "A♯", "B", "C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯"];

    public static StandardKeyMap Default { get; } = new();

    /// <summary>The step within the octave that the key <paramref name="keyFromA"/> semitones above A plays.</summary>
    public int Degree(int keyFromA)
    {
        // A key k semitones above A is 9k/4 steps above it; working in quarters keeps it exact.
        var quarters = Tet27.Mod(keyFromA, KeysPerOctave) * 9;
        var lower = quarters / 4;
        return (quarters % 4) switch
        {
            < 2 => lower,
            > 2 => lower + 1,
            _ => TieFor(keyFromA) == Tie.Upper ? lower + 1 : lower,
        };
    }

    public int StepFor(int midiNote)
    {
        var fromA0 = midiNote - A0MidiNote;
        return (Tet27.FloorDiv(fromA0, KeysPerOctave) * Tet27.StepsPerOctave) + Degree(fromA0);
    }

    /// <summary>The MIDI note that plays <paramref name="step"/>, or null when no standard key reaches it.</summary>
    public int? MidiNoteFor(int step)
    {
        var degree = Tet27.Degree(step);
        for (var key = 0; key < KeysPerOctave; key++)
        {
            if (Degree(key) == degree)
            {
                return A0MidiNote + (Tet27.Octave(step) * KeysPerOctave) + key;
            }
        }

        return null;
    }

    /// <summary>The usual name of a MIDI note, such as A4 for 69; octaves begin at C, as they always have.</summary>
    public static string NoteName(int midiNote)
    {
        var fromA0 = midiNote - A0MidiNote;
        var key = Tet27.Mod(fromA0, KeysPerOctave);
        var octave = Tet27.FloorDiv(midiNote, KeysPerOctave) - 1;
        return KeyNames[key] + octave;
    }

    private Tie TieFor(int keyFromA) => Tet27.Mod(keyFromA, KeysPerOctave) switch
    {
        2 => B,
        6 => DSharp,
        10 => G,
        _ => throw new ArgumentOutOfRangeException(nameof(keyFromA), "Only B, D♯ and G lie halfway between steps."),
    };
}
