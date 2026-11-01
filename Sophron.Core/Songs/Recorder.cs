namespace Sophron.Songs;

/// <summary>
/// Gathers what is played while recording into notes, bends and pedal changes, and adds them to a
/// track when recording stops. Times are ticks; anything played in the count-in, before tick 0, is
/// moved up to 0, since a note played a hair early for the first beat was meant for it.
/// </summary>
public sealed class Recorder
{
    /// <summary>The shortest note kept, so a key tapped and let go at once still leaves something to see.</summary>
    public const long ShortestNote = Song.TicksPerBeat / 64;

    private readonly Dictionary<int, (long Start, float Velocity)> _held = [];
    private readonly List<Note> _notes = [];
    private readonly List<BendPoint> _bends = [];
    private readonly List<PedalChange> _pedal = [];

    /// <summary>A step struck again while still held ends the first note there.</summary>
    public void NoteDown(int step, float velocity, long tick)
    {
        NoteUp(step, tick);
        _held[step] = (tick, velocity);
    }

    public void NoteUp(int step, long tick)
    {
        if (_held.Remove(step, out var held))
        {
            Close(step, held, tick);
        }
    }

    public void Bend(double cents, long tick) => _bends.Add(new BendPoint(Math.Max(0, tick), cents));

    public void Sustain(bool down, long tick) => _pedal.Add(new PedalChange(Math.Max(0, tick), down));

    /// <summary>The notes so far, those still held drawn out to <paramref name="now"/>, for showing as they are played.</summary>
    public List<Note> NotesSoFar(long now)
    {
        var notes = new List<Note>(_notes);
        foreach (var (step, held) in _held)
        {
            var start = Math.Max(0, held.Start);
            notes.Add(new Note(start, Math.Max(ShortestNote, now - start), step, held.Velocity));
        }

        return notes;
    }

    /// <summary>Lets go of every held note at <paramref name="tick"/> and adds everything to <paramref name="track"/>.</summary>
    public void FinishInto(Track track, long tick)
    {
        foreach (var (step, held) in _held)
        {
            Close(step, held, tick);
        }

        _held.Clear();
        // The track's lists are replaced, never changed in place: the transport and the edit history
        // may both be holding the old ones.
        track.Notes = [.. track.Notes.Concat(_notes).OrderBy(note => note.Start).ThenBy(note => note.Step)];
        track.Bends = [.. track.Bends.Concat(_bends).OrderBy(bend => bend.Time)];
        track.Sustain = [.. track.Sustain.Concat(_pedal).OrderBy(pedal => pedal.Time)];
        _notes.Clear();
        _bends.Clear();
        _pedal.Clear();
    }

    /// <summary>A note let go before tick 0 was only part of the count-in, and is dropped.</summary>
    private void Close(int step, (long Start, float Velocity) held, long end)
    {
        if (end <= 0)
        {
            return;
        }

        var start = Math.Max(0, held.Start);
        _notes.Add(new Note(start, Math.Max(ShortestNote, end - start), step, held.Velocity));
    }
}
