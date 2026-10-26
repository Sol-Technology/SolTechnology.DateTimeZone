namespace SolTechnology.Time;

/// <summary>
/// The only place that turns a wall time (date + time on a clock in a zone) into an instant.
/// It does not use <see cref="TimeZoneInfo.ConvertTimeToUtc(DateTime, TimeZoneInfo)"/> (throws on gaps, picks standard
/// time on overlaps) or <see cref="TimeZoneInfo.IsInvalidTime"/> (misses changes of the base offset, such as
/// Pacific/Apia skipping 30 December 2011).
/// </summary>
internal static class ZoneResolver
{
    private const long MaxTicks = 3155378975999999999; // DateTime.MaxValue.Ticks

    /// <summary>Offset of <paramref name="zone"/> at the instant <paramref name="utcTicks"/>.</summary>
    public static TimeSpan OffsetAt(TimeZoneInfo zone, long utcTicks)
    {
        if (Zones.IsUtc(zone) || Zones.IsFixed(zone))
        {
            return zone.BaseUtcOffset;
        }

        return zone.GetUtcOffset(new DateTime(Math.Clamp(utcTicks, 0, MaxTicks), DateTimeKind.Utc));
    }

    /// <summary>The instant of a wall time in a zone, as a <see cref="DateTimeOffset"/> with the zone's offset.</summary>
    public static DateTimeOffset Resolve(DateTime wall, TimeZoneInfo zone, GapPolicy gap, OverlapPolicy overlap)
    {
        var wallTicks = wall.Ticks;
        if (Zones.IsUtc(zone) || Zones.IsFixed(zone))
        {
            return Create(wallTicks, zone.BaseUtcOffset, zone);
        }

        // Offsets a day before, at and a day after the wall time (read as if it were UTC). Any offset valid for this wall
        // time is among them, because offsets are within ±14 h and a zone changes its offset at most once in a day.
        var before = OffsetAt(zone, wallTicks - TimeSpan.TicksPerDay);
        var at = OffsetAt(zone, wallTicks);
        var after = OffsetAt(zone, wallTicks + TimeSpan.TicksPerDay);

        TimeSpan? earlier = null;
        TimeSpan? later = null;
        foreach (var candidate in (ReadOnlySpan<TimeSpan>)[before, at, after])
        {
            if (OffsetAt(zone, wallTicks - candidate.Ticks) != candidate)
            {
                continue;
            }

            if (earlier is null || candidate > earlier)
            {
                earlier = candidate;
            }

            if (later is null || candidate < later)
            {
                later = candidate;
            }
        }

        if (earlier is null || later is null)
        {
            if (gap == GapPolicy.Throw)
            {
                throw new DateTimeZoneException($"{Describe(wall)} does not exist in {zone.Id}: the clocks skip it ({Format(before)} → {Format(after)}).");
            }

            // Shift forward by the length of the gap: read the wall time with the offset from before the gap.
            var utcTicks = wallTicks - before.Ticks;
            return Create(utcTicks + OffsetAt(zone, utcTicks).Ticks, OffsetAt(zone, utcTicks), zone);
        }

        if (earlier == later)
        {
            return Create(wallTicks, earlier.Value, zone);
        }

        return overlap switch
        {
            OverlapPolicy.Earlier => Create(wallTicks, earlier.Value, zone),
            OverlapPolicy.Later => Create(wallTicks, later.Value, zone),
            _ => throw new DateTimeZoneException($"{Describe(wall)} is ambiguous in {zone.Id}: it occurs at {Format(earlier.Value)} and again at {Format(later.Value)}.")
        };
    }

    /// <summary>A <see cref="DateTimeOffset"/> for local ticks and an offset, with a range check that names the zone.</summary>
    public static DateTimeOffset Create(long localTicks, TimeSpan offset, TimeZoneInfo zone)
    {
        var utcTicks = localTicks - offset.Ticks;
        if (localTicks < 0 || localTicks > MaxTicks || utcTicks < 0 || utcTicks > MaxTicks)
        {
            throw new DateTimeZoneException($"The value is outside the range of DateTime when shown in {zone.Id} (offset {Format(offset)}).");
        }

        return new DateTimeOffset(localTicks, offset);
    }

    private static string Describe(DateTime wall) => wall.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", System.Globalization.CultureInfo.InvariantCulture).TrimEnd('.');

    private static string Format(TimeSpan offset) => (offset < TimeSpan.Zero ? "-" : "+") + offset.Duration().ToString(@"hh\:mm", System.Globalization.CultureInfo.InvariantCulture);
}
