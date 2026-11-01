using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Sophron.Tuning;

namespace Sophron.Controls;

public sealed class StepEventArgs(int step) : EventArgs
{
    public int Step { get; } = step;
}

/// <summary>
/// A keyboard for the <see cref="Temperament"/> in use, from A0 to just past C8. In 27-TET the tones of
/// 9-TET are full-length white keys, and between two of them sit two short keys: the light gray step
/// just above the white on the left, and the dark gray step just below the white on the right. Keys a
/// standard keyboard reaches through the <see cref="KeyMap"/> carry a dot. In 12-TET it is a piano.
/// </summary>
public sealed class TemperedKeyboard : FrameworkElement
{
    public static readonly DependencyProperty TemperamentProperty = DependencyProperty.Register(
        nameof(Temperament), typeof(Temperament), typeof(TemperedKeyboard),
        new FrameworkPropertyMetadata(Temperament.TwentySeven(), FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty WhiteKeyWidthProperty = DependencyProperty.Register(
        nameof(WhiteKeyWidth), typeof(double), typeof(TemperedKeyboard),
        new FrameworkPropertyMetadata(28.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty KeyMapProperty = DependencyProperty.Register(
        nameof(KeyMap), typeof(StandardKeyMap), typeof(TemperedKeyboard),
        new FrameworkPropertyMetadata(StandardKeyMap.Default, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>"Light gray (35%)" and "dark gray (65%)" are amounts of black, as a printer would say.</summary>
    private static readonly Brush WhiteBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)));
    private static readonly Brush LightBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xA6, 0xA6, 0xA6)));
    private static readonly Brush DarkBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x59, 0x59, 0x59)));
    private static readonly Brush BlackBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C)));
    private static readonly Brush PressedBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x3D, 0x8B, 0xE8)));
    private static readonly Brush MarkBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xD9, 0x6B, 0x1E)));
    private static readonly Brush LabelBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30)));
    private static readonly Pen EdgePen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x26)), 1));
    private static readonly Typeface LabelFace = new("Segoe UI");

    /// <summary>The 27-TET keyboard ends on the white just past C8, as a piano ends on C8 itself.</summary>
    private const int LastStep27 = (7 * Tet27.StepsPerOctave) + 9;

    private const int LastStep12 = (7 * 12) + 3;

    /// <summary>The white keys of a piano octave, from A, and the degree of C among them.</summary>
    private static readonly int[] PianoWhites = [0, 2, 3, 5, 7, 8, 10];
    private const int PianoC = 3;

    /// <summary>How much of a white key's length the short keys take.</summary>
    private const double ShortKeyLength = 0.62;

    /// <summary>How much of a white key's width each short key of 27-TET takes.</summary>
    private const double ShortKeyWidth = 0.38;

    /// <summary>A piano's black keys are wider, since there is only one in each gap.</summary>
    private const double BlackKeyWidth = 0.58;

    private readonly HashSet<int> _pressed = [];
    private int? _mouseStep;

    public event EventHandler<StepEventArgs>? StepPressed;

    public event EventHandler<StepEventArgs>? StepReleased;

    public Temperament Temperament
    {
        get => (Temperament)GetValue(TemperamentProperty);
        set => SetValue(TemperamentProperty, value);
    }

    public double WhiteKeyWidth
    {
        get => (double)GetValue(WhiteKeyWidthProperty);
        set => SetValue(WhiteKeyWidthProperty, value);
    }

    public StandardKeyMap KeyMap
    {
        get => (StandardKeyMap)GetValue(KeyMapProperty);
        set => SetValue(KeyMapProperty, value);
    }

    /// <summary>Shows a step as held down, whoever is holding it: the mouse, a MIDI keyboard or the sequencer.</summary>
    public void SetPressed(int step, bool pressed)
    {
        if (pressed ? _pressed.Add(step) : _pressed.Remove(step))
        {
            InvalidateVisual();
        }
    }

    /// <summary>Lets go of every key shown as held, as when the input holding them goes away.</summary>
    public void ReleaseAll()
    {
        if (_pressed.Count > 0)
        {
            _pressed.Clear();
            InvalidateVisual();
        }
    }

    /// <summary>The middle of a step's key, for scrolling it into view.</summary>
    public double CentreOf(int step)
    {
        var (left, width, _) = KeyBounds(step);
        return left + (width / 2);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var height = double.IsInfinity(availableSize.Height) ? 160 : availableSize.Height;
        return new Size((WhiteIndex(LastStep) + 1) * WhiteKeyWidth, height);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var height = ActualHeight;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // White keys first, so the short keys lie over them.
        for (var step = 0; step <= LastStep; step++)
        {
            if (IsWhite(step))
            {
                DrawKey(drawingContext, step, height, dpi);
            }
        }

        for (var step = 0; step <= LastStep; step++)
        {
            if (!IsWhite(step))
            {
                DrawKey(drawingContext, step, height, dpi);
            }
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        CaptureMouse();
        MoveMouseTo(StepAt(e.GetPosition(this)));
        e.Handled = true;
    }

    /// <summary>Dragging across the keys plays each one in turn, as a glissando should.</summary>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsMouseCaptured)
        {
            MoveMouseTo(StepAt(e.GetPosition(this)));
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        MoveMouseTo(null);
    }

    private bool IsPiano => Temperament.IsStandard;

    private int LastStep => IsPiano ? LastStep12 : LastStep27;

    private bool IsWhite(int step) => IsPiano
        ? PianoWhites.Contains(Temperament.Degree(step))
        : Tet27.Shade(step) == KeyShade.White;

    /// <summary>How many white keys lie to the left of a white step.</summary>
    private int WhiteIndex(int whiteStep) => IsPiano
        ? (Temperament.Octave(whiteStep) * PianoWhites.Length) + Array.IndexOf(PianoWhites, Temperament.Degree(whiteStep))
        : whiteStep / Tet27.StepsPerShadeCycle;

    private int WhiteStep(int whiteIndex) => IsPiano
        ? (whiteIndex / PianoWhites.Length * 12) + PianoWhites[whiteIndex % PianoWhites.Length]
        : whiteIndex * Tet27.StepsPerShadeCycle;

    /// <summary>
    /// A light key overhangs the right of the white below it, and a dark key the left of the white
    /// above; a piano's black key sits across the line between the two whites beside it.
    /// </summary>
    private (double Left, double Width, double Length) KeyBounds(int step)
    {
        var width = WhiteKeyWidth;
        var length = ActualHeight;
        if (IsWhite(step))
        {
            return (WhiteIndex(step) * width, width, length);
        }

        var shortLength = length * ShortKeyLength;
        if (IsPiano)
        {
            var line = (WhiteIndex(step - 1) + 1) * width;
            return (line - (width * BlackKeyWidth / 2), width * BlackKeyWidth, shortLength);
        }

        return Tet27.Shade(step) == KeyShade.Light
            ? ((WhiteIndex(step - 1) * width) + (width * (1 - ShortKeyWidth)), width * ShortKeyWidth, shortLength)
            : (WhiteIndex(step + 1) * width, width * ShortKeyWidth, shortLength);
    }

    private void DrawKey(DrawingContext context, int step, double height, double dpi)
    {
        var (left, width, length) = KeyBounds(step);
        var white = IsWhite(step);
        var fill = _pressed.Contains(step) ? PressedBrush
            : white ? WhiteBrush
            : IsPiano ? BlackBrush
            : Tet27.Shade(step) == KeyShade.Light ? LightBrush : DarkBrush;

        // A light and a dark key share the gap between two whites; a hairline keeps them apart.
        var inset = white || IsPiano ? 0 : 0.5;
        context.DrawRectangle(fill, EdgePen, new Rect(left + inset + 0.5, 0.5, Math.Max(1, width - (2 * inset) - 1), length - 1));

        // Every key of a piano is a standard key, so the dots only mean something in 27-TET.
        if (!IsPiano && KeyMap.MidiNoteFor(step) is not null)
        {
            var radius = Math.Max(2, width * 0.14);
            context.DrawEllipse(MarkBrush, null, new Point(left + (width / 2), length - (radius * 2.5)), radius, radius);
        }

        var label = Label(step);
        if (label is not null)
        {
            var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelFace, 10, LabelBrush, dpi);
            context.DrawText(text, new Point(left + ((width - text.Width) / 2), height - text.Height - 18));
        }
    }

    /// <summary>27-TET counts its octaves from A, as its steps do; a piano is labelled at each C, as pianists expect.</summary>
    private string? Label(int step)
    {
        if (IsPiano)
        {
            return Temperament.Degree(step) == PianoC ? StandardKeyMap.NoteName(StandardKeyMap.A0MidiNote + step) : null;
        }

        return Tet27.Shade(step) == KeyShade.White && Tet27.Degree(step) == 0
            ? "A" + Tet27.Octave(step).ToString(CultureInfo.CurrentCulture)
            : null;
    }

    private int? StepAt(Point point)
    {
        if (point.X < 0 || point.Y < 0 || point.Y > ActualHeight)
        {
            return null;
        }

        var white = WhiteStep((int)Math.Floor(point.X / WhiteKeyWidth));
        if (white > LastStep)
        {
            return null;
        }

        // The short keys lie over the whites, so they are looked for first.
        if (point.Y < ActualHeight * ShortKeyLength)
        {
            foreach (var neighbour in new[] { white - 1, white + 1 })
            {
                if (neighbour >= 0 && neighbour <= LastStep && !IsWhite(neighbour))
                {
                    var (left, width, _) = KeyBounds(neighbour);
                    if (point.X >= left && point.X < left + width)
                    {
                        return neighbour;
                    }
                }
            }
        }

        return white;
    }

    private void MoveMouseTo(int? step)
    {
        if (step == _mouseStep)
        {
            return;
        }

        if (_mouseStep is { } previous)
        {
            StepReleased?.Invoke(this, new StepEventArgs(previous));
        }

        _mouseStep = step;
        if (step is { } next)
        {
            StepPressed?.Invoke(this, new StepEventArgs(next));
        }
    }

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
