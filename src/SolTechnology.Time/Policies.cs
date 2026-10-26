namespace SolTechnology.Time;

/// <summary>
/// How to read a time on the clock that falls into a clock change, when the zone switches its UTC offset
/// (daylight saving time, or a change of the base offset like Samoa in 2011). On such a night a time is either
/// skipped (Europe/Warsaw, 29 March 2026: the clocks go from 02:00 to 03:00, there is no 02:30) or shown twice
/// (25 October 2026: the clocks go from 03:00 back to 02:00, 02:30 happens at +02:00 and again at +01:00).
/// </summary>
public enum ClockChange
{
    /// <summary>
    /// Read the time with the offset from before the change. A skipped 02:30 is read as 02:30+01:00, which the new
    /// clock shows as 03:30+02:00. A repeated 02:30 is the first one, 02:30+02:00. Default; Java and NodaTime do the same.
    /// </summary>
    PreviousOffset = 0,

    /// <summary>
    /// Read the time with the offset from after the change. A skipped 02:30 is read as 02:30+02:00, which the old
    /// clock shows as 01:30+01:00. A repeated 02:30 is the second one, 02:30+01:00.
    /// </summary>
    NewOffset = 1,

    /// <summary>Throw a <see cref="DateTimeZoneException"/> that names the zone and the change.</summary>
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
