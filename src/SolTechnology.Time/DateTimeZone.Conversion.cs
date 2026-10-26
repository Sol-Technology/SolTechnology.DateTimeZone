namespace SolTechnology.Time;

public readonly partial struct DateTimeZone
{
    /// <summary>
    /// The instant of <paramref name="value"/> in a fixed-offset zone with its offset. An offset is not a zone: arithmetic
    /// across DST keeps the offset. Use <see cref="FromDateTimeOffset(DateTimeOffset, TimeZoneInfo)"/> when the zone is known.
    /// </summary>
    public static DateTimeZone FromDateTimeOffset(DateTimeOffset value) => FromResolved(value, Zones.FixedOffset(value.Offset));

    /// <summary>The instant of <paramref name="value"/>, seen in <paramref name="zone"/>.</summary>
    public static DateTimeZone FromDateTimeOffset(DateTimeOffset value, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return FromInstant(value.UtcTicks, zone);
    }

    /// <summary>The instant of <paramref name="value"/>, seen in the zone <paramref name="zoneId"/>.</summary>
    public static DateTimeZone FromDateTimeOffset(DateTimeOffset value, string zoneId) => FromDateTimeOffset(value, Zones.Resolve(zoneId));

    /// <summary>
    /// A UTC <see cref="DateTime"/>, seen in <paramref name="zone"/>. <see cref="DateTimeKind.Unspecified"/> is read as UTC
    /// (the method name says so); <see cref="DateTimeKind.Local"/> throws.
    /// </summary>
    public static DateTimeZone FromUtc(DateTime utcDateTime, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (utcDateTime.Kind == DateTimeKind.Local)
        {
            throw new DateTimeZoneException("FromUtc got a DateTime with Kind.Local. Use FromDateTime for local values.");
        }

        return FromInstant(utcDateTime.Ticks, zone);
    }

    /// <summary>
    /// A <see cref="DateTime"/> whose kind says where it is from: <see cref="DateTimeKind.Utc"/> → UTC,
    /// <see cref="DateTimeKind.Local"/> → <see cref="TimeZoneInfo.Local"/>. <see cref="DateTimeKind.Unspecified"/> throws,
    /// because the zone is not known: use <c>UnzonedDateTime.FromDateTime(value).InZone(zone)</c>.
    /// </summary>
    public static DateTimeZone FromDateTime(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => FromInstant(value.Ticks, TimeZoneInfo.Utc),
        // ToUniversalTime keeps the hidden flag that tells the two occurrences of an ambiguous local time apart.
        DateTimeKind.Local => FromInstant(value.ToUniversalTime().Ticks, TimeZoneInfo.Local),
        _ => throw new DateTimeZoneException(
            "The DateTime has Kind.Unspecified, so its zone is not known. " +
            "Use UnzonedDateTime.FromDateTime(value).InZone(zone), or AssumeUtc() if it really is UTC.")
    };

    /// <summary>The current instant, in the local zone of <paramref name="timeProvider"/> (the system clock and zone by default).</summary>
    public static DateTimeZone Now(TimeProvider? timeProvider = null)
    {
        timeProvider ??= TimeProvider.System;
        return FromInstant(timeProvider.GetUtcNow().UtcTicks, timeProvider.LocalTimeZone);
    }

    /// <summary>The current instant in UTC.</summary>
    public static DateTimeZone UtcNow(TimeProvider? timeProvider = null)
    {
        timeProvider ??= TimeProvider.System;
        return FromInstant(timeProvider.GetUtcNow().UtcTicks, TimeZoneInfo.Utc);
    }

    /// <summary>The same instant, seen in <paramref name="zone"/>.</summary>
    public DateTimeZone ToTimeZone(TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return FromInstant(UtcTicks, zone);
    }

    /// <summary>The same instant, seen in the zone <paramref name="zoneId"/>.</summary>
    public DateTimeZone ToTimeZone(string zoneId) => ToTimeZone(Zones.Resolve(zoneId));

    /// <summary>The same instant in UTC.</summary>
    public DateTimeZone ToUtc() => FromResolved(_value.ToUniversalTime(), TimeZoneInfo.Utc);

    /// <summary>The instant and the current offset. The zone is dropped.</summary>
    public DateTimeOffset ToDateTimeOffset() => _value;

    /// <summary>Date and time on the clock in the zone, with the zone dropped on purpose.</summary>
    public UnzonedDateTime ToUnzoned() => new(Date, Time);
}
