using System.Text.Json.Serialization;
using Sophron.Tuning;

namespace Sophron.Songs;

/// <summary>
/// A note as Sophron keeps it: a step of the song's temperament, counted from A0 and unbounded either
/// way, rather than a MIDI note number, which would reach less than five octaves of 27-TET. Start and
/// length are in ticks, so a change of tempo carries every note with it.
/// </summary>
/// <param name="Start">When the key went down, in ticks.</param>
/// <param name="Length">How long it was held, in ticks.</param>
/// <param name="Step">The step it played, 0 being A0.</param>
/// <param name="Velocity">How hard the key went down, from 0 to 1.</param>
public readonly record struct Note(long Start, long Length, int Step, float Velocity)
{
    public long End => Start + Length;
}

/// <summary>The pitch wheel, in cents rather than wheel positions, so a later change of range leaves it as played.</summary>
public readonly record struct BendPoint(long Time, double Cents);

/// <summary>The sustain pedal going down or coming up.</summary>
public readonly record struct PedalChange(long Time, bool Down);

/// <summary>One part: its notes, and the wheel and pedal as they were played with them.</summary>
public sealed class Track
{
    public string Name { get; set; } = "Track 1";

    public List<Note> Notes { get; set; } = [];

    public List<BendPoint> Bends { get; set; } = [];

    public List<PedalChange> Sustain { get; set; } = [];

    /// <summary>Where the last note ends, or 0 for an empty track.</summary>
    [JsonIgnore]
    public long End => Notes.Count == 0 ? 0 : Notes.Max(note => note.End);
}

/// <summary>
/// A song: its tuning, its tempo and its tracks. The steps of every note mean something only in the
/// song's own temperament, which is why the song, not the window, holds it.
/// </summary>
public sealed class Song
{
    /// <summary>Ticks to the beat: fine enough for any human playing, and divisible by 2, 3, 4, 5 and 6.</summary>
    public const int TicksPerBeat = 960;

    public int StepsPerOctave { get; set; } = Tet27.StepsPerOctave;

    public double FundamentalHz { get; set; } = Temperament.Fundamental432;

    /// <summary>Beats per minute.</summary>
    public double Tempo { get; set; } = 120;

    public int BeatsPerBar { get; set; } = 4;

    public List<Track> Tracks { get; set; } = [new Track()];

    [JsonIgnore]
    public Temperament Temperament
    {
        get => new(StepsPerOctave, FundamentalHz);
        set => (StepsPerOctave, FundamentalHz) = (value.StepsPerOctave, value.FundamentalHz);
    }

    [JsonIgnore]
    public long TicksPerBar => (long)BeatsPerBar * TicksPerBeat;

    [JsonIgnore]
    public double SecondsPerTick => 60.0 / (Tempo * TicksPerBeat);

    [JsonIgnore]
    public bool IsEmpty => Tracks.All(track => track.Notes.Count == 0);
}
