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
/// A keyboard for 27-TET. The tones of 9-TET are full-length white keys; between two of them sit two
/// short keys, the light gray step just above the white on the left and the dark gray step just below
/// the white on the right. Keys a standard keyboard reaches through the <see cref="KeyMap"/> carry a dot.
/// </summary>
public sealed class Tet27Keyboard : FrameworkElement
{
    public static readonly DependencyProperty FirstStepProperty = DependencyProperty.Register(
        nameof(FirstStep), typeof(int), typeof(Tet27Keyboard),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsMeasure), IsWhite);

    public static readonly DependencyProperty LastStepProperty = DependencyProperty.Register(
        nameof(LastStep), typeof(int), typeof(Tet27Keyboard),
        new FrameworkPropertyMetadata(7 * Tet27.StepsPerOctave + 9, FrameworkPropertyMetadataOptions.AffectsMeasure), IsWhite);

    public static readonly DependencyProperty WhiteKeyWidthProperty = DependencyProperty.Register(
        nameof(WhiteKeyWidth), typeof(double), typeof(Tet27Keyboard),
        new FrameworkPropertyMetadata(28.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty KeyMapProperty = DependencyProperty.Register(
        nameof(KeyMap), typeof(StandardKeyMap), typeof(Tet27Keyboard),
        new FrameworkPropertyMetadata(StandardKeyMap.Default, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>"Light gray (35%)" and "dark gray (65%)" are amounts of black, as a printer would say.</summary>
    private static readonly Brush WhiteBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)));
    private static readonly Brush LightBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xA6, 0xA6, 0xA6)));
    private static readonly Brush DarkBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x59, 0x59, 0x59)));
    private static readonly Brush PressedBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x3D, 0x8B, 0xE8)));
    private static readonly Brush MarkBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xD9, 0x6B, 0x1E)));
    private static readonly Brush LabelBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30)));
    private static readonly Pen EdgePen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x26)), 1));
    private static readonly Typeface LabelFace = new("Segoe UI");

    /// <summary>How much of a white key's length the short keys take.</summary>
    private const double ShortKeyLength = 0.62;

    /// <summary>How much of a white key's width each short key takes.</summary>
    private const double ShortKeyWidth = 0.38;

    private readonly HashSet<int> _pressed = [];
    private int? _mouseStep;

    public event EventHandler<StepEventArgs>? StepPressed;

    public event EventHandler<StepEventArgs>? StepReleased;

    /// <summary>The lowest step shown; a white key.</summary>
    public int FirstStep
    {
        get => (int)GetValue(FirstStepProperty);
        set => SetValue(FirstStepProperty, value);
    }

    /// <summary>The highest step shown; a white key. The default ends just above C8, as a piano does.</summary>
    public int LastStep
    {
        get => (int)GetValue(LastStepProperty);
        set => SetValue(LastStepProperty, value);
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
        return new Size(WhiteCount * WhiteKeyWidth, height);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var height = ActualHeight;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // White keys first, so the short keys lie over them.
        for (var step = FirstStep; step <= LastStep; step += Tet27.StepsPerShadeCycle)
        {
            DrawKey(drawingContext, step, height, dpi);
        }

        for (var step = FirstStep; step <= LastStep; step++)
        {
            if (Tet27.Shade(step) != KeyShade.White)
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

    private int WhiteCount => ((LastStep - FirstStep) / Tet27.StepsPerShadeCycle) + 1;

    private double WhiteLeft(int whiteStep) => (whiteStep - FirstStep) / Tet27.StepsPerShadeCycle * WhiteKeyWidth;

    /// <summary>A light key overhangs the right of the white below it, and a dark key the left of the white above.</summary>
    private (double Left, double Width, double Length) KeyBounds(int step)
    {
        var width = WhiteKeyWidth;
        var length = ActualHeight;
        return Tet27.Shade(step) switch
        {
            KeyShade.White => (WhiteLeft(step), width, length),
            KeyShade.Light => (WhiteLeft(step - 1) + (width * (1 - ShortKeyWidth)), width * ShortKeyWidth, length * ShortKeyLength),
            _ => (WhiteLeft(step + 1), width * ShortKeyWidth, length * ShortKeyLength),
        };
    }

    private void DrawKey(DrawingContext context, int step, double height, double dpi)
    {
        var (left, width, length) = KeyBounds(step);
        var shade = Tet27.Shade(step);
        var fill = _pressed.Contains(step) ? PressedBrush : shade switch
        {
            KeyShade.White => WhiteBrush,
            KeyShade.Light => LightBrush,
            _ => DarkBrush,
        };

        // A light and a dark key share the gap between two whites; a hairline keeps them apart.
        var inset = shade == KeyShade.White ? 0 : 0.5;
        context.DrawRectangle(fill, EdgePen, new Rect(left + inset + 0.5, 0.5, Math.Max(1, width - (2 * inset) - 1), length - 1));

        if (KeyMap.MidiNoteFor(step) is not null)
        {
            var radius = Math.Max(2, width * 0.14);
            context.DrawEllipse(MarkBrush, null, new Point(left + (width / 2), length - (radius * 2.5)), radius, radius);
        }

        if (shade == KeyShade.White && Tet27.Degree(step) == 0)
        {
            var label = "A" + Tet27.Octave(step).ToString(CultureInfo.CurrentCulture);
            var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelFace, 10, LabelBrush, dpi);
            context.DrawText(text, new Point(left + ((width - text.Width) / 2), height - text.Height - 18));
        }
    }

    private int? StepAt(Point point)
    {
        if (point.X < 0 || point.Y < 0 || point.Y > ActualHeight)
        {
            return null;
        }

        var whiteIndex = (int)Math.Floor(point.X / WhiteKeyWidth);
        var white = FirstStep + (whiteIndex * Tet27.StepsPerShadeCycle);
        if (white > LastStep)
        {
            return null;
        }

        if (point.Y < ActualHeight * ShortKeyLength)
        {
            var across = (point.X / WhiteKeyWidth) - whiteIndex;
            if (across >= 1 - ShortKeyWidth && white + 1 <= LastStep)
            {
                return white + 1;
            }

            if (across < ShortKeyWidth && white - 1 >= FirstStep)
            {
                return white - 1;
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

    private static bool IsWhite(object value) => Tet27.Shade((int)value) == KeyShade.White;

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
