namespace Sophron.Songs;

/// <summary>
/// Undo and redo for a track. Each edit is remembered as the track was before it, whole: a track is
/// small beside the memory of any machine that can run Sophron, and a snapshot cannot go wrong the way
/// an edit undone by hand can. The lists are never changed in place, only replaced, so a snapshot can
/// share them with the track.
/// </summary>
public sealed class EditHistory
{
    /// <summary>How many edits can be undone; the oldest are forgotten after this.</summary>
    public const int Depth = 500;

    private readonly LinkedList<Entry> _undo = new();
    private readonly Stack<Entry> _redo = new();

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>What undoing would undo, as the edit was named; null if nothing.</summary>
    public string? UndoName => _undo.Last?.Value.Name;

    /// <summary>What redoing would redo; null if nothing.</summary>
    public string? RedoName => _redo.TryPeek(out var entry) ? entry.Name : null;

    /// <summary>Remembers the track as it is now, before an edit named <paramref name="name"/> changes it. Anything undone can no longer be redone.</summary>
    public void Before(Track track, string name)
    {
        _undo.AddLast(new Entry(name, Snapshot.Of(track)));
        if (_undo.Count > Depth)
        {
            _undo.RemoveFirst();
        }

        _redo.Clear();
    }

    /// <summary>Puts the track back as it was before the last edit, returning that edit's name, or null if there was none.</summary>
    public string? Undo(Track track)
    {
        if (_undo.Last is not { } last)
        {
            return null;
        }

        _undo.RemoveLast();
        _redo.Push(last.Value with { State = Snapshot.Of(track) });
        last.Value.State.Into(track);
        return last.Value.Name;
    }

    /// <summary>Makes the last edit undone again, returning its name, or null if there was none.</summary>
    public string? Redo(Track track)
    {
        if (!_redo.TryPop(out var entry))
        {
            return null;
        }

        _undo.AddLast(entry with { State = Snapshot.Of(track) });
        entry.State.Into(track);
        return entry.Name;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    private readonly record struct Entry(string Name, Snapshot State);

    private readonly record struct Snapshot(List<Note> Notes, List<BendPoint> Bends, List<PedalChange> Sustain)
    {
        public static Snapshot Of(Track track) => new(track.Notes, track.Bends, track.Sustain);

        public void Into(Track track) => (track.Notes, track.Bends, track.Sustain) = (Notes, Bends, Sustain);
    }
}
