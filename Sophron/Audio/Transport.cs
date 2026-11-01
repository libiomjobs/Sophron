using Sophron.Songs;

namespace Sophron.Audio;

internal enum TransportState
{
    Stopped,
    Playing,
    Recording,
}

/// <summary>
/// Plays a song and records into it. A thread of its own looks a little ahead of what the listener is
/// hearing and hands the engine everything due in that stretch, each event stamped with the sample it
/// belongs on, so timing does not depend on when this thread happens to wake. Notes played while
/// recording are timed by the same clock, against the sample being heard as they were played, so they
/// land where they were heard to land against the click.
/// </summary>
internal sealed class Transport : IDisposable
{
    /// <summary>Playback notes take ids of their own, so they never stop a note being played by hand on the same step.</summary>
    private const int PlaybackIds = 1 << 24;

    /// <summary>How far ahead of the listener events are sent: well past any wake-up this thread might be late for.</summary>
    private static readonly TimeSpan Lookahead = TimeSpan.FromMilliseconds(120);

    private static readonly TimeSpan Wake = TimeSpan.FromMilliseconds(10);

    private readonly Engine _engine;
    private readonly Lock _gate = new();

    /// <summary>
    /// Every playback note started since playing began, so stopping can end exactly those. A note's
    /// off may have been sent and then cancelled with everything else still waiting, so a note is not
    /// crossed off when its off is sent; stopping a note already stopped does nothing.
    /// </summary>
    private readonly HashSet<int> _sounding = [];

    private Thread? _thread;
    private volatile bool _running;
    private volatile bool _metronome = true;
    private volatile bool _reschedule;

    // Where the song meets the clock: tick _originTick is heard at sample _originSample.
    private long _originSample;
    private long _originTick;
    private double _samplesPerTick;
    private long _lookaheadSamples;
    private long _scheduledTo;
    private long _countInEnd;

    private long _position;
    private Recorder? _recorder;

    public Transport(Engine engine, Song song)
    {
        _engine = engine;
        Song = song;
    }

    public TransportState State { get; private set; }

    /// <summary>The song played and recorded into. It may be swapped only while stopped.</summary>
    public Song Song { get; set; }

    public bool Metronome
    {
        get => _metronome;
        set => _metronome = value;
    }

    /// <summary>Where the listener is, in ticks; negative during a count-in.</summary>
    public long Position => _running ? Now() : _position;

    /// <summary>Whether playing has gone a bar past the last note, and may as well stop.</summary>
    public bool PastEnd => State == TransportState.Playing && Position > Song.Tracks.Max(track => track.End) + Song.TicksPerBar;

    public void Play() => Start(record: false);

    /// <summary>Records from the current position, after a bar of count-in.</summary>
    public void Record() => Start(record: true);

    public void Stop()
    {
        if (!_running)
        {
            return;
        }

        var stopped = Now();
        _running = false;
        _thread?.Join();
        _thread = null;

        _engine.CancelScheduled();
        foreach (var id in _sounding)
        {
            _engine.NoteOff(id);
        }

        _sounding.Clear();
        if (Song.Tracks.Any(track => track.Sustain.Count > 0))
        {
            _engine.Sustain(false);
        }

        if (Song.Tracks.Any(track => track.Bends.Count > 0))
        {
            _engine.PitchBend(0);
        }

        lock (_gate)
        {
            _recorder?.FinishInto(Song.Tracks[0], stopped);
            _recorder = null;
        }

        _position = Math.Max(0, stopped);
        State = TransportState.Stopped;
    }

    /// <summary>Moves where playing or recording will start from; only while stopped.</summary>
    public void Seek(long tick)
    {
        if (!_running)
        {
            _position = Math.Max(0, tick);
        }
    }

    public void NoteDown(int step, float velocity)
    {
        lock (_gate)
        {
            _recorder?.NoteDown(step, velocity, Now());
        }
    }

    public void NoteUp(int step)
    {
        lock (_gate)
        {
            _recorder?.NoteUp(step, Now());
        }
    }

    public void Bend(double cents)
    {
        lock (_gate)
        {
            _recorder?.Bend(cents, Now());
        }
    }

    public void Sustain(bool down)
    {
        lock (_gate)
        {
            _recorder?.Sustain(down, Now());
        }
    }

    /// <summary>What has been recorded so far, held notes drawn out to now, or null when not recording.</summary>
    public List<Note>? RecordingSoFar()
    {
        lock (_gate)
        {
            return _recorder?.NotesSoFar(Now());
        }
    }

    /// <summary>
    /// The notes have been edited while playing: whatever was already sent for the next moment is taken
    /// back and sent again from the notes as they now are, and every playback note sounding is stopped,
    /// since its end may have moved or gone.
    /// </summary>
    public void Reschedule() => _reschedule = _running;

    public void Dispose() => Stop();

    private void Start(bool record)
    {
        if (_running)
        {
            return;
        }

        var rate = _engine.SampleRate;
        _samplesPerTick = Song.SecondsPerTick * rate;
        _lookaheadSamples = (long)(Lookahead.TotalSeconds * rate);
        _countInEnd = _position;
        _originTick = record ? _position - Song.TicksPerBar : _position;
        _originSample = _engine.HeardSample() + _lookaheadSamples;
        _scheduledTo = _originTick;

        lock (_gate)
        {
            _recorder = record ? new Recorder() : null;
        }

        State = record ? TransportState.Recording : TransportState.Playing;
        _running = true;
        _thread = new Thread(Run) { IsBackground = true, Name = "Sophron transport", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    private void Run()
    {
        while (_running)
        {
            if (_reschedule)
            {
                _reschedule = false;
                _engine.CancelScheduled();
                foreach (var id in _sounding)
                {
                    _engine.NoteOff(id);
                }

                _sounding.Clear();
                _scheduledTo = Now();
            }

            var horizon = TickAt(_engine.HeardSample() + _lookaheadSamples);
            if (horizon > _scheduledTo)
            {
                Send(_scheduledTo, horizon);
                _scheduledTo = horizon;
            }

            Thread.Sleep(Wake);
        }
    }

    /// <summary>The click always counts in; after that it plays only if the metronome is on.</summary>
    private void Send(long from, long to)
    {
        var events = new List<SongEvent>(Schedule.Clicks(Song, from, _metronome ? to : Math.Min(to, _countInEnd)));
        foreach (var track in Song.Tracks)
        {
            events.AddRange(Schedule.Events(track, from, to));
        }

        var temperament = Song.Temperament;
        foreach (var e in Schedule.Sorted(events))
        {
            var sample = SampleAt(e.Tick);
            var id = PlaybackIds + e.Step;
            switch (e.Kind)
            {
                case SongEventKind.NoteOn:
                    _engine.NoteOnAt(sample, id, temperament.Frequency(e.Step), (float)e.Value);
                    _sounding.Add(id);
                    break;
                case SongEventKind.NoteOff:
                    _engine.NoteOffAt(sample, id);
                    break;
                case SongEventKind.Bend:
                    _engine.PitchBendAt(sample, e.Value);
                    break;
                case SongEventKind.Sustain:
                    _engine.SustainAt(sample, e.Value != 0);
                    break;
                case SongEventKind.Click:
                    _engine.ClickAt(sample, e.Value != 0);
                    break;
            }
        }
    }

    private long Now() => TickAt(_engine.HeardSample());

    private long TickAt(long sample) => _originTick + (long)Math.Floor((sample - _originSample) / _samplesPerTick);

    private long SampleAt(long tick) => _originSample + (long)Math.Round((tick - _originTick) * _samplesPerTick);
}
