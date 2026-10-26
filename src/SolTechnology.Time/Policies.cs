namespace SolTechnology.Time;

/// <summary>
/// What to do with a wall time that does not exist in a zone, because the clocks jumped forward over it
/// (02:30 on the night when Europe/Warsaw goes from 02:00 to 03:00).
/// </summary>
public enum GapPolicy
{
    /// <summary>Move forward by the length of the gap: 02:30 becomes 03:30. Default.</summary>
    ShiftForward = 0,

    /// <summary>Throw a <see cref="DateTimeZoneException"/>.</summary>
    Throw = 1
}

/// <summary>
/// What to do with a wall time that exists twice in a zone, because the clocks went back
/// (02:30 on the night when Europe/Warsaw goes from 03:00 back to 02:00).
/// </summary>
public enum OverlapPolicy
{
    /// <summary>The first occurrence, with the larger offset (02:30+02:00). Default.</summary>
    Earlier = 0,

    /// <summary>The second occurrence, with the smaller offset (02:30+01:00).</summary>
    Later = 1,

    /// <summary>Throw a <see cref="DateTimeZoneException"/>.</summary>
    Throw = 2
}

/// <summary>
/// What <see cref="DateTimeZone.Parse(string, OffsetConflict)"/> does when the text has both an offset and a zone,
/// and the offset is not the zone's offset at that moment (<c>2026-07-01T10:00+01:00[Europe/Warsaw]</c>).
/// </summary>
public enum OffsetConflict
{
    /// <summary>Throw a <see cref="FormatException"/>. Default.</summary>
    Throw = 0,

    /// <summary>Trust the offset: keep the instant it gives and show it in the zone (the wall time changes).</summary>
    UseOffset = 1,

    /// <summary>Trust the zone: keep the wall time and find its instant in the zone (the instant changes).</summary>
    UseZone = 2
}
