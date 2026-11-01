namespace Sophron.Songs;

public enum SongEventKind
{
    NoteOff,
    NoteOn,
    Bend,
    Sustain,
    Click,
}

/// <summary>
/// Something to happen at a tick. <see cref="Value"/> is the velocity of a note-on, the cents of a
/// bend, 1 or 0 for the pedal, and 1 for the first click of a bar.
/// </summary>
public readonly record struct SongEvent(long Tick, SongEventKind Kind, int Step, double Value);

/// <summary>What a stretch of a song asks the engine to do, in the order it must be done.</summary>
public static class Schedule
{
    /// <summary>
    /// The events of <paramref name="track"/> from <paramref name="from"/> up to, but not including,
    /// <paramref name="to"/>. A note that ends where another begins on the same step is stopped first,
    /// so the second is not cut off by the first. Notes on one step share a voice, so where two overlap
    /// the first one's end is left out, and the step sounds until the later of them ends.
    /// </summary>
    public static List<SongEvent> Events(Track track, long from, long to)
    {
        var events = new List<SongEvent>();
        var notes = track.Notes;
        foreach (var note in notes)
        {
            if (note.Start >= from && note.Start < to)
            {
                events.Add(new SongEvent(note.Start, SongEventKind.NoteOn, note.Step, note.Velocity));
            }

            if (note.End >= from && note.End < to && !Overlapped(notes, note))
            {
                events.Add(new SongEvent(note.End, SongEventKind.NoteOff, note.Step, 0));
            }
        }

        foreach (var bend in track.Bends.Where(bend => bend.Time >= from && bend.Time < to))
        {
            events.Add(new SongEvent(bend.Time, SongEventKind.Bend, 0, bend.Cents));
        }

        foreach (var pedal in track.Sustain.Where(pedal => pedal.Time >= from && pedal.Time < to))
        {
            events.Add(new SongEvent(pedal.Time, SongEventKind.Sustain, 0, pedal.Down ? 1 : 0));
        }

        return Sorted(events);
    }

    /// <summary>A click on every beat from <paramref name="from"/> up to <paramref name="to"/>, the first of each bar marked.</summary>
    public static List<SongEvent> Clicks(Song song, long from, long to)
    {
        var events = new List<SongEvent>();
        for (var tick = CeilingTo(from, Song.TicksPerBeat); tick < to; tick += Song.TicksPerBeat)
        {
            var downbeat = Mod(tick, song.TicksPerBar) == 0;
            events.Add(new SongEvent(tick, SongEventKind.Click, 0, downbeat ? 1 : 0));
        }

        return events;
    }

    /// <summary>Stable, so that events at the same tick keep their kinds' order: offs before ons.</summary>
    public static List<SongEvent> Sorted(IEnumerable<SongEvent> events) =>
        [.. events.OrderBy(e => e.Tick).ThenBy(e => e.Kind)];

    /// <summary>Whether another note on the same step is sounding when this one ends, and goes on past it.</summary>
    private static bool Overlapped(List<Note> notes, Note note)
    {
        var seen = false;
        foreach (var other in notes)
        {
            if (other == note && !seen)
            {
                seen = true;
                continue;
            }

            if (other.Step == note.Step && other.Start < note.End && other.End > note.End)
            {
                return true;
            }
        }

        return false;
    }

    private static long CeilingTo(long value, long multiple) => value + Mod(-value, multiple);

    private static long Mod(long value, long modulus) => ((value % modulus) + modulus) % modulus;
}
