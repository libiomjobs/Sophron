namespace Sophron.Songs;

/// <summary>
/// What the piano roll does to notes, kept apart from the drawing so it can be tested. Every edit
/// returns a new list, leaving the one it was given alone: the transport may be reading the old list
/// on its own thread, and it must never see one half changed. The notes chosen are given by their
/// places in the list, which edits other than adding and deleting keep.
/// </summary>
public static class NoteEditing
{
    /// <summary>No edit makes a note shorter than this.</summary>
    public const long ShortestNote = Recorder.ShortestNote;

    /// <summary>The nearest point of the grid; a grid of 1 or less leaves the tick as it is.</summary>
    public static long Snap(long tick, long grid) =>
        grid <= 1 ? tick : (long)Math.Round((double)tick / grid, MidpointRounding.AwayFromZero) * grid;

    /// <summary>The point of the grid at or before the tick.</summary>
    public static long SnapDown(long tick, long grid) =>
        grid <= 1 ? tick : (long)Math.Floor((double)tick / grid) * grid;

    /// <summary>
    /// Moves the chosen notes together, keeping their lengths. The whole group stops at the start of
    /// the song and at the ends of the keyboard, so its shape is kept even when pushed against them.
    /// </summary>
    public static List<Note> Move(IReadOnlyList<Note> notes, IReadOnlyCollection<int> chosen, long ticks, int steps, int lowestStep, int highestStep)
    {
        if (chosen.Count == 0)
        {
            return [.. notes];
        }

        var group = chosen.Select(index => notes[index]).ToList();
        ticks = Math.Max(ticks, -group.Min(note => note.Start));
        steps = Math.Clamp(steps, lowestStep - group.Min(note => note.Step), highestStep - group.Max(note => note.Step));
        return Changed(notes, chosen, note => note with { Start = note.Start + ticks, Step = note.Step + steps });
    }

    /// <summary>Moves the ends of the chosen notes, none of them shorter than <see cref="ShortestNote"/>.</summary>
    public static List<Note> ResizeEnd(IReadOnlyList<Note> notes, IReadOnlyCollection<int> chosen, long ticks) =>
        Changed(notes, chosen, note => note with { Length = Math.Max(ShortestNote, note.Length + ticks) });

    /// <summary>
    /// Moves the starts of the chosen notes, leaving their ends where they are: a note drawn out to the
    /// left starts earlier, as far back as the start of the song.
    /// </summary>
    public static List<Note> ResizeStart(IReadOnlyList<Note> notes, IReadOnlyCollection<int> chosen, long ticks) =>
        Changed(notes, chosen, note =>
        {
            var start = Math.Clamp(note.Start + ticks, 0, note.End - ShortestNote);
            return note with { Start = start, Length = note.End - start };
        });

    /// <summary>
    /// Pulls the starts and ends of the chosen notes onto the grid. A note so short that both would
    /// land on the same point keeps one step of the grid.
    /// </summary>
    public static List<Note> Quantise(IReadOnlyList<Note> notes, IReadOnlyCollection<int> chosen, long grid) =>
        grid <= 1 ? [.. notes] : Changed(notes, chosen, note =>
        {
            var start = Math.Max(0, Snap(note.Start, grid));
            var end = Math.Max(start + grid, Snap(note.End, grid));
            return note with { Start = start, Length = end - start };
        });

    public static List<Note> SetVelocity(IReadOnlyList<Note> notes, IReadOnlyCollection<int> chosen, float velocity) =>
        Changed(notes, chosen, note => note with { Velocity = Math.Clamp(velocity, 0, 1) });

    /// <summary>
    /// Gives each of the chosen notes the velocity <paramref name="shape"/> has at its start, leaving
    /// alone any note where the shape has none.
    /// </summary>
    public static List<Note> ShapeVelocity(IReadOnlyList<Note> notes, IReadOnlyCollection<int> chosen, Func<long, double?> shape) =>
        Changed(notes, chosen, note => shape(note.Start) is { } velocity ? note with { Velocity = (float)Math.Clamp(velocity, 0, 1) } : note);

    /// <summary>
    /// A straight line of velocity from one tick to another, as a stroke of the mouse paints it. Notes
    /// within <paramref name="reach"/> ticks beyond either end take that end's velocity, so a stroke
    /// that only touches a note's stem still catches it.
    /// </summary>
    public static List<Note> VelocityLine(IReadOnlyList<Note> notes, IReadOnlyCollection<int> chosen, long from, double fromVelocity, long to, double toVelocity, long reach)
    {
        if (to < from)
        {
            (from, fromVelocity, to, toVelocity) = (to, toVelocity, from, fromVelocity);
        }

        return ShapeVelocity(notes, chosen, tick =>
        {
            if (tick < from - reach || tick > to + reach)
            {
                return null;
            }

            if (to == from)
            {
                return toVelocity;
            }

            var along = Math.Clamp((double)(tick - from) / (to - from), 0, 1);
            return fromVelocity + ((toVelocity - fromVelocity) * along);
        });
    }

    public static List<Note> Delete(IReadOnlyList<Note> notes, IReadOnlyCollection<int> chosen)
    {
        var gone = chosen as ISet<int> ?? new HashSet<int>(chosen);
        return [.. notes.Where((_, index) => !gone.Contains(index))];
    }

    /// <summary>The chosen notes in time order, for copying; empty if none are chosen.</summary>
    public static List<Note> Copy(IReadOnlyList<Note> notes, IReadOnlyCollection<int> chosen) =>
        [.. chosen.Select(index => notes[index]).OrderBy(note => note.Start).ThenBy(note => note.Step)];

    /// <summary>
    /// Adds <paramref name="pasted"/> so that the earliest of them starts at <paramref name="at"/>,
    /// returning the new list and the places of the notes added, which come at its end.
    /// </summary>
    public static (List<Note> Notes, List<int> Added) Paste(IReadOnlyList<Note> notes, IReadOnlyList<Note> pasted, long at)
    {
        var result = new List<Note>(notes);
        var added = new List<int>();
        if (pasted.Count == 0)
        {
            return (result, added);
        }

        var shift = at - pasted.Min(note => note.Start);
        foreach (var note in pasted)
        {
            added.Add(result.Count);
            result.Add(note with { Start = note.Start + shift });
        }

        return (result, added);
    }

    /// <summary>
    /// Where a copy of the chosen notes goes when duplicated: straight after them, at the next point of
    /// the grid that leaves the copy clear of the original, as a repeat would be played.
    /// </summary>
    public static long DuplicateAt(IReadOnlyList<Note> notes, IReadOnlyCollection<int> chosen, long grid)
    {
        var group = chosen.Select(index => notes[index]).ToList();
        var first = group.Min(note => note.Start);
        var span = group.Max(note => note.End) - first;
        if (grid > 1)
        {
            span = (span + grid - 1) / grid * grid;
        }

        return first + Math.Max(span, ShortestNote);
    }

    /// <summary>The places of the notes that touch the rectangle between two ticks and two steps, both ends included.</summary>
    public static List<int> Within(IReadOnlyList<Note> notes, long from, long to, int lowestStep, int highestStep)
    {
        var found = new List<int>();
        for (var index = 0; index < notes.Count; index++)
        {
            var note = notes[index];
            if (note.Step >= lowestStep && note.Step <= highestStep && note.Start <= to && note.End >= from)
            {
                found.Add(index);
            }
        }

        return found;
    }

    private static List<Note> Changed(IReadOnlyList<Note> notes, IReadOnlyCollection<int> chosen, Func<Note, Note> change)
    {
        var result = new List<Note>(notes);
        foreach (var index in chosen)
        {
            result[index] = change(notes[index]);
        }

        return result;
    }
}
