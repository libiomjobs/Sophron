using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sophron.Songs;

/// <summary>
/// A song on disk: plain JSON, so it can be read, compared and mended by hand. The format number lets
/// a later Sophron recognise an older file.
/// </summary>
public static class SongFile
{
    public const string Extension = ".sophron";

    public const int Format = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string Write(Song song) => JsonSerializer.Serialize(new Envelope(Format, song), Options);

    /// <summary>Reads a song, or throws <see cref="InvalidDataException"/> for anything that is not one.</summary>
    public static Song Read(string json)
    {
        Envelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<Envelope>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("This is not a Sophron song: " + ex.Message, ex);
        }

        if (envelope?.Song is not { } song)
        {
            throw new InvalidDataException("This is not a Sophron song.");
        }

        if (envelope.Format > Format)
        {
            throw new InvalidDataException("This song was saved by a newer Sophron.");
        }

        if (song.StepsPerOctave <= 0 || song.Tempo <= 0 || song.BeatsPerBar <= 0 || song.Tracks.Count == 0)
        {
            throw new InvalidDataException("This song's tuning or tempo makes no sense.");
        }

        return song;
    }

    private sealed record Envelope(int Format, Song Song);
}
