using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using Sophron.Audio;
using Sophron.Controls;
using Sophron.Tuning;

namespace Sophron;

public partial class MainWindow : Window
{
    private const float MouseVelocity = 0.8f;

    private readonly Engine? _engine;

    /// <summary>
    /// The step each MIDI note started, so it stops the same step even if the ties change while it is
    /// held. Only the MIDI thread touches it.
    /// </summary>
    private readonly int[] _midiSteps = new int[128];

    /// <summary>
    /// Listens on every input at once. A keyboard can reach Windows by more than one road (a Nektar's
    /// own USB ports, or its MIDI cable into an interface), and nobody should have to guess which.
    /// </summary>
    private const string AllInputs = "All inputs";

    private readonly List<MidiKeyboard> _inputs = [];

    // Read on the MIDI thread; each is replaced whole, never changed in place.
    private volatile Temperament _temperament;
    private volatile StandardKeyMap _map = StandardKeyMap.Default;

    /// <summary>
    /// How far the pitch wheel reaches, in steps, and where it starts: a whole tone in 12-TET, and in
    /// 27-TET one white key to the next.
    /// </summary>
    private static readonly Dictionary<int, (int[] Ranges, int Default)> BendRanges = new()
    {
        [StandardKeyMap.KeysPerOctave] = ([1, 2, 3, 5, 7, 12], 2),
        [Tet27.StepsPerOctave] = ([1, 2, 3, 6, 9, 27], 3),
    };

    // The wheel and its range are read on the MIDI thread, and each is a single int, read whole.
    private volatile int _bendSteps;
    private volatile int _wheel = PitchBend.Centre;

    private const int WmDeviceChange = 0x0219;
    private static readonly TimeSpan DeviceSettleTime = TimeSpan.FromSeconds(1.5);
    private readonly DispatcherTimer _devicesSettled;
    private bool _refreshing;

    /// <summary>Set while the tuning controls are being brought into line, so they do not answer themselves.</summary>
    private bool _tuning = true;

    public MainWindow()
    {
        InitializeComponent();

        var steps = Preferences.StepsPerOctave is StandardKeyMap.KeysPerOctave ? StandardKeyMap.KeysPerOctave : Tet27.StepsPerOctave;
        var fundamental = Math.Clamp(Preferences.FundamentalHz ?? Temperament.Fundamental432, Temperament.LowestFundamentalHz, Temperament.HighestFundamentalHz);
        _temperament = new Temperament(steps, fundamental);

        _engine = Engine.TryStart(out var problem);
        Status.Text = problem ?? $"Engine running at {_engine!.SampleRate.ToString("0", CultureInfo.CurrentCulture)} Hz.";

        FillTie(BTie, 2);
        FillTie(DSharpTie, 6);
        FillTie(GTie, 10);

        Temperaments.Items.Add(new ComboBoxItem { Content = "12-TET", Tag = StandardKeyMap.KeysPerOctave });
        Temperaments.Items.Add(new ComboBoxItem { Content = "27-TET", Tag = Tet27.StepsPerOctave });
        Temperaments.SelectedIndex = steps == StandardKeyMap.KeysPerOctave ? 0 : 1;
        FundamentalSlider.Minimum = Temperament.LowestFundamentalHz;
        FundamentalSlider.Maximum = Temperament.HighestFundamentalHz;
        _tuning = false;
        ShowTemperament();

        _devicesSettled = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = DeviceSettleTime };
        _devicesSettled.Tick += (_, _) =>
        {
            _devicesSettled.Stop();
            RefreshInputs();
        };

        RefreshInputs();
        Loaded += (_, _) => ScrollToA4();
    }

    /// <summary>Windows tells every top-level window when devices come and go, without being asked.</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(OnWindowMessage);
    }

    protected override void OnClosed(EventArgs e)
    {
        _devicesSettled.Stop();
        CloseInputs();
        _engine?.Dispose();
        Preferences.StepsPerOctave = _temperament.StepsPerOctave;
        Preferences.FundamentalHz = _temperament.FundamentalHz;
        base.OnClosed(e);
    }

    /// <summary>
    /// One plug brings a burst of device changes, and Windows MIDI Services publishes the ports a moment
    /// after the device itself, so the inputs are looked at again only once things have gone quiet.
    /// </summary>
    private nint OnWindowMessage(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmDeviceChange)
        {
            _devicesSettled.Stop();
            _devicesSettled.Start();
        }

        return 0;
    }

    /// <summary>
    /// Lists the MIDI inputs again and, when they have changed, reopens the chosen one. Port numbers
    /// shift as devices come and go, so every open input is closed and opened afresh. A chosen device
    /// that has gone away is stood in for by all inputs, and is chosen again when it comes back.
    /// </summary>
    private void RefreshInputs()
    {
        var names = MidiKeyboard.DeviceNames();
        var listed = MidiInputs.Items.Cast<object>().Skip(2).Cast<string>().ToList();
        var first = MidiInputs.Items.Count == 0;
        if (!first && names.SequenceEqual(listed))
        {
            return;
        }

        _refreshing = true;
        try
        {
            MidiInputs.Items.Clear();
            MidiInputs.Items.Add("None");
            MidiInputs.Items.Add(AllInputs);
            foreach (var name in names)
            {
                MidiInputs.Items.Add(name);
            }

            var remembered = MidiInputs.Items.IndexOf(Preferences.MidiInput);
            MidiInputs.SelectedIndex = remembered >= 0 ? remembered : 1;
        }
        finally
        {
            _refreshing = false;
        }

        OpenSelectedInputs();
        if (!first)
        {
            var found = names.Except(listed).ToList();
            var lost = listed.Except(names).ToList();
            Status.Text = string.Join(" · ", new[]
            {
                found.Count > 0 ? "Found " + string.Join(", ", found) : null,
                lost.Count > 0 ? "Lost " + string.Join(", ", lost) : null,
            }.Where(part => part is not null));
        }
    }

    private void Play(int step, float velocity)
    {
        _engine?.NoteOn(step, _temperament.Frequency(step), velocity);
        Keyboard.SetPressed(step, true);
        Status.Text = Describe(step);
    }

    private void Stop(int step)
    {
        _engine?.NoteOff(step);
        Keyboard.SetPressed(step, false);
    }

    private string Describe(int step)
    {
        var temperament = _temperament;
        var frequency = temperament.Frequency(step).ToString("0.00", CultureInfo.CurrentCulture);
        if (temperament.IsStandard)
        {
            return $"Step {step}: {StandardKeyMap.NoteName(StandardKeyMap.A0MidiNote + step)} · {frequency} Hz";
        }

        var key = temperament.MidiNoteFor(step, _map) is { } note ? $" · {StandardKeyMap.NoteName(note)} on a standard keyboard" : "";
        return $"Step {step}: octave {temperament.Octave(step)}, degree {temperament.Degree(step)} ({Tet27.Shade(step).ToString().ToLowerInvariant()}) · {frequency} Hz{key}";
    }

    private void OnStepPressed(object? sender, StepEventArgs e) => Play(e.Step, MouseVelocity);

    private void OnStepReleased(object? sender, StepEventArgs e) => Stop(e.Step);

    /// <summary>
    /// Steps mean something else in the other temperament, so every sounding note is let go before the
    /// switch rather than left at a pitch nothing will ever stop.
    /// </summary>
    private void OnTemperamentChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_tuning || Temperaments.SelectedItem is not ComboBoxItem { Tag: int steps } || steps == _temperament.StepsPerOctave)
        {
            return;
        }

        _engine?.AllNotesOff();
        Keyboard.ReleaseAll();
        _temperament = _temperament with { StepsPerOctave = steps };
        ShowTemperament();
        ScrollToA4();
    }

    /// <summary>Brings the keyboard, the tuner, the bend ranges and the ties into line with the temperament.</summary>
    private void ShowTemperament()
    {
        var temperament = _temperament;
        Keyboard.Temperament = temperament;
        Ties.Visibility = temperament.IsStandard ? Visibility.Collapsed : Visibility.Visible;
        FillBendRange();
        ShowFundamental();
    }

    /// <summary>
    /// Retunes everything, notes already sounding included, so a chord can be held while A0 is moved
    /// to hear what it does.
    /// </summary>
    private void SetFundamental(double hertz)
    {
        hertz = Math.Clamp(hertz, Temperament.LowestFundamentalHz, Temperament.HighestFundamentalHz);
        var old = _temperament;
        if (hertz != old.FundamentalHz)
        {
            _temperament = old with { FundamentalHz = hertz };
            _engine?.Retune(hertz / old.FundamentalHz);
            Keyboard.Temperament = _temperament;
        }

        ShowFundamental();
    }

    private void ShowFundamental()
    {
        var temperament = _temperament;
        _tuning = true;
        try
        {
            FundamentalSlider.Value = temperament.FundamentalHz;
            FundamentalText.Text = temperament.FundamentalHz.ToString("0.000", CultureInfo.CurrentCulture);
        }
        finally
        {
            _tuning = false;
        }

        // How far from concert pitch, which is what most other instruments in the room will be at.
        var fromConcert = 1200 * Math.Log2(temperament.FundamentalHz / Temperament.Fundamental440);
        A4Text.Text = $"A4 = {temperament.A4Hz.ToString("0.00", CultureInfo.CurrentCulture)} Hz ({fromConcert.ToString("+0.0;−0.0;0", CultureInfo.CurrentCulture)}¢ from 440)";
    }

    private void OnFundamentalSlid(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_tuning)
        {
            SetFundamental(e.NewValue);
        }
    }

    private void OnFundamentalKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnFundamentalTyped(sender, e);
            e.Handled = true;
        }
    }

    /// <summary>Something that is not a number puts back the fundamental already in use.</summary>
    private void OnFundamentalTyped(object sender, RoutedEventArgs e)
    {
        if (double.TryParse(FundamentalText.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var hertz))
        {
            SetFundamental(hertz);
        }
        else
        {
            ShowFundamental();
        }
    }

    private void OnFundamentalPreset(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string a4 })
        {
            SetFundamental(Temperament.FundamentalFor(double.Parse(a4, CultureInfo.InvariantCulture)));
        }
    }

    private void ScrollToA4() =>
        KeyboardScroller.ScrollToHorizontalOffset(Keyboard.CentreOf(4 * _temperament.StepsPerOctave) - (KeyboardScroller.ViewportWidth / 2));

    private void OnMidiInputChanged(object sender, SelectionChangedEventArgs e)
    {
        // Rebuilding the list moves the selection about; only a choice made by hand is remembered.
        if (_refreshing)
        {
            return;
        }

        Preferences.MidiInput = MidiInputs.SelectedItem as string;
        OpenSelectedInputs();
    }

    /// <summary>
    /// For a keyboard that has gone quiet. If this brings it back, the fault was in Windows' MIDI
    /// service; if only switching the keyboard off and on does, it was in the keyboard or its USB link.
    /// </summary>
    private void OnReconnect(object sender, RoutedEventArgs e)
    {
        OpenSelectedInputs();
        Status.Text = $"Reopened {_inputs.Count.ToString(CultureInfo.CurrentCulture)} MIDI input{(_inputs.Count == 1 ? "" : "s")}.";
    }

    /// <summary>Notes held on an input that closes would never hear their note-off, so everything lets go.</summary>
    private void OpenSelectedInputs()
    {
        CloseInputs();
        _engine?.AllNotesOff();
        _engine?.Sustain(false);
        _wheel = PitchBend.Centre;
        _engine?.PitchBend(0);
        Keyboard.ReleaseAll();

        var devices = MidiInputs.SelectedIndex switch
        {
            < 1 => [],
            1 => Enumerable.Range(0, MidiInputs.Items.Count - 2),
            var index => [index - 2],
        };

        var failed = new List<string>();
        foreach (var device in devices)
        {
            try
            {
                var input = MidiKeyboard.Open(device);
                Listen(input);
                _inputs.Add(input);
            }
            catch (NAudio.MmException ex)
            {
                failed.Add($"{MidiInputs.Items[device + 2]} ({ex.Message})");
            }
        }

        if (failed.Count > 0)
        {
            Status.Text = "Would not open: " + string.Join(", ", failed);
        }
    }

    private void CloseInputs()
    {
        foreach (var input in _inputs)
        {
            input.Dispose();
        }

        _inputs.Clear();
    }

    /// <summary>The engine takes notes from any thread; only the keyboard's drawing waits for the window's.</summary>
    private void Listen(MidiKeyboard input)
    {
        input.NoteDown += (note, velocity) =>
        {
            var temperament = _temperament;
            var step = _midiSteps[note] = temperament.StepFor(note, _map);
            _engine?.NoteOn(step, temperament.Frequency(step), velocity);
            Dispatcher.BeginInvoke(() =>
            {
                Keyboard.SetPressed(step, true);
                Status.Text = Describe(step);
            });
        };
        input.NoteUp += note =>
        {
            var step = _midiSteps[note];
            _engine?.NoteOff(step);
            Dispatcher.BeginInvoke(() => Keyboard.SetPressed(step, false));
        };
        input.Sustain += down =>
        {
            _engine?.Sustain(down);
            Dispatcher.BeginInvoke(() => Status.Text = down ? "Sustain down" : "Sustain up");
        };
        input.PitchWheel += wheel =>
        {
            _wheel = wheel;
            var centsPerStep = _temperament.CentsPerStep;
            var cents = PitchBend.Cents(wheel, _bendSteps, centsPerStep);
            _engine?.PitchBend(cents);
            Dispatcher.BeginInvoke(() => Status.Text = $"Bend {cents.ToString("+0.0;−0.0;0", CultureInfo.CurrentCulture)}¢ ({(cents / centsPerStep).ToString("+0.00;−0.00;0", CultureInfo.CurrentCulture)} steps)");
        };
    }

    /// <summary>Each temperament has its own ranges, so switching starts again from its usual one.</summary>
    private void FillBendRange()
    {
        var temperament = _temperament;
        var (ranges, usual) = BendRanges[temperament.StepsPerOctave];
        _tuning = true;
        try
        {
            BendRange.Items.Clear();
            foreach (var steps in ranges)
            {
                var cents = temperament.Cents(steps).ToString("0", CultureInfo.CurrentCulture);
                BendRange.Items.Add(new ComboBoxItem { Content = $"±{steps} ({cents}¢)", Tag = steps });
            }
        }
        finally
        {
            _tuning = false;
        }

        BendRange.SelectedIndex = Array.IndexOf(ranges, usual);
    }

    /// <summary>A new range applies at once, even to a wheel already held away from the centre.</summary>
    private void OnBendRangeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_tuning && BendRange.SelectedItem is ComboBoxItem { Tag: int steps })
        {
            _bendSteps = steps;
            _engine?.PitchBend(PitchBend.Cents(_wheel, steps, _temperament.CentsPerStep));
        }
    }

    private static void FillTie(ComboBox box, int keyFromA)
    {
        foreach (var tie in new[] { Tie.Lower, Tie.Upper })
        {
            var degree = new StandardKeyMap(tie, tie, tie).Degree(keyFromA);
            var cents = Tet27.Cents(degree) - (keyFromA * 100);
            box.Items.Add(new ComboBoxItem
            {
                Content = $"{degree} ({cents.ToString("+0.0;−0.0", CultureInfo.CurrentCulture)}¢)",
                Tag = tie,
            });
        }

        box.SelectedIndex = 1;
    }

    private void OnTieChanged(object sender, SelectionChangedEventArgs e)
    {
        // The boxes are filled one at a time, and each fires as it is.
        if (BTie?.SelectedItem is not ComboBoxItem b || DSharpTie?.SelectedItem is not ComboBoxItem dSharp || GTie?.SelectedItem is not ComboBoxItem g)
        {
            return;
        }

        _map = new StandardKeyMap((Tie)b.Tag, (Tie)dSharp.Tag, (Tie)g.Tag);
        Keyboard.KeyMap = _map;
    }

    private void OnExportScala(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose where to save the Scala files" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var temperament = _temperament;
        var scale = ScalaFiles.ScaleFileName(temperament);
        var mapping = ScalaFiles.StandardMappingFileName(temperament);
        File.WriteAllText(Path.Combine(dialog.FolderName, scale), ScalaFiles.Scale(temperament));
        File.WriteAllText(Path.Combine(dialog.FolderName, mapping), ScalaFiles.StandardMapping(temperament, _map));
        Status.Text = $"Saved {scale} and {mapping} in {dialog.FolderName}.";
    }
}
