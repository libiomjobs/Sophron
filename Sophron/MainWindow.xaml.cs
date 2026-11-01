using System.ComponentModel;
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
using Sophron.Songs;
using Sophron.Tuning;

namespace Sophron;

public partial class MainWindow : Window
{
    private const float MouseVelocity = 0.8f;

    private readonly Engine? _engine;
    private readonly Transport? _transport;

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

    // Read on the MIDI thread; each is replaced whole, never changed in place. The temperament is the
    // song's, kept here as well so the MIDI thread need not reach into the song.
    private volatile Temperament _temperament;
    private volatile StandardKeyMap _map = StandardKeyMap.Default;

    private Song _song;
    private string? _songPath;
    private bool _unsaved;

    /// <summary>Every edit and take, for undoing and redoing.</summary>
    private readonly EditHistory _history = new();

    /// <summary>
    /// The track as it was when recording began. A take is remembered for undoing only if it added
    /// something, which is known only when it stops. The track's lists are replaced, never changed, so
    /// holding them is enough.
    /// </summary>
    private Track? _beforeTake;

    /// <summary>Notes heard while pressed or dragged in the roll take an id of their own, apart from playing and playback.</summary>
    private const int AuditionId = 1 << 25;
    private const float AuditionVelocity = 0.7f;
    private int? _auditionStep;

    /// <summary>The grids notes can snap to, in ticks; the bar's depends on the beats in it, and 0 is none.</summary>
    private const long BarGrid = -1;
    private static readonly (string Name, long Ticks)[] Grids =
    [
        ("Bar", BarGrid),
        ("1/2", Song.TicksPerBeat * 2),
        ("1/4", Song.TicksPerBeat),
        ("1/8", Song.TicksPerBeat / 2),
        ("1/16", Song.TicksPerBeat / 4),
        ("1/32", Song.TicksPerBeat / 8),
        ("1/4 triplet", Song.TicksPerBeat * 2 / 3),
        ("1/8 triplet", Song.TicksPerBeat / 3),
        ("1/16 triplet", Song.TicksPerBeat / 6),
        ("Off", 0),
    ];

    /// <summary>Set while the velocity slider's thumb is being dragged, so the whole drag is one edit to undo.</summary>
    private bool _velocityDragging;

    /// <summary>Set while the velocity slider applies a curve left in the lane, so the slider is not moved under itself.</summary>
    private bool _sliding;

    /// <summary>
    /// How far the pitch wheel reaches, in steps, and where it starts: a whole tone in 12-TET, and in
    /// 27-TET one white key to the next.
    /// </summary>
    private static readonly Dictionary<int, (int[] Ranges, int Default)> BendRanges = new()
    {
        [StandardKeyMap.KeysPerOctave] = ([1, 2, 3, 5, 7, 12], 2),
        [Tet27.StepsPerOctave] = ([1, 2, 3, 6, 9, 27], 3),
    };

    private static readonly int[] BeatsPerBarChoices = [2, 3, 4, 5, 6, 7];
    private const double SlowestTempo = 20;
    private const double FastestTempo = 300;

    // The wheel and its range are read on the MIDI thread, and each is a single int, read whole.
    private volatile int _bendSteps;
    private volatile int _wheel = PitchBend.Centre;

    private const int WmDeviceChange = 0x0219;
    private static readonly TimeSpan DeviceSettleTime = TimeSpan.FromSeconds(1.5);
    private readonly DispatcherTimer _devicesSettled;
    private bool _refreshing;

    /// <summary>Moves the playhead about thirty times a second while the transport runs.</summary>
    private readonly DispatcherTimer _playheadTimer;

    /// <summary>Set while the controls are being brought into line, so they do not answer themselves.</summary>
    private bool _tuning = true;

    public MainWindow()
    {
        InitializeComponent();

        var steps = Preferences.StepsPerOctave is StandardKeyMap.KeysPerOctave ? StandardKeyMap.KeysPerOctave : Tet27.StepsPerOctave;
        var fundamental = Math.Clamp(Preferences.FundamentalHz ?? Temperament.Fundamental432, Temperament.LowestFundamentalHz, Temperament.HighestFundamentalHz);
        _song = new Song { StepsPerOctave = steps, FundamentalHz = fundamental };
        _temperament = _song.Temperament;

        _engine = Engine.TryStart(out var problem);
        Status.Text = problem ?? $"Engine running at {_engine!.SampleRate.ToString("0", CultureInfo.CurrentCulture)} Hz.";
        _transport = _engine is null ? null : new Transport(_engine, _song);

        FillTie(BTie, 2);
        FillTie(DSharpTie, 6);
        FillTie(GTie, 10);

        Temperaments.Items.Add(new ComboBoxItem { Content = "12-TET", Tag = StandardKeyMap.KeysPerOctave });
        Temperaments.Items.Add(new ComboBoxItem { Content = "27-TET", Tag = Tet27.StepsPerOctave });
        foreach (var beats in BeatsPerBarChoices)
        {
            BeatsPerBarBox.Items.Add(beats);
        }

        foreach (var (name, ticks) in Grids)
        {
            SnapBox.Items.Add(new ComboBoxItem { Content = name, Tag = ticks });
        }

        SnapBox.SelectedIndex = Array.FindIndex(Grids, grid => grid.Name == "1/16");

        FundamentalSlider.Minimum = Temperament.LowestFundamentalHz;
        FundamentalSlider.Maximum = Temperament.HighestFundamentalHz;
        _tuning = false;
        ShowSong();

        _playheadTimer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher) { Interval = TimeSpan.FromMilliseconds(33) };
        _playheadTimer.Tick += (_, _) => FollowTransport();

        _devicesSettled = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = DeviceSettleTime };
        _devicesSettled.Tick += (_, _) =>
        {
            _devicesSettled.Stop();
            RefreshInputs();
        };

        RefreshInputs();
        ShowTransport();
        Loaded += (_, _) =>
        {
            ScrollToA4();
            Roll.CentreOnStep(4 * _temperament.StepsPerOctave);
        };
    }

    /// <summary>Windows tells every top-level window when devices come and go, without being asked.</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(OnWindowMessage);
    }

    protected override void OnClosed(EventArgs e)
    {
        _playheadTimer.Stop();
        _devicesSettled.Stop();
        _transport?.Dispose();
        CloseInputs();
        _engine?.Dispose();
        Preferences.StepsPerOctave = _temperament.StepsPerOctave;
        Preferences.FundamentalHz = _temperament.FundamentalHz;
        base.OnClosed(e);
    }

    private void OnClosing(object? sender, CancelEventArgs e) => e.Cancel = !MayDiscardSong();

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
        _transport?.NoteDown(step, velocity);
        Keyboard.SetPressed(step, true);
        Status.Text = Describe(step);
    }

    private void Stop(int step)
    {
        _engine?.NoteOff(step);
        _transport?.NoteUp(step);
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

    /// <summary>Brings every control into line with the song, as when one is opened.</summary>
    private void ShowSong()
    {
        _tuning = true;
        try
        {
            Temperaments.SelectedIndex = _song.StepsPerOctave == StandardKeyMap.KeysPerOctave ? 0 : 1;
            BeatsPerBarBox.SelectedItem = _song.BeatsPerBar;
        }
        finally
        {
            _tuning = false;
        }

        _temperament = _song.Temperament;
        ShowTemperament();
        ShowTempo();
        Roll.BeatsPerBar = _song.BeatsPerBar;
        Roll.Notes = _song.Tracks[0].Notes;
        ApplyGrid();
        Roll.Playhead = _transport?.Position ?? 0;
        ShowPosition(Roll.Playhead);
        ShowTitle();
    }

    private void ShowTitle()
    {
        var name = _songPath is null ? "Untitled" : Path.GetFileNameWithoutExtension(_songPath);
        Title = $"{name}{(_unsaved ? " •" : "")} — Sophron";
    }

    /// <summary>
    /// Steps mean something else in the other temperament, so a song with notes keeps the one it was
    /// played in. An empty one may switch freely; every sounding note is let go first rather than left
    /// at a pitch nothing will ever stop.
    /// </summary>
    private void OnTemperamentChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_tuning || Temperaments.SelectedItem is not ComboBoxItem { Tag: int steps } || steps == _temperament.StepsPerOctave)
        {
            return;
        }

        if (!_song.IsEmpty || _transport?.State is TransportState.Playing or TransportState.Recording)
        {
            Status.Text = _song.IsEmpty
                ? "Stop first, then change the tuning."
                : $"This song is played in {_song.StepsPerOctave.ToString(CultureInfo.CurrentCulture)}-TET; its notes would mean something else in another. Start a new song to switch.";
            _tuning = true;
            Temperaments.SelectedIndex = _song.StepsPerOctave == StandardKeyMap.KeysPerOctave ? 0 : 1;
            _tuning = false;
            return;
        }

        _engine?.AllNotesOff();
        Keyboard.ReleaseAll();
        _song.StepsPerOctave = steps;
        _temperament = _song.Temperament;
        ShowTemperament();
        ScrollToA4();
        Roll.CentreOnStep(4 * steps);
    }

    /// <summary>Brings the keyboard, the roll, the tuner, the bend ranges and the ties into line with the temperament.</summary>
    private void ShowTemperament()
    {
        var temperament = _temperament;
        Keyboard.Temperament = temperament;
        Roll.Temperament = temperament;
        Ties.Visibility = temperament.IsStandard ? Visibility.Collapsed : Visibility.Visible;
        FillBendRange();
        ShowFundamental();
    }

    /// <summary>
    /// Retunes everything, notes already sounding included, so a chord can be held while A0 is moved
    /// to hear what it does. The song takes the new fundamental with it.
    /// </summary>
    private void SetFundamental(double hertz)
    {
        hertz = Math.Clamp(hertz, Temperament.LowestFundamentalHz, Temperament.HighestFundamentalHz);
        var old = _temperament;
        if (hertz != old.FundamentalHz)
        {
            _song.FundamentalHz = hertz;
            _temperament = _song.Temperament;
            _engine?.Retune(hertz / old.FundamentalHz);
            Keyboard.Temperament = _temperament;
            Roll.Temperament = _temperament;
            MarkUnsaved();
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

    private void OnPlay(object sender, RoutedEventArgs e)
    {
        _transport?.Play();
        StartFollowing();
    }

    /// <summary>Keeps the track as it was, so the take can be undone if it went wrong. Notes cannot be edited while recording.</summary>
    private void OnRecord(object sender, RoutedEventArgs e)
    {
        if (_transport is null || _transport.State != TransportState.Stopped)
        {
            return;
        }

        Roll.CommitCurve();
        var track = _song.Tracks[0];
        _beforeTake = new Track { Notes = track.Notes, Bends = track.Bends, Sustain = track.Sustain };
        Roll.Editable = false;
        _transport.Record();
        StartFollowing();
    }

    private void OnStop(object sender, RoutedEventArgs e) => StopTransport();

    private void OnRewind(object sender, RoutedEventArgs e) => Seek(0);

    private void OnPositionChosen(object? sender, TickEventArgs e) => Seek(e.Tick);

    private void Seek(long tick)
    {
        if (_transport?.State == TransportState.Stopped)
        {
            _transport.Seek(tick);
            Roll.Playhead = _transport.Position;
            ShowPosition(Roll.Playhead);
        }
    }

    /// <summary>
    /// An edit made in the roll: the song takes the new notes, the edit is remembered for undoing, and
    /// if the song is playing, what was already sent ahead is sent again from the notes as they now are.
    /// </summary>
    private void OnNotesEdited(object? sender, NotesEditedEventArgs e)
    {
        var track = _song.Tracks[0];
        _history.Before(track, e.Name);
        track.Notes = e.Notes;

        // A new velocity matters only to notes not yet sent, so playing goes on undisturbed.
        if (!e.Name.StartsWith("Velocity", StringComparison.Ordinal))
        {
            _transport?.Reschedule();
        }

        MarkUnsaved();
        ShowHistory();
        if (!_sliding)
        {
            OnRollSelectionChanged(sender, e);
        }
    }

    private void OnUndo(object sender, RoutedEventArgs e) => Undo();

    private void OnRedo(object sender, RoutedEventArgs e) => Redo();

    private void Undo()
    {
        if (_transport?.State == TransportState.Recording || _history.Undo(_song.Tracks[0]) is not { } name)
        {
            return;
        }

        AfterHistory();
        Status.Text = $"Undid {name.ToLowerInvariant()}.";
    }

    private void Redo()
    {
        if (_transport?.State == TransportState.Recording || _history.Redo(_song.Tracks[0]) is not { } name)
        {
            return;
        }

        AfterHistory();
        Status.Text = $"Redid {name.ToLowerInvariant()}.";
    }

    private void AfterHistory()
    {
        Roll.Notes = _song.Tracks[0].Notes;
        _transport?.Reschedule();
        MarkUnsaved();
        ShowHistory();
    }

    private void ShowHistory()
    {
        var recording = _transport?.State == TransportState.Recording;
        UndoButton.IsEnabled = !recording && _history.CanUndo;
        RedoButton.IsEnabled = !recording && _history.CanRedo;
        UndoButton.ToolTip = _history.UndoName is { } undo ? $"Undo {undo.ToLowerInvariant()} (Ctrl+Z)" : "Undo (Ctrl+Z)";
        RedoButton.ToolTip = _history.RedoName is { } redo ? $"Redo {redo.ToLowerInvariant()} (Ctrl+Y)" : "Redo (Ctrl+Y)";
    }

    /// <summary>
    /// Space plays and stops; Ctrl+Z and Ctrl+Y undo and redo; P switches drawing on and off; and the
    /// roll's own keys go to the roll, wherever the focus is. None of it while something is being typed
    /// or a list or slider is being worked with the keys.
    /// </summary>
    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox or ComboBox or ComboBoxItem or Slider)
        {
            return;
        }

        var modifiers = e.KeyboardDevice.Modifiers;
        var control = modifiers.HasFlag(ModifierKeys.Control);
        switch (e.Key)
        {
            case Key.Space when _transport is not null:
                if (_transport.State == TransportState.Stopped)
                {
                    OnPlay(sender, e);
                }
                else
                {
                    StopTransport();
                }

                break;
            case Key.Z when control && modifiers.HasFlag(ModifierKeys.Shift):
            case Key.Y when control:
                Redo();
                break;
            case Key.Z when control:
                Undo();
                break;
            case Key.P when modifiers == ModifierKeys.None:
                DrawButton.IsChecked = DrawButton.IsChecked != true;
                break;
            default:
                if (!Roll.HandleKey(e.Key, modifiers))
                {
                    return;
                }

                break;
        }

        e.Handled = true;
    }

    private void OnDrawModeChanged(object sender, RoutedEventArgs e) => Roll.DrawMode = DrawButton.IsChecked == true;

    private void OnQuantise(object sender, RoutedEventArgs e) => Roll.Quantise();

    // Both fire while the window is still being built, before the roll is there.
    private void OnLaneShownChanged(object sender, RoutedEventArgs e)
    {
        if (Roll is not null)
        {
            Roll.LaneShown = LaneButton.IsChecked == true;
            LaneToolBox.IsEnabled = Roll.LaneShown;
        }
    }

    private void OnLaneToolChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Roll is not null)
        {
            Roll.LaneTool = LaneToolBox.SelectedIndex == 1 ? LaneTool.Curve : LaneTool.Freehand;
        }
    }

    private void OnSnapChanged(object sender, SelectionChangedEventArgs e) => ApplyGrid();

    /// <summary>The bar's grid follows the beats in a bar, so it is worked out afresh whenever they change.</summary>
    private void ApplyGrid()
    {
        if (SnapBox.SelectedItem is ComboBoxItem { Tag: long ticks })
        {
            Roll.Grid = ticks == BarGrid ? _song.TicksPerBar : ticks;
            QuantiseButton.IsEnabled = ticks != 0;
        }
    }

    private void OnZoom(object sender, RoutedEventArgs e)
    {
        const double factor = 1.5;
        switch ((sender as Button)?.Tag)
        {
            case "TimeIn":
                Roll.ZoomTime(factor);
                break;
            case "TimeOut":
                Roll.ZoomTime(1 / factor);
                break;
            case "StepsIn":
                Roll.ZoomSteps(factor);
                break;
            case "StepsOut":
                Roll.ZoomSteps(1 / factor);
                break;
        }
    }

    /// <summary>Shows how hard the chosen notes are played: the loudest of them, if they differ.</summary>
    private void OnRollSelectionChanged(object? sender, EventArgs e)
    {
        var chosen = Roll.Selected;
        var notes = Roll.Notes;
        _tuning = true;
        try
        {
            VelocitySlider.IsEnabled = chosen.Count > 0;
            if (chosen.Count == 0)
            {
                VelocityText.Text = "";
                return;
            }

            var velocities = chosen.Select(index => notes[index].Velocity).ToList();
            VelocitySlider.Value = Math.Round(velocities.Max() * 127);
            VelocityText.Text = velocities.Min() == velocities.Max()
                ? VelocitySlider.Value.ToString("0", CultureInfo.CurrentCulture)
                : "mixed";
        }
        finally
        {
            _tuning = false;
        }
    }

    private void OnVelocityDragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
    {
        _history.Before(_song.Tracks[0], "Velocity");
        _velocityDragging = true;
        ShowHistory();
    }

    private void OnVelocityDragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) => _velocityDragging = false;

    /// <summary>
    /// Sets every chosen note to the slider's velocity. Notes already sent ahead keep the velocity they
    /// went with, which is a moment's worth at most, rather than cutting off whatever is sounding.
    /// </summary>
    private void OnVelocitySlid(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_tuning || Roll.Selected.Count == 0)
        {
            return;
        }

        // A curve left in the lane goes in first, as its own edit.
        _sliding = true;
        Roll.CommitCurve();
        _sliding = false;

        var track = _song.Tracks[0];
        if (!_velocityDragging)
        {
            _history.Before(track, "Velocity");
        }

        track.Notes = NoteEditing.SetVelocity(track.Notes, Roll.Selected, (float)(e.NewValue / 127));
        Roll.Update(track.Notes);
        VelocityText.Text = e.NewValue.ToString("0", CultureInfo.CurrentCulture);
        MarkUnsaved();
        ShowHistory();
    }

    private void OnAuditionStarted(object? sender, StepEventArgs e)
    {
        OnAuditionStopped(sender, e);
        _auditionStep = e.Step;
        _engine?.NoteOn(AuditionId, _temperament.Frequency(e.Step), AuditionVelocity);
        Keyboard.SetPressed(e.Step, true);
        Status.Text = Describe(e.Step);
    }

    private void OnAuditionStopped(object? sender, EventArgs e)
    {
        if (_auditionStep is { } step)
        {
            _engine?.NoteOff(AuditionId);
            Keyboard.SetPressed(step, false);
            _auditionStep = null;
        }
    }

    private void StartFollowing()
    {
        _playheadTimer.Start();
        ShowTransport();
        FollowTransport();
    }

    private void StopTransport()
    {
        if (_transport is null || _transport.State == TransportState.Stopped)
        {
            return;
        }

        var recorded = _transport.State == TransportState.Recording;
        var before = _song.Tracks[0].Notes.Count;
        _transport.Stop();
        _playheadTimer.Stop();

        // After only playing, the notes are the same list, and the roll keeps the chosen ones chosen.
        var track = _song.Tracks[0];
        Roll.Recording = null;
        Roll.Notes = track.Notes;
        Roll.Editable = true;
        Roll.Playhead = _transport.Position;
        ShowPosition(Roll.Playhead);
        if (recorded && _beforeTake is { } take)
        {
            var added = track.Notes.Count - before;
            Status.Text = added == 0 ? "Nothing was recorded." : $"Recorded {added.ToString(CultureInfo.CurrentCulture)} note{(added == 1 ? "" : "s")}.";
            if (added > 0 || track.Bends.Count != take.Bends.Count || track.Sustain.Count != take.Sustain.Count)
            {
                _history.Before(take, "Recording");
                MarkUnsaved();
            }

            _beforeTake = null;
        }

        ShowTransport();
    }

    private void FollowTransport()
    {
        if (_transport is null)
        {
            return;
        }

        if (_transport.PastEnd)
        {
            StopTransport();
            return;
        }

        var position = _transport.Position;
        Roll.Playhead = Math.Max(0, position);
        Roll.Recording = _transport.RecordingSoFar();
        Roll.KeepPlayheadInView();
        ShowPosition(position);
    }

    /// <summary>Bar, beat and sixteenth, counted from 1 as musicians count; the count-in shows as such.</summary>
    private void ShowPosition(long tick)
    {
        if (tick < 0)
        {
            PositionText.Text = "Count-in";
            return;
        }

        var bar = (tick / _song.TicksPerBar) + 1;
        var beat = (tick % _song.TicksPerBar / Song.TicksPerBeat) + 1;
        var sixteenth = (tick % Song.TicksPerBeat / (Song.TicksPerBeat / 4)) + 1;
        PositionText.Text = string.Create(CultureInfo.CurrentCulture, $"{bar}.{beat}.{sixteenth}");
    }

    /// <summary>The song's shape is fixed while it plays: the tempo, the bar and the file can change only when stopped.</summary>
    private void ShowTransport()
    {
        var state = _transport?.State ?? TransportState.Stopped;
        var stopped = state == TransportState.Stopped;
        var available = _transport is not null;
        PlayButton.IsEnabled = available && stopped;
        RecordButton.IsEnabled = available && stopped;
        StopButton.IsEnabled = !stopped;
        RewindButton.IsEnabled = available && stopped;
        ShowHistory();
        TempoText.IsEnabled = stopped;
        BeatsPerBarBox.IsEnabled = stopped;
        RecordButton.Content = state == TransportState.Recording ? "●  Recording" : "●  Record";
    }

    private void ShowTempo() => TempoText.Text = _song.Tempo.ToString("0.##", CultureInfo.CurrentCulture);

    private void OnTempoKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnTempoTyped(sender, e);
            e.Handled = true;
        }
    }

    private void OnTempoTyped(object sender, RoutedEventArgs e)
    {
        if (double.TryParse(TempoText.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var tempo))
        {
            tempo = Math.Clamp(tempo, SlowestTempo, FastestTempo);
            if (tempo != _song.Tempo)
            {
                _song.Tempo = tempo;
                MarkUnsaved();
            }
        }

        ShowTempo();
    }

    private void OnBeatsPerBarChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_tuning || BeatsPerBarBox.SelectedItem is not int beats || beats == _song.BeatsPerBar)
        {
            return;
        }

        _song.BeatsPerBar = beats;
        Roll.BeatsPerBar = beats;
        ApplyGrid();
        ShowPosition(Roll.Playhead);
        MarkUnsaved();
    }

    private void OnMetronomeChanged(object sender, RoutedEventArgs e)
    {
        if (_transport is not null)
        {
            _transport.Metronome = MetronomeBox.IsChecked == true;
        }
    }

    private void MarkUnsaved()
    {
        _unsaved = true;
        ShowTitle();
    }

    /// <summary>Asks before unsaved work is thrown away, offering to save it; false if the user would rather keep it.</summary>
    private bool MayDiscardSong()
    {
        if (!_unsaved)
        {
            return true;
        }

        var answer = MessageBox.Show(this, "Save the changes to this song first?", "Sophron", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return answer switch
        {
            MessageBoxResult.Yes => SaveSong(_songPath),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    /// <summary>A new song starts in the tuning in use, so whoever is experimenting with A0 keeps it.</summary>
    private void OnNewSong(object sender, RoutedEventArgs e)
    {
        StopTransport();
        if (!MayDiscardSong())
        {
            return;
        }

        UseSong(new Song { StepsPerOctave = _temperament.StepsPerOctave, FundamentalHz = _temperament.FundamentalHz }, null);
        Status.Text = "New song.";
    }

    private void OnOpenSong(object sender, RoutedEventArgs e)
    {
        StopTransport();
        if (!MayDiscardSong())
        {
            return;
        }

        var dialog = new OpenFileDialog { Filter = $"Sophron songs (*{SongFile.Extension})|*{SongFile.Extension}" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            UseSong(SongFile.Read(File.ReadAllText(dialog.FileName)), dialog.FileName);
            Status.Text = $"Opened {Path.GetFileName(dialog.FileName)}.";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Status.Text = $"Could not open {Path.GetFileName(dialog.FileName)}: {ex.Message}";
        }
    }

    /// <summary>A song's tuning may lie outside the tuner's reach if written by hand; it is kept, and the tuner shows its nearest.</summary>
    private void UseSong(Song song, string? path)
    {
        _engine?.AllNotesOff();
        Keyboard.ReleaseAll();
        if (_engine is not null && song.FundamentalHz != _temperament.FundamentalHz)
        {
            _engine.Retune(song.FundamentalHz / _temperament.FundamentalHz);
        }

        _song = song;
        _songPath = path;
        _unsaved = false;
        _beforeTake = null;
        _history.Clear();
        if (_transport is not null)
        {
            _transport.Song = song;
            _transport.Seek(0);
        }

        ShowSong();
        ShowTransport();
        ScrollToA4();
        Roll.SetHorizontalOffset(0);
        Roll.CentreOnStep(4 * song.StepsPerOctave);
    }

    private void OnSaveSong(object sender, RoutedEventArgs e) => SaveSong(_songPath);

    private void OnSaveSongAs(object sender, RoutedEventArgs e) => SaveSong(null);

    /// <summary>Saves to <paramref name="path"/>, or asks where if there is none; false if not saved.</summary>
    private bool SaveSong(string? path)
    {
        Roll.CommitCurve();
        if (path is null)
        {
            var dialog = new SaveFileDialog
            {
                Filter = $"Sophron songs (*{SongFile.Extension})|*{SongFile.Extension}",
                DefaultExt = SongFile.Extension,
                FileName = _songPath is null ? "Untitled" : Path.GetFileName(_songPath),
            };
            if (dialog.ShowDialog(this) != true)
            {
                return false;
            }

            path = dialog.FileName;
        }

        try
        {
            File.WriteAllText(path, SongFile.Write(_song));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status.Text = $"Could not save {Path.GetFileName(path)}: {ex.Message}";
            return false;
        }

        _songPath = path;
        _unsaved = false;
        ShowTitle();
        Status.Text = $"Saved {Path.GetFileName(path)}.";
        return true;
    }

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

    /// <summary>
    /// The engine and the transport take notes from any thread; only the keyboard's drawing waits for
    /// the window's. The transport is told at once, so a recorded note is timed as it was played, not
    /// as the window got round to it.
    /// </summary>
    private void Listen(MidiKeyboard input)
    {
        input.NoteDown += (note, velocity) =>
        {
            var temperament = _temperament;
            var step = _midiSteps[note] = temperament.StepFor(note, _map);
            _engine?.NoteOn(step, temperament.Frequency(step), velocity);
            _transport?.NoteDown(step, velocity);
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
            _transport?.NoteUp(step);
            Dispatcher.BeginInvoke(() => Keyboard.SetPressed(step, false));
        };
        input.Sustain += down =>
        {
            _engine?.Sustain(down);
            _transport?.Sustain(down);
            Dispatcher.BeginInvoke(() => Status.Text = down ? "Sustain down" : "Sustain up");
        };
        input.PitchWheel += wheel =>
        {
            _wheel = wheel;
            var centsPerStep = _temperament.CentsPerStep;
            var cents = PitchBend.Cents(wheel, _bendSteps, centsPerStep);
            _engine?.PitchBend(cents);
            _transport?.Bend(cents);
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
