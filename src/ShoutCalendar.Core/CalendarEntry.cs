using System.Numerics;

namespace ShoutCalendar.Core;

/// <summary>
/// One harvested shout. <see cref="Time"/> is the first explicit clock time.
/// <see cref="End"/> is set only when the shout states exactly two clock times.
/// </summary>
public sealed record CalendarEntry(
    DateOnly? Date,
    TimeOnly? Time,
    TimeOnly? End,
    int? Ward,
    string? Server,
    string Place,
    string EventText,
    string Sender,
    bool Accepted,
    string Id,
    DateTimeOffset DetectedAt,
    EventRepeat? Repeat = null,
    int Channel = 0,
    bool NoteUpdated = false,
    bool Manual = false,
    string SpeakerWorld = "",
    Vector4? Color = null);
