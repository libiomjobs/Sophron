using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Sophron.Songs;
using Sophron.Tuning;

namespace Sophron.Controls;

/// <summary>How the velocity lane is drawn in.</summary>
public enum LaneTool
{
    /// <summary>The mouse paints velocities as it goes, each note taking the height of the stroke over it.</summary>
    Freehand,

    /// <summary>Clicks place the points of a smooth curve, which the notes under it follow once applied.</summary>
    Curve,
}

public sealed class TickEventArgs(long tick) : EventArgs
{
    public long Tick { get; } = tick;
}

/// <summary>Notes as an edit left them, and what to call the edit in the undo list.</summary>
public sealed class NotesEditedEventArgs(List<Note> notes, string name) : EventArgs
{
    public List<Note> Notes { get; } = notes;

    public string Name { get; } = name;
}

/// <summary>
/// A song's notes laid out with time running right and steps running up, one row to a step. The rows
/// are shaded as the keys are, and a gutter on the left shows the keys themselves, so a note's row is
/// as easy to read as a key on the keyboard below. The roll scrolls itself, inside a
/// <see cref="ScrollViewer"/>, so that the gutter and the bar numbers along the top stay where they are.
/// <para>
/// Notes are edited here, but the roll does not own them: each edit is offered through
/// <see cref="NotesEdited"/> as a new list, for the window to put in the song and remember for undoing.
/// While a note is dragged the roll shows where it would go, and offers the edit only when it is let go.
/// </para>
/// </summary>
public sealed class PianoRoll : FrameworkElement, IScrollInfo
{
    public const double LeastPixelsPerBeat = 6;
    public const double MostPixelsPerBeat = 600;
    public const double LeastRowHeight = 3;
    public const double MostRowHeight = 24;

    private const double Gutter = 44;
    private const double Header = 20;

    /// <summary>The roll always runs this far past the last note, so there is room to record more.</summary>
    private const int SpareBars = 8;
    private const int LeastBars = 32;

    /// <summary>How far, in pixels, the mouse must go before a press on a note becomes a drag.</summary>
    private const double DragThreshold = 3;

    /// <summary>How near, in pixels, to the dot at either end of a chosen note the mouse must be to take hold of it.</summary>
    private const double HandleReach = 6;
    private const double HandleRadius = 3.5;

    /// <summary>Grid lines closer together than this are not drawn.</summary>
    private const double FinestGridLines = 6;

    public const double LeastLaneHeight = 40;
    public const double MostLaneHeight = 320;

    /// <summary>The bar between the rows and the velocity lane, which is dragged to make the lane taller or shorter.</summary>
    private const double Separator = 5;
    private const double LanePadding = 6;
    private const double ScrollBarRoom = 12;

    /// <summary>How near, in pixels, a stroke in the lane must pass a note's stem to catch it.</summary>
    private const double StemReach = 5;
    private const double CurvePointRadius = 4.5;

    /// <summary>The softest a velocity can be drawn: a velocity of 0 is no note at all.</summary>
    private const double SoftestVelocity = 1.0 / 127;

    private static readonly Brush Background = Frozen(new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)));
    private static readonly Brush WhiteRow = Frozen(new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x2E)));
    private static readonly Brush LightRow = Frozen(new SolidColorBrush(Color.FromRgb(0x27, 0x27, 0x27)));
    private static readonly Brush DarkRow = Frozen(new SolidColorBrush(Color.FromRgb(0x21, 0x21, 0x21)));
    private static readonly Pen OctavePen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50)), 1));
    private static readonly Pen GridPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B)), 1));
    private static readonly Pen BeatPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x38, 0x38, 0x38)), 1));
    private static readonly Pen BarPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x5C, 0x5C, 0x5C)), 1));
    private static readonly Pen KeyEdgePen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x26)), 0.5));
    private static readonly Color NoteColour = Color.FromRgb(0x3D, 0x8B, 0xE8);
    private static readonly Color ChosenColour = Color.FromRgb(0x7C, 0xB8, 0xFF);
    private static readonly Color RecordingColour = Color.FromRgb(0xE8, 0x4F, 0x3D);
    private static readonly Pen NoteEdgePen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xC8, 0xDD, 0xF7)), 1));
    private static readonly Pen ChosenEdgePen = Frozen(new Pen(Brushes.White, 1.5));
    private static readonly Brush HandleFill = Brushes.White;
    private static readonly Pen HandleEdgePen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x10, 0x30, 0x60)), 1));
    private static readonly Brush BandFill = Frozen(new SolidColorBrush(Color.FromArgb(0x30, 0x7C, 0xB8, 0xFF)));
    private static readonly Pen BandEdgePen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0xC0, 0x7C, 0xB8, 0xFF)), 1));
    private static readonly Pen PlayheadPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xD9, 0x6B, 0x1E)), 2));
    private static readonly Brush HeaderText = Frozen(new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)));
    private static readonly Brush KeyText = Frozen(new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30)));
    private static readonly Typeface LabelFace = new("Segoe UI");
    private static readonly Brush SeparatorBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)));
    private static readonly Brush LaneBackground = Frozen(new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)));
    private static readonly Pen LaneGuidePen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x2C)), 1));
    private static readonly Pen StemPen = Frozen(new Pen(new SolidColorBrush(NoteColour), 2));
    private static readonly Pen ChosenStemPen = Frozen(new Pen(new SolidColorBrush(ChosenColour), 2));
    private static readonly Pen OtherStemPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x70, 0x3D, 0x8B, 0xE8)), 2));
    private static readonly Pen RecordingStemPen = Frozen(new Pen(new SolidColorBrush(RecordingColour), 2));
    private static readonly Pen CurvePen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xF2, 0xB1, 0x3C)), 2));
    private static readonly Brush CurvePointFill = Frozen(new SolidColorBrush(Color.FromRgb(0xF2, 0xB1, 0x3C)));
    private static readonly Brush HintText = Frozen(new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90)));

    /// <summary>What was copied or cut, shared by every roll so it survives opening another song.</summary>
    private static List<Note> _clipboard = [];

    private Temperament _temperament = Temperament.TwentySeven();
    private IReadOnlyList<Note> _notes = [];
    private IReadOnlyList<Note>? _recording;
    private int _beatsPerBar = 4;
    private long _playhead;
    private double _pixelsPerBeat = 48;
    private double _rowHeight = 8;
    private long _grid = Song.TicksPerBeat / 4;
    private bool _editable = true;
    private bool _drawing;

    private readonly HashSet<int> _chosen = [];

    /// <summary>How long a drawn note is: as long as the last one drawn or stretched, so a run of them can be drawn alike.</summary>
    private long _drawLength = Song.TicksPerBeat;

    private Size _viewport;
    private Vector _offset;

    private Drag _drag;
    private bool _dragging;
    private Point _pressedAt;
    private double _fromTick;
    private int _fromStep;

    /// <summary>Where the press was down the whole roll, scrolled or not, for the corner of a rectangle.</summary>
    private double _fromY;
    private IReadOnlyList<Note> _before = [];
    private List<int> _dragged = [];
    private int _anchor = -1;
    private int _pressedNote = -1;
    private bool _pressedWasChosen;
    private HashSet<int> _chosenBefore = [];
    private int? _auditioning;

    private double _laneHeight = 96;
    private bool _laneShown = true;
    private LaneTool _laneTool = LaneTool.Freehand;
    private double _laneHeightBefore;

    // Where a freehand stroke in the lane last was, so the next stretch of it can be drawn as a line from there.
    private double _strokeTick;
    private double _strokeVelocity;

    /// <summary>
    /// The points of a curve being drawn in the lane, in the order placed. Until it is applied, the roll
    /// shows the notes following it while the song keeps them as they were (<see cref="_curveBase"/>),
    /// so a curve can be shaped point by point and becomes one edit to undo.
    /// </summary>
    private readonly List<(double Tick, double Velocity)> _curve = [];
    private IReadOnlyList<Note>? _curveBase;
    private List<int> _curveTargets = [];
    private int _curvePoint = -1;

    private enum Drag
    {
        None,
        Move,
        ResizeStart,
        ResizeEnd,
        Draw,
        Band,
        Audition,
        Velocity,
        CurvePoint,
        LaneResize,
    }

    /// <summary>A click along the bar numbers, asking for the song to be played from there.</summary>
    public event EventHandler<TickEventArgs>? PositionChosen;

    /// <summary>The notes have been edited; the list given is the roll's own from now on, and must not be changed.</summary>
    public event EventHandler<NotesEditedEventArgs>? NotesEdited;

    /// <summary>Notes have been chosen, or let go.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>A note has been pressed, drawn or dragged to a new step, and should be heard.</summary>
    public event EventHandler<StepEventArgs>? AuditionStarted;

    /// <summary>The note being heard should stop.</summary>
    public event EventHandler? AuditionStopped;

    public PianoRoll()
    {
        Focusable = true;
        FocusVisualStyle = null;
    }

    public Temperament Temperament
    {
        get => _temperament;
        set
        {
            _temperament = value;
            Changed();
        }
    }

    /// <summary>
    /// The notes shown. A list other than the one already shown lets go of the chosen notes, since
    /// their places in it may now mean other notes; <see cref="Update"/> keeps them.
    /// </summary>
    public IReadOnlyList<Note> Notes
    {
        get => _notes;
        set
        {
            // While a curve is being drawn the roll shows notes the song does not have yet; the song's
            // own, given back unchanged, leave the curve be.
            if (_curveBase is not null && ReferenceEquals(value, _curveBase))
            {
                Changed();
                return;
            }

            if (!ReferenceEquals(value, _notes))
            {
                CancelDrag();
                ForgetCurve();
                _notes = value;
                Choose([]);
            }

            Changed();
        }
    }

    /// <summary>Whether the velocity lane is shown under the rows.</summary>
    public bool LaneShown
    {
        get => _laneShown;
        set
        {
            if (!value)
            {
                CommitCurve();
            }

            _laneShown = value;
            Changed();
            SetVerticalOffset(VerticalOffset);
        }
    }

    public LaneTool LaneTool
    {
        get => _laneTool;
        set
        {
            if (value != _laneTool)
            {
                CommitCurve();
                _laneTool = value;
                Changed();
            }
        }
    }

    /// <summary>How tall the velocity lane is; dragging the bar above it changes it too.</summary>
    public double LaneHeight
    {
        get => _laneHeight;
        set
        {
            _laneHeight = Math.Clamp(value, LeastLaneHeight, Math.Max(LeastLaneHeight, Math.Min(MostLaneHeight, _viewport.Height - Header - 60 - Separator)));
            Changed();
            SetVerticalOffset(VerticalOffset);
        }
    }

    /// <summary>Whether a curve is drawn in the lane and not yet applied.</summary>
    public bool CurvePending => _curveBase is not null;

    /// <summary>The places in <see cref="Notes"/> of the notes chosen.</summary>
    public IReadOnlyCollection<int> Selected => _chosen;

    /// <summary>Notes being recorded, shown in their own colour until recording stops; null when not recording.</summary>
    public IReadOnlyList<Note>? Recording
    {
        get => _recording;
        set
        {
            _recording = value;
            Changed();
        }
    }

    /// <summary>Whether notes may be edited; not while recording, when the track is about to change under them.</summary>
    public bool Editable
    {
        get => _editable;
        set
        {
            if (!value)
            {
                CancelDrag();
                CommitCurve();
            }

            _editable = value;
        }
    }

    /// <summary>Whether a click on empty space draws a note, rather than starting a rectangle to choose notes with.</summary>
    public bool DrawMode
    {
        get => _drawing;
        set
        {
            _drawing = value;
            Cursor = null;
        }
    }

    /// <summary>The grid notes snap to, in ticks; 0 for none.</summary>
    public long Grid
    {
        get => _grid;
        set
        {
            _grid = Math.Max(0, value);
            Changed();
        }
    }

    public int BeatsPerBar
    {
        get => _beatsPerBar;
        set
        {
            _beatsPerBar = Math.Max(1, value);
            Changed();
        }
    }

    /// <summary>Where the listener is, in ticks.</summary>
    public long Playhead
    {
        get => _playhead;
        set
        {
            if (value != _playhead)
            {
                _playhead = value;
                Changed();
            }
        }
    }

    /// <summary>How wide a beat is drawn: the zoom along time.</summary>
    public double PixelsPerBeat => _pixelsPerBeat;

    /// <summary>How tall a step is drawn: the zoom along pitch.</summary>
    public double RowHeight => _rowHeight;

    public bool CanVerticallyScroll { get; set; }

    public bool CanHorizontallyScroll { get; set; }

    public double ExtentWidth => Gutter + (Bars * _beatsPerBar * _pixelsPerBeat);

    public double ExtentHeight => Header + (Rows * _rowHeight) + LaneSpace;

    public double ViewportWidth => _viewport.Width;

    public double ViewportHeight => _viewport.Height;

    public double HorizontalOffset => _offset.X;

    public double VerticalOffset => _offset.Y;

    public ScrollViewer? ScrollOwner { get; set; }

    private int LastStep => TemperedKeyboard.LastStepOf(_temperament);

    private int Rows => LastStep + 1;

    private double PixelsPerTick => _pixelsPerBeat / Song.TicksPerBeat;

    private long TicksPerBar => (long)_beatsPerBar * Song.TicksPerBeat;

    /// <summary>How far an arrow key moves a note along time: a step of the grid, or a sixteenth with none.</summary>
    private long Nudge => _grid > 1 ? _grid : Song.TicksPerBeat / 4;

    private int Bars
    {
        get
        {
            var end = Math.Max(_playhead, _notes.Count == 0 ? 0 : _notes.Max(note => note.End));
            if (_recording is { Count: > 0 })
            {
                end = Math.Max(end, _recording.Max(note => note.End));
            }

            return Math.Max(LeastBars, (int)(end / TicksPerBar) + SpareBars);
        }
    }

    /// <summary>Shows notes changed by the window, such as a new velocity, keeping the chosen ones chosen; the list must be as long as before.</summary>
    public void Update(IReadOnlyList<Note> notes)
    {
        if (notes.Count != _notes.Count)
        {
            Notes = notes;
            return;
        }

        ForgetCurve();
        _notes = notes;
        Changed();
    }

    /// <summary>Applies the curve drawn in the lane, as one edit; nothing happens if none is drawn.</summary>
    public void CommitCurve()
    {
        if (_curveBase is not { } before)
        {
            return;
        }

        var shown = _notes;
        _notes = before;
        ForgetCurve();
        if (shown.SequenceEqual(before))
        {
            Changed();
            return;
        }

        Edit([.. shown], "Velocity curve");
    }

    /// <summary>Throws the curve drawn in the lane away, putting the notes back as they were.</summary>
    public void DiscardCurve()
    {
        if (_curveBase is { } before)
        {
            _notes = before;
            ForgetCurve();
            Changed();
        }
    }

    /// <summary>Scrolls so that a step's row is in the middle, as when a song is opened.</summary>
    public void CentreOnStep(int step) =>
        SetVerticalOffset(RowTop(step) - Header - (NotesHeight / 2));

    /// <summary>While playing, turns the page whenever the playhead runs off the right of what is shown.</summary>
    public void KeepPlayheadInView()
    {
        var x = _playhead * PixelsPerTick;
        var shown = ViewportWidth - Gutter;
        if (x < HorizontalOffset || x > HorizontalOffset + shown - _pixelsPerBeat)
        {
            SetHorizontalOffset(x - _pixelsPerBeat);
        }
    }

    /// <summary>Widens or narrows time by <paramref name="factor"/>, keeping still whatever is at <paramref name="x"/>, or the middle if none is given.</summary>
    public void ZoomTime(double factor, double? x = null)
    {
        var at = x ?? (Gutter + ((ViewportWidth - Gutter) / 2));
        var tick = TickAt(at);
        _pixelsPerBeat = Math.Clamp(_pixelsPerBeat * factor, LeastPixelsPerBeat, MostPixelsPerBeat);
        Changed();
        SetHorizontalOffset((tick * PixelsPerTick) - (at - Gutter));
    }

    /// <summary>Makes rows taller or shorter by <paramref name="factor"/>, keeping still the step at <paramref name="y"/>, or the middle if none is given.</summary>
    public void ZoomSteps(double factor, double? y = null)
    {
        var at = y ?? (Header + (NotesHeight / 2));
        var rows = (at - Header + VerticalOffset) / _rowHeight;
        _rowHeight = Math.Clamp(Math.Round(_rowHeight * factor), LeastRowHeight, MostRowHeight);
        Changed();
        SetVerticalOffset((rows * _rowHeight) - (at - Header));
    }

    /// <summary>
    /// Answers a key meant for the notes, returning whether it was one. The window passes keys on,
    /// rather than the roll waiting for focus, so the keys work after a click on a button too.
    /// </summary>
    public bool HandleKey(Key key, ModifierKeys modifiers)
    {
        if (!_editable || _dragging)
        {
            return false;
        }

        var control = modifiers.HasFlag(ModifierKeys.Control);
        var shift = modifiers.HasFlag(ModifierKeys.Shift);
        if (_curveBase is not null)
        {
            switch (key)
            {
                case Key.Enter:
                    CommitCurve();
                    return true;
                case Key.Escape:
                    DiscardCurve();
                    return true;
                case Key.Delete or Key.Back or Key.Left or Key.Right or Key.Up or Key.Down or Key.Q:
                case Key.A or Key.C or Key.X or Key.V or Key.D when control:
                    // Any other edit applies the curve first, so the two do not fight over the notes.
                    CommitCurve();
                    break;
            }
        }

        var any = _chosen.Count > 0;
        switch (key)
        {
            case Key.A when control:
                Choose(Enumerable.Range(0, _notes.Count));
                return true;
            case Key.Escape when any:
                Choose([]);
                return true;
            case Key.Delete or Key.Back when any:
                DeleteChosen("Delete");
                return true;
            case Key.C when control && any:
                _clipboard = NoteEditing.Copy(_notes, _chosen);
                return true;
            case Key.X when control && any:
                _clipboard = NoteEditing.Copy(_notes, _chosen);
                DeleteChosen("Cut");
                return true;
            case Key.V when control && _clipboard.Count > 0:
                PasteAt(_playhead, _clipboard, "Paste");
                PositionChosen?.Invoke(this, new TickEventArgs(NoteEditing.DuplicateAt(_notes, _chosen, _grid)));
                return true;
            case Key.D when control && any:
                PasteAt(NoteEditing.DuplicateAt(_notes, _chosen, _grid), NoteEditing.Copy(_notes, _chosen), "Duplicate");
                return true;
            case Key.Q when !control:
                Quantise();
                return true;
            case Key.Left or Key.Right when any:
                var ticks = key == Key.Left ? -Nudge : Nudge;
                Edit(shift ? NoteEditing.ResizeEnd(_notes, _chosen, ticks) : NoteEditing.Move(_notes, _chosen, ticks, 0, 0, LastStep), shift ? "Resize" : "Move");
                return true;
            case Key.Up or Key.Down when any:
                var steps = (key == Key.Up ? 1 : -1) * (shift ? _temperament.StepsPerOctave : 1);
                Edit(NoteEditing.Move(_notes, _chosen, 0, steps, 0, LastStep), "Transpose");
                AuditionFirstChosen();
                return true;
            default:
                return false;
        }
    }

    /// <summary>Pulls the chosen notes onto the grid, or every note if none is chosen.</summary>
    public void Quantise()
    {
        CommitCurve();
        if (!_editable || _grid <= 1 || _notes.Count == 0)
        {
            return;
        }

        var which = _chosen.Count > 0 ? (IReadOnlyCollection<int>)_chosen : [.. Enumerable.Range(0, _notes.Count)];
        Edit(NoteEditing.Quantise(_notes, which, _grid), "Quantise");
    }

    public void LineUp() => SetVerticalOffset(VerticalOffset - (3 * _rowHeight));

    public void LineDown() => SetVerticalOffset(VerticalOffset + (3 * _rowHeight));

    public void LineLeft() => SetHorizontalOffset(HorizontalOffset - _pixelsPerBeat);

    public void LineRight() => SetHorizontalOffset(HorizontalOffset + _pixelsPerBeat);

    public void PageUp() => SetVerticalOffset(VerticalOffset - NotesHeight);

    public void PageDown() => SetVerticalOffset(VerticalOffset + NotesHeight);

    public void PageLeft() => SetHorizontalOffset(HorizontalOffset - ViewportWidth + Gutter);

    public void PageRight() => SetHorizontalOffset(HorizontalOffset + ViewportWidth - Gutter);

    public void MouseWheelUp() => SetVerticalOffset(VerticalOffset - (6 * _rowHeight));

    public void MouseWheelDown() => SetVerticalOffset(VerticalOffset + (6 * _rowHeight));

    public void MouseWheelLeft() => SetHorizontalOffset(HorizontalOffset - (2 * _pixelsPerBeat));

    public void MouseWheelRight() => SetHorizontalOffset(HorizontalOffset + (2 * _pixelsPerBeat));

    public void SetHorizontalOffset(double offset)
    {
        var x = Math.Clamp(offset, 0, Math.Max(0, ExtentWidth - ViewportWidth));
        if (x != _offset.X)
        {
            _offset.X = x;
            Changed();
        }
    }

    public void SetVerticalOffset(double offset)
    {
        var y = Math.Clamp(offset, 0, Math.Max(0, ExtentHeight - ViewportHeight));
        if (y != _offset.Y)
        {
            _offset.Y = y;
            Changed();
        }
    }

    public Rect MakeVisible(Visual visual, Rect rectangle) => rectangle;

    protected override Size MeasureOverride(Size availableSize) => new(
        double.IsInfinity(availableSize.Width) ? 800 : availableSize.Width,
        double.IsInfinity(availableSize.Height) ? 400 : availableSize.Height);

    protected override Size ArrangeOverride(Size finalSize)
    {
        _viewport = finalSize;
        SetHorizontalOffset(HorizontalOffset);
        SetVerticalOffset(VerticalOffset);
        ScrollOwner?.InvalidateScrollInfo();
        return finalSize;
    }

    /// <summary>
    /// Shift turns the wheel sideways, as it does in most programs that scroll both ways. Ctrl zooms
    /// time about the mouse, and Ctrl with Shift zooms the steps.
    /// </summary>
    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);
        var modifiers = Keyboard.Modifiers;
        var notches = e.Delta / 120.0;
        var point = e.GetPosition(this);
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            if (modifiers.HasFlag(ModifierKeys.Shift))
            {
                ZoomSteps(Math.Pow(1.25, notches), point.Y);
            }
            else
            {
                ZoomTime(Math.Pow(1.25, notches), point.X);
            }

            e.Handled = true;
        }
        else if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            SetHorizontalOffset(HorizontalOffset - (notches * 2 * _pixelsPerBeat));
            e.Handled = true;
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        var point = e.GetPosition(this);
        if (point.Y < Header)
        {
            if (point.X > Gutter)
            {
                var beat = Song.TicksPerBeat;
                var tick = Math.Max(0, (long)TickAt(point.X));
                PositionChosen?.Invoke(this, new TickEventArgs((tick + (beat / 2)) / beat * beat));
            }

            e.Handled = true;
            return;
        }

        if (InLane(point))
        {
            LaneDown(point);
            e.Handled = true;
            return;
        }

        if (point.X < Gutter)
        {
            Begin(Drag.Audition, point);
            Audition(StepAt(point.Y));
            e.Handled = true;
            return;
        }

        if (!_editable)
        {
            return;
        }

        // Working on the notes applies a curve left in the lane, rather than leaving it to fight them.
        CommitCurve();
        var modifiers = Keyboard.Modifiers;
        var (hit, index) = HitTest(point);
        switch (hit)
        {
            case Hit.Start or Hit.End:
                Begin(hit == Hit.Start ? Drag.ResizeStart : Drag.ResizeEnd, point);
                _anchor = index;
                _dragged = [.. _chosen];
                break;

            case Hit.Note:
                var wasChosen = _chosen.Contains(index);
                if (modifiers.HasFlag(ModifierKeys.Shift))
                {
                    Choose(_chosen.Append(index));
                }
                else if (!_chosen.Contains(index) && !modifiers.HasFlag(ModifierKeys.Control))
                {
                    Choose([index]);
                }

                Begin(Drag.Move, point);
                _anchor = index;
                _pressedNote = index;
                _pressedWasChosen = wasChosen;
                _dragged = [.. _chosen.Append(index).Distinct()];
                Audition(_notes[index].Step);
                break;

            default:
                if (_drawing || e.ClickCount == 2)
                {
                    DrawNoteAt(point);
                }
                else
                {
                    Begin(Drag.Band, point);
                    _chosenBefore = modifiers.HasFlag(ModifierKeys.Shift) || modifiers.HasFlag(ModifierKeys.Control) ? [.. _chosen] : [];
                    Choose(_chosenBefore);
                }

                break;
        }

        e.Handled = true;
    }

    /// <summary>In draw mode a right click rubs out the note under the mouse.</summary>
    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        var point = e.GetPosition(this);
        if (!_editable || _dragging)
        {
            return;
        }

        // In the lane a right click takes away the curve point under the mouse.
        if (InLane(point))
        {
            if (CurvePointNear(point) is var near and >= 0)
            {
                _curve.RemoveAt(near);
                ApplyCurve();
            }

            e.Handled = true;
            return;
        }

        if (!_drawing)
        {
            return;
        }

        var (hit, index) = HitTest(point);
        if (hit != Hit.None)
        {
            Choose([index]);
            DeleteChosen("Delete");
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var point = e.GetPosition(this);
        if (!_dragging)
        {
            Cursor = CursorFor(point);
            return;
        }

        if (_drag is Drag.Move or Drag.ResizeStart or Drag.ResizeEnd or Drag.Band && (point - _pressedAt).Length < DragThreshold && ReferenceEquals(_notes, _before))
        {
            return;
        }

        if (_drag == Drag.LaneResize)
        {
            LaneHeight = _laneHeightBefore + (_pressedAt.Y - point.Y);
            return;
        }

        ScrollTowards(point);
        var snap = !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        var ticks = TickAt(point.X) - _fromTick;
        switch (_drag)
        {
            case Drag.Audition:
                Audition(StepAt(point.Y));
                break;
            case Drag.Move:
                DragMove(ticks, StepAt(point.Y) - _fromStep, snap, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
                break;
            case Drag.ResizeEnd:
            {
                var anchor = _before[_anchor];
                var end = Snapped(anchor.End + (long)ticks, snap);
                if (end <= anchor.Start)
                {
                    end = anchor.Start + (snap && _grid > 1 ? _grid : NoteEditing.ShortestNote);
                }

                Show(NoteEditing.ResizeEnd(_before, _dragged, end - anchor.End));
                break;
            }

            case Drag.ResizeStart:
            {
                var anchor = _before[_anchor];
                var start = Snapped(anchor.Start + (long)ticks, snap);
                Show(NoteEditing.ResizeStart(_before, _dragged, start - anchor.Start));
                break;
            }

            case Drag.Draw:
            {
                // The note being drawn is not among the notes as they were, only among those shown.
                var drawn = _notes[_anchor];
                var end = Snapped((long)TickAt(point.X), snap);
                var least = snap && _grid > 1 ? _grid : NoteEditing.ShortestNote;
                Show(NoteEditing.ResizeEnd(_notes, [_anchor], Math.Max(least, end - drawn.Start) - drawn.Length));
                break;
            }

            case Drag.Velocity:
            {
                var tick = TickAt(point.X);
                var velocity = VelocityAt(point.Y);
                Stroke(tick, velocity);
                break;
            }

            case Drag.CurvePoint:
                _curve[_curvePoint] = (Math.Max(0, TickAt(point.X)), VelocityAt(point.Y));
                ApplyCurve();
                break;

            case Drag.Band:
            {
                var (from, to) = (Math.Min(_fromTick, TickAt(point.X)), Math.Max(_fromTick, TickAt(point.X)));
                var (low, high) = (Math.Min(_fromStep, StepAt(point.Y)), Math.Max(_fromStep, StepAt(point.Y)));
                Choose(_chosenBefore.Union(NoteEditing.Within(_notes, (long)from, (long)to, low, high)));
                Changed();
                break;
            }
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging)
        {
            return;
        }

        var drag = _drag;
        var shown = _notes;
        var moved = !ReferenceEquals(shown, _before) && !shown.SequenceEqual(_before);
        var pressed = _pressedNote;
        EndDrag();

        // A drag that came back to where it started, copies and all, changes nothing.
        if (!moved && drag is Drag.Move or Drag.ResizeStart or Drag.ResizeEnd or Drag.Velocity)
        {
            _notes = _before;
            Choose(_dragged.Count > 0 && shown.Count > _before.Count ? _dragged : _chosen.Where(index => index < _before.Count));
        }

        switch (drag)
        {
            case Drag.Move when !moved && pressed >= 0:
                // A click on a chosen note, without dragging it, chooses that note alone; with Ctrl, it
                // is let go of or taken up, leaving the others as they were.
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    Choose(_pressedWasChosen ? _chosen.Where(index => index != pressed) : _chosen.Append(pressed));
                }
                else if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    Choose([pressed]);
                }

                break;
            case Drag.Move or Drag.ResizeStart or Drag.ResizeEnd when moved:
                _notes = _before;
                Edit((List<Note>)shown, drag switch
                {
                    Drag.Move when shown.Count > _before.Count => "Copy",
                    Drag.Move => "Move",
                    _ => "Resize",
                }, keepChosen: true);
                if (drag != Drag.Move)
                {
                    _drawLength = shown[_anchor].Length;
                }

                break;
            case Drag.Draw:
                _notes = _before;
                _drawLength = shown[_anchor].Length;
                Edit((List<Note>)shown, "Draw", keepChosen: true);
                break;
            case Drag.Velocity when moved:
                _notes = _before;
                Edit((List<Note>)shown, "Velocity");
                break;
        }

        Changed();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_dragging)
        {
            CancelDrag();
        }
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        var bottom = NotesBottom;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        drawingContext.DrawRectangle(Background, null, new Rect(0, 0, width, height));

        var (lowest, highest) = VisibleSteps(bottom);
        var right = Gutter + (Bars * _beatsPerBar * _pixelsPerBeat) - HorizontalOffset;

        // The rows and lines, clipped to the part right of the gutter and below the bar numbers.
        drawingContext.PushClip(new RectangleGeometry(new Rect(Gutter, Header, Math.Max(0, width - Gutter), Math.Max(0, bottom - Header))));
        for (var step = lowest; step <= highest; step++)
        {
            var top = RowTop(step) - VerticalOffset;
            drawingContext.DrawRectangle(RowBrush(step), null, new Rect(Gutter, top, Math.Max(0, right - Gutter), _rowHeight));
            if (_temperament.Degree(step) == 0)
            {
                drawingContext.DrawLine(OctavePen, new Point(Gutter, top + _rowHeight - 0.5), new Point(right, top + _rowHeight - 0.5));
            }
        }

        DrawGrid(drawingContext, width, bottom);

        var firstBeat = (long)Math.Floor(HorizontalOffset / _pixelsPerBeat);
        var lastBeat = (long)Math.Ceiling((HorizontalOffset + width - Gutter) / _pixelsPerBeat);
        var everyBeat = BeatsBetweenLines();
        for (var beat = firstBeat - (firstBeat % everyBeat); beat <= Math.Min(lastBeat, (long)Bars * _beatsPerBar); beat += everyBeat)
        {
            var x = Math.Round(Gutter + (beat * _pixelsPerBeat) - HorizontalOffset) + 0.5;
            drawingContext.DrawLine(beat % _beatsPerBar == 0 ? BarPen : BeatPen, new Point(x, Header), new Point(x, bottom));
        }

        DrawNotes(drawingContext, _notes, NoteColour, lowest, highest, width, _chosen);
        if (_recording is not null)
        {
            DrawNotes(drawingContext, _recording, RecordingColour, lowest, highest, width, null);
        }

        if (_dragging && _drag == Drag.Band)
        {
            var corner = new Point(XAt(_fromTick), _fromY - VerticalOffset);
            drawingContext.DrawRectangle(BandFill, BandEdgePen, new Rect(corner, Mouse.GetPosition(this)));
        }

        var playheadX = Gutter + (_playhead * PixelsPerTick) - HorizontalOffset;
        drawingContext.DrawLine(PlayheadPen, new Point(playheadX, Header), new Point(playheadX, bottom));
        drawingContext.Pop();

        DrawGutter(drawingContext, lowest, highest, bottom, dpi);
        DrawLane(drawingContext, width, firstBeat, lastBeat, everyBeat, dpi);
        DrawHeader(drawingContext, firstBeat, lastBeat, width, dpi);
        drawingContext.DrawRectangle(Background, null, new Rect(0, 0, Gutter, Header));
    }

    private enum Hit
    {
        None,
        Note,
        Start,
        End,
    }

    /// <summary>
    /// What is under the mouse: first the dots at the ends of the chosen notes, which may stand out past
    /// a note too short to hold them, then the notes themselves, the last drawn first since it lies on top.
    /// </summary>
    private (Hit Hit, int Index) HitTest(Point point)
    {
        if (point.X < Gutter || point.Y < Header || point.Y > NotesBottom)
        {
            return (Hit.None, -1);
        }

        foreach (var index in _chosen)
        {
            var rect = NoteRect(_notes[index]);
            var middle = rect.Top + (rect.Height / 2);
            if (Math.Abs(point.Y - middle) > Math.Max(HandleReach, rect.Height / 2))
            {
                continue;
            }

            // On a short note the dots crowd together, so the nearer end wins.
            var toStart = Math.Abs(point.X - rect.Left);
            var toEnd = Math.Abs(point.X - rect.Right);
            if (Math.Min(toStart, toEnd) <= HandleReach)
            {
                return (toEnd <= toStart ? Hit.End : Hit.Start, index);
            }
        }

        for (var index = _notes.Count - 1; index >= 0; index--)
        {
            if (NoteRect(_notes[index]).Contains(point))
            {
                return (Hit.Note, index);
            }
        }

        return (Hit.None, -1);
    }

    private Cursor? CursorFor(Point point)
    {
        if (InLane(point))
        {
            if (point.Y < LaneTop)
            {
                return Cursors.SizeNS;
            }

            if (!_editable || point.X < Gutter)
            {
                return null;
            }

            return _laneTool == LaneTool.Curve ? CurvePointNear(point) >= 0 ? Cursors.Hand : Cursors.Cross : Cursors.Pen;
        }

        if (point.Y < Header)
        {
            return point.X > Gutter ? Cursors.Hand : null;
        }

        if (point.X < Gutter)
        {
            return Cursors.Hand;
        }

        if (!_editable)
        {
            return null;
        }

        return HitTest(point).Hit switch
        {
            Hit.Start or Hit.End => Cursors.SizeWE,
            Hit.Note => Cursors.SizeAll,
            _ => _drawing ? Cursors.Pen : null,
        };
    }

    private void Begin(Drag drag, Point point)
    {
        _drag = drag;
        _dragging = true;
        _pressedAt = point;
        _fromTick = TickAt(point.X);
        _fromStep = StepAt(point.Y);
        _fromY = point.Y + VerticalOffset;
        _before = _notes;
        _anchor = -1;
        _pressedNote = -1;
        CaptureMouse();
    }

    private void EndDrag()
    {
        _dragging = false;
        _drag = Drag.None;
        StopAudition();
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }
    }

    /// <summary>Puts back what a drag had shown so far, as when the mouse is taken away mid-drag.</summary>
    private void CancelDrag()
    {
        if (!_dragging)
        {
            return;
        }

        var drag = _drag;
        EndDrag();
        if (drag is Drag.Move or Drag.ResizeStart or Drag.ResizeEnd or Drag.Draw)
        {
            _notes = _before;
            Choose(_chosen.Where(index => index < _notes.Count));
        }

        Changed();
    }

    /// <summary>
    /// Moves the notes held, snapping the one taken hold of to the grid. With Ctrl the notes are copied
    /// instead, the copies going where the notes are dragged and the notes staying where they were.
    /// </summary>
    private void DragMove(double ticks, int steps, bool snap, bool copy)
    {
        var anchor = _before[_anchor];
        var start = Snapped(anchor.Start + (long)Math.Round(ticks), snap);
        var moved = NoteEditing.Move(_before, _dragged, start - anchor.Start, steps, 0, LastStep);
        if (copy)
        {
            var notes = new List<Note>(_before);
            var added = new List<int>();
            foreach (var index in _dragged)
            {
                added.Add(notes.Count);
                notes.Add(moved[index]);
            }

            ShowChosen(notes, added);
        }
        else
        {
            Show(moved);
            Choose(_dragged);
        }

        if (moved[_anchor].Step != _auditioning)
        {
            Audition(moved[_anchor].Step);
        }
    }

    /// <summary>A note drawn where the mouse is, starting at the beginning of the grid square clicked in; dragging right then draws it out.</summary>
    private void DrawNoteAt(Point point)
    {
        var start = Math.Max(0, Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) ? (long)TickAt(point.X) : NoteEditing.SnapDown((long)TickAt(point.X), _grid));
        var step = Math.Clamp(StepAt(point.Y), 0, LastStep);
        Begin(Drag.Draw, point);
        var (notes, added) = NoteEditing.Paste(_before, [new Note(start, _drawLength, step, 0.8f)], start);
        _anchor = added[0];
        ShowChosen(notes, added);
        Audition(step);
    }

    private void Show(List<Note> notes)
    {
        _notes = notes;
        Changed();
    }

    private void ShowChosen(List<Note> notes, IEnumerable<int> chosen)
    {
        _notes = notes;
        Choose(chosen);
        Changed();
    }

    /// <summary>Makes an edit: the roll shows the new notes at once, and the window is told so it can keep them.</summary>
    private void Edit(List<Note> notes, string name, bool keepChosen = true)
    {
        _notes = notes;
        if (!keepChosen)
        {
            Choose([]);
        }

        Changed();
        NotesEdited?.Invoke(this, new NotesEditedEventArgs(notes, name));
    }

    private void DeleteChosen(string name)
    {
        var notes = NoteEditing.Delete(_notes, _chosen);
        Choose([]);
        Edit(notes, name);
    }

    private void PasteAt(long at, IReadOnlyList<Note> pasted, string name)
    {
        var (notes, added) = NoteEditing.Paste(_notes, pasted, Math.Max(0, at));
        _notes = notes;
        Choose(added);
        Edit(notes, name);
    }

    private void Choose(IEnumerable<int> chosen)
    {
        var next = chosen.Where(index => index >= 0 && index < _notes.Count).ToHashSet();
        if (next.SetEquals(_chosen))
        {
            return;
        }

        _chosen.Clear();
        _chosen.UnionWith(next);
        Changed();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Audition(int step)
    {
        if (step < 0 || step > LastStep || step == _auditioning)
        {
            return;
        }

        _auditioning = step;
        AuditionStarted?.Invoke(this, new StepEventArgs(step));
    }

    private void StopAudition()
    {
        if (_auditioning is not null)
        {
            _auditioning = null;
            AuditionStopped?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>A short blip of the first chosen note after transposing with the keys, so the new pitch can be heard.</summary>
    private void AuditionFirstChosen()
    {
        if (_chosen.Count == 0)
        {
            return;
        }

        Audition(_notes[_chosen.Min()].Step);
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!_dragging)
            {
                StopAudition();
            }
        };
        timer.Start();
    }

    private double LaneSpace => _laneShown ? _laneHeight + Separator : 0;

    /// <summary>Where the rows end and the lane, if shown, begins.</summary>
    private double NotesBottom => Math.Max(Header, _viewport.Height - LaneSpace);

    private double NotesHeight => NotesBottom - Header;

    private double LaneTop => NotesBottom + Separator;

    /// <summary>How far a stem rises from velocity 0 to 1.</summary>
    private double LaneSpan => Math.Max(1, LaneBottom - LaneTop - (2 * LanePadding));

    /// <summary>The lane stops short of the bottom, where the horizontal scroll bar floats over the roll.</summary>
    private double LaneBottom => _viewport.Height - ScrollBarRoom;

    /// <summary>The lane and the bar above it, which is where it is taken hold of to resize it.</summary>
    private bool InLane(Point point) => _laneShown && point.Y >= NotesBottom - 1;

    /// <summary>The velocity at a height in the lane, never quite silent.</summary>
    private double VelocityAt(double y) =>
        Math.Clamp((LaneBottom - LanePadding - y) / LaneSpan, SoftestVelocity, 1);

    private double YAtVelocity(double velocity) => LaneBottom - LanePadding - (velocity * LaneSpan);

    /// <summary>The notes the lane changes: the chosen ones if any are chosen, as when shaping one part of a chord, or else every note.</summary>
    private List<int> LaneTargets() => _chosen.Count > 0 ? [.. _chosen] : [.. Enumerable.Range(0, _notes.Count)];

    private void LaneDown(Point point)
    {
        if (point.Y < LaneTop)
        {
            Begin(Drag.LaneResize, point);
            _laneHeightBefore = _laneHeight;
            return;
        }

        if (!_editable || point.X < Gutter)
        {
            return;
        }

        if (_laneTool == LaneTool.Freehand)
        {
            CommitCurve();
            Begin(Drag.Velocity, point);
            _strokeTick = TickAt(point.X);
            _strokeVelocity = VelocityAt(point.Y);
            Stroke(_strokeTick, _strokeVelocity);
            return;
        }

        // A press away from every point adds one there, and goes on to drag it.
        var index = CurvePointNear(point);
        if (index < 0)
        {
            if (_curveBase is null)
            {
                _curveBase = _notes;
                _curveTargets = LaneTargets();
                _curve.Clear();
            }

            _curve.Add((Math.Max(0, TickAt(point.X)), VelocityAt(point.Y)));
            index = _curve.Count - 1;
        }

        Begin(Drag.CurvePoint, point);
        _curvePoint = index;
        ApplyCurve();
    }

    /// <summary>Paints the stretch of a freehand stroke from where it last was to here.</summary>
    private void Stroke(double tick, double velocity)
    {
        var reach = (long)Math.Ceiling(StemReach / PixelsPerTick);
        Show(NoteEditing.VelocityLine(_notes, LaneTargets(), (long)_strokeTick, _strokeVelocity, (long)tick, velocity, reach));
        (_strokeTick, _strokeVelocity) = (tick, velocity);
    }

    /// <summary>Shows the notes following the curve as it now stands.</summary>
    private void ApplyCurve()
    {
        if (_curveBase is not { } before)
        {
            return;
        }

        var spline = new Spline(_curve);
        _notes = NoteEditing.ShapeVelocity(before, _curveTargets, tick => spline.At(tick));
        Changed();
    }

    /// <summary>Lets go of the curve without touching the notes shown.</summary>
    private void ForgetCurve()
    {
        _curveBase = null;
        _curve.Clear();
        _curvePoint = -1;
    }

    /// <summary>The curve point under the mouse, or -1.</summary>
    private int CurvePointNear(Point point)
    {
        if (_curveBase is null)
        {
            return -1;
        }

        for (var index = _curve.Count - 1; index >= 0; index--)
        {
            var (tick, velocity) = _curve[index];
            if ((new Point(XAt(tick), YAtVelocity(velocity)) - point).Length <= CurvePointRadius + 3)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// The lane: a stem for each note at its start, as tall as the note is loud. With notes chosen, only
    /// they can be drawn on, so the others are shown faintly. A curve being drawn is shown over the stems
    /// with its points, and a line along the bottom says how to finish it.
    /// </summary>
    private void DrawLane(DrawingContext context, double width, long firstBeat, long lastBeat, long everyBeat, double dpi)
    {
        if (!_laneShown)
        {
            return;
        }

        var top = LaneTop;
        var bottom = LaneBottom;
        context.DrawRectangle(SeparatorBrush, null, new Rect(0, NotesBottom, width, Separator));
        context.DrawRectangle(LaneBackground, null, new Rect(0, top, width, Math.Max(0, bottom - top)));
        context.DrawRectangle(SeparatorBrush, null, new Rect(0, bottom, width, ScrollBarRoom));

        var label = new FormattedText("Velocity", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelFace, 9, HeaderText, dpi);
        context.DrawText(label, new Point(4, top + 3));

        context.PushClip(new RectangleGeometry(new Rect(Gutter, top, Math.Max(0, width - Gutter), Math.Max(0, bottom - top))));
        foreach (var guide in new[] { 0.25, 0.5, 0.75, 1.0 })
        {
            var y = Math.Round(YAtVelocity(guide)) + 0.5;
            context.DrawLine(LaneGuidePen, new Point(Gutter, y), new Point(width, y));
        }

        for (var beat = firstBeat - (firstBeat % everyBeat); beat <= Math.Min(lastBeat, (long)Bars * _beatsPerBar); beat += everyBeat)
        {
            var x = Math.Round(Gutter + (beat * _pixelsPerBeat) - HorizontalOffset) + 0.5;
            context.DrawLine(beat % _beatsPerBar == 0 ? BarPen : BeatPen, new Point(x, top), new Point(x, bottom));
        }

        var anyChosen = _chosen.Count > 0;
        for (var index = 0; index < _notes.Count; index++)
        {
            if (!_chosen.Contains(index))
            {
                DrawStem(context, _notes[index], anyChosen ? OtherStemPen : StemPen, width);
            }
        }

        foreach (var index in _chosen)
        {
            DrawStem(context, _notes[index], ChosenStemPen, width);
        }

        if (_recording is not null)
        {
            foreach (var note in _recording)
            {
                DrawStem(context, note, RecordingStemPen, width);
            }
        }

        if (_curveBase is not null)
        {
            DrawCurve(context, width);
        }

        var playheadX = Gutter + (_playhead * PixelsPerTick) - HorizontalOffset;
        context.DrawLine(PlayheadPen, new Point(playheadX, top), new Point(playheadX, bottom));

        if (_editable && _laneTool == LaneTool.Curve)
        {
            var hint = _curveBase is null
                ? "Click to place the points of a curve"
                : _curve.Count < 2
                    ? "Click to place another point"
                    : "Drag points to shape it · right-click takes one away · Enter applies · Esc cancels";
            var text = new FormattedText(hint, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelFace, 10, HintText, dpi);
            context.DrawText(text, new Point(width - text.Width - 8, top + 2));
        }

        context.Pop();
    }

    private void DrawStem(DrawingContext context, Note note, Pen pen, double width)
    {
        var x = XAt(note.Start);
        if (x < Gutter - 4 || x > width + 4)
        {
            return;
        }

        var y = YAtVelocity(note.Velocity);
        context.DrawLine(pen, new Point(x + 1, YAtVelocity(0)), new Point(x + 1, y));
        context.DrawEllipse(pen.Brush, null, new Point(x + 1, y), 3, 3);
    }

    /// <summary>The curve between its first and last points, followed a few pixels at a time, and the points themselves.</summary>
    private void DrawCurve(DrawingContext context, double width)
    {
        var spline = new Spline(_curve);
        if (spline.Count >= 2)
        {
            var from = Math.Max(Gutter, XAt(spline.First));
            var to = Math.Min(width, XAt(spline.Last));
            var line = new StreamGeometry();
            using (var geometry = line.Open())
            {
                var started = false;
                for (var x = from; x <= to + 2; x += 2)
                {
                    var at = Math.Min(x, to);
                    if (spline.At(TickAt(at)) is not { } velocity)
                    {
                        continue;
                    }

                    var point = new Point(at, YAtVelocity(velocity));
                    if (!started)
                    {
                        geometry.BeginFigure(point, false, false);
                        started = true;
                    }
                    else
                    {
                        geometry.LineTo(point, true, true);
                    }
                }
            }

            line.Freeze();
            context.DrawGeometry(null, CurvePen, line);
        }

        foreach (var (tick, velocity) in _curve)
        {
            context.DrawEllipse(CurvePointFill, HandleEdgePen, new Point(XAt(tick), YAtVelocity(velocity)), CurvePointRadius, CurvePointRadius);
        }
    }

    /// <summary>Dragging past an edge scrolls that way, so a note can be taken further than the roll shows.</summary>
    private void ScrollTowards(Point point)
    {
        const double edge = 12;
        if (point.X > ActualWidth - edge)
        {
            SetHorizontalOffset(HorizontalOffset + (_pixelsPerBeat / 4));
        }
        else if (point.X < Gutter + edge && _drag != Drag.Audition)
        {
            SetHorizontalOffset(HorizontalOffset - (_pixelsPerBeat / 4));
        }

        if (point.Y > NotesBottom - edge && point.Y < NotesBottom)
        {
            SetVerticalOffset(VerticalOffset + _rowHeight);
        }
        else if (point.Y < Header + edge)
        {
            SetVerticalOffset(VerticalOffset - _rowHeight);
        }
    }

    private long Snapped(long tick, bool snap) => snap ? NoteEditing.Snap(tick, _grid) : tick;

    /// <summary>The tick at a point across the roll, not rounded, and negative left of the song's start.</summary>
    private double TickAt(double x) => (x - Gutter + HorizontalOffset) / PixelsPerTick;

    private double XAt(double tick) => Gutter + (tick * PixelsPerTick) - HorizontalOffset;

    /// <summary>The step whose row is at a point down the roll; past either end it carries on counting.</summary>
    private int StepAt(double y) => LastStep - (int)Math.Floor((y - Header + VerticalOffset) / _rowHeight);

    private Rect NoteRect(Note note) =>
        new(XAt(note.Start), RowTop(note.Step) - VerticalOffset, Math.Max(2, note.Length * PixelsPerTick), _rowHeight);

    private (int Lowest, int Highest) VisibleSteps(double height)
    {
        var highest = LastStep - (int)Math.Floor(VerticalOffset / _rowHeight);
        var lowest = LastStep - (int)Math.Ceiling((VerticalOffset + height - Header) / _rowHeight);
        return (Math.Max(0, lowest), Math.Min(LastStep, highest));
    }

    private double RowTop(int step) => Header + ((LastStep - step) * _rowHeight);

    private Brush RowBrush(int step)
    {
        if (TemperedKeyboard.IsWhiteStep(_temperament, step))
        {
            return WhiteRow;
        }

        return !_temperament.IsStandard && Tet27.Shade(step) == KeyShade.Light ? LightRow : DarkRow;
    }

    /// <summary>Zoomed far out, beat lines would run together, so only every bar's, or every few bars', are drawn.</summary>
    private long BeatsBetweenLines()
    {
        if (_pixelsPerBeat >= FinestGridLines)
        {
            return 1;
        }

        var bars = 1L;
        while (bars * _beatsPerBar * _pixelsPerBeat < FinestGridLines * 2)
        {
            bars *= 2;
        }

        return bars * _beatsPerBar;
    }

    /// <summary>Faint lines for a grid finer than a beat, when they are far enough apart to be of use.</summary>
    private void DrawGrid(DrawingContext context, double width, double height)
    {
        if (_grid <= 1 || _grid >= Song.TicksPerBeat || _grid * PixelsPerTick < FinestGridLines)
        {
            return;
        }

        var first = (long)Math.Floor(TickAt(Gutter) / _grid);
        var last = (long)Math.Ceiling(TickAt(width) / _grid);
        for (var line = Math.Max(0, first); line <= last; line++)
        {
            if (line * _grid % Song.TicksPerBeat == 0)
            {
                continue;
            }

            var x = Math.Round(XAt(line * _grid)) + 0.5;
            context.DrawLine(GridPen, new Point(x, Header), new Point(x, height));
        }
    }

    /// <summary>
    /// A note's colour deepens with its velocity, so loud and soft can be told apart at a glance. Chosen
    /// notes are lighter, edged in white, and carry a dot at each end to stretch them by.
    /// </summary>
    private void DrawNotes(DrawingContext context, IReadOnlyList<Note> notes, Color colour, int lowest, int highest, double width, HashSet<int>? chosen)
    {
        for (var index = 0; index < notes.Count; index++)
        {
            var note = notes[index];
            if (note.Step < lowest || note.Step > highest)
            {
                continue;
            }

            var rect = NoteRect(note);
            if (rect.Left > width || rect.Right < Gutter)
            {
                continue;
            }

            var isChosen = chosen?.Contains(index) == true;
            var shade = isChosen ? ChosenColour : colour;
            var alpha = (byte)(110 + (145 * Math.Clamp(note.Velocity, 0, 1)));
            var fill = new SolidColorBrush(Color.FromArgb(alpha, shade.R, shade.G, shade.B));
            fill.Freeze();
            context.DrawRectangle(fill, isChosen ? ChosenEdgePen : NoteEdgePen, new Rect(rect.Left + 0.5, rect.Top + 0.5, rect.Width - 1, rect.Height - 1));
        }

        if (chosen is null)
        {
            return;
        }

        // The dots go on last, over any note they overlap, since they are what the mouse will look for.
        foreach (var index in chosen)
        {
            var note = notes[index];
            if (note.Step < lowest || note.Step > highest)
            {
                continue;
            }

            var rect = NoteRect(note);
            var middle = rect.Top + (rect.Height / 2);
            context.DrawEllipse(HandleFill, HandleEdgePen, new Point(rect.Left, middle), HandleRadius, HandleRadius);
            context.DrawEllipse(HandleFill, HandleEdgePen, new Point(rect.Right, middle), HandleRadius, HandleRadius);
        }
    }

    private void DrawGutter(DrawingContext context, int lowest, int highest, double height, double dpi)
    {
        context.PushClip(new RectangleGeometry(new Rect(0, Header, Gutter, Math.Max(0, height - Header))));
        var fontSize = Math.Min(8, _rowHeight);
        for (var step = lowest; step <= highest; step++)
        {
            var top = RowTop(step) - VerticalOffset;
            var white = TemperedKeyboard.IsWhiteStep(_temperament, step);
            var keyWidth = white ? Gutter : Gutter * 0.62;
            context.DrawRectangle(TemperedKeyboard.KeyBrush(_temperament, step), KeyEdgePen, new Rect(0, top, keyWidth, _rowHeight));

            if (_rowHeight >= 5 && TemperedKeyboard.LabelOf(_temperament, step) is { } label)
            {
                var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelFace, fontSize, KeyText, dpi);
                context.DrawText(text, new Point(Gutter - text.Width - 3, top + ((_rowHeight - text.Height) / 2)));
            }
        }

        context.Pop();
    }

    private void DrawHeader(DrawingContext context, long firstBeat, long lastBeat, double width, double dpi)
    {
        context.DrawRectangle(Background, null, new Rect(0, 0, width, Header));
        context.PushClip(new RectangleGeometry(new Rect(Gutter, 0, Math.Max(0, width - Gutter), Header)));

        // Bar numbers need room; zoomed out, only every few bars are numbered.
        var everyBars = 1L;
        while (everyBars * _beatsPerBar * _pixelsPerBeat < 28)
        {
            everyBars *= 2;
        }

        var every = everyBars * _beatsPerBar;
        for (var beat = firstBeat - (firstBeat % every); beat <= lastBeat; beat += every)
        {
            if (beat < 0 || beat >= (long)Bars * _beatsPerBar)
            {
                continue;
            }

            var x = Gutter + (beat * _pixelsPerBeat) - HorizontalOffset;
            var bar = ((beat / _beatsPerBar) + 1).ToString(CultureInfo.CurrentCulture);
            var text = new FormattedText(bar, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelFace, 10, HeaderText, dpi);
            context.DrawText(text, new Point(x + 3, (Header - text.Height) / 2));
            context.DrawLine(BarPen, new Point(x + 0.5, Header - 6), new Point(x + 0.5, Header));
        }

        var playheadX = Gutter + (_playhead * PixelsPerTick) - HorizontalOffset;
        var marker = new StreamGeometry();
        using (var geometry = marker.Open())
        {
            geometry.BeginFigure(new Point(playheadX - 5, Header - 8), true, true);
            geometry.LineTo(new Point(playheadX + 5, Header - 8), true, false);
            geometry.LineTo(new Point(playheadX, Header), true, false);
        }

        marker.Freeze();
        context.DrawGeometry(PlayheadPen.Brush, null, marker);
        context.Pop();
    }

    private void Changed()
    {
        InvalidateVisual();
        ScrollOwner?.InvalidateScrollInfo();
    }

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
