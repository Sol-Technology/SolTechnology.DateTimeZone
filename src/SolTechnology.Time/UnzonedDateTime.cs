using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using SolTechnology.Time.Serialization;

namespace SolTechnology.Time;

/// <summary>
/// A date and time on a clock, without a recorded zone: what Avro <c>local-timestamp-*</c>, Iceberg <c>timestamp</c>
/// and Parquet <c>isAdjustedToUTC=false</c> store. It is not an instant: it becomes one only with a zone
/// (<see cref="InZone(TimeZoneInfo, ClockChange)"/>) or by an explicit <see cref="AssumeUtc"/>.
/// </summary>
/// <param name="Date">The date.</param>
/// <param name="Time">The time.</param>
[JsonConverter(typeof(UnzonedDateTimeJsonConverter))]
public readonly record struct UnzonedDateTime(DateOnly Date, TimeOnly Time)
    : IComparable<UnzonedDateTime>, IComparable, ISpanFormattable, ISpanParsable<UnzonedDateTime>
{
    /// <summary>Creates the value from its parts.</summary>
    public UnzonedDateTime(int year, int month, int day, int hour = 0, int minute = 0, int second = 0, int millisecond = 0)
        : this(new DateOnly(year, month, day), new TimeOnly(hour, minute, second, millisecond))
    {
    }

    /// <summary>The date and time of <paramref name="value"/>; its <see cref="DateTime.Kind"/> is dropped on purpose.</summary>
    public static UnzonedDateTime FromDateTime(DateTime value) => new(DateOnly.FromDateTime(value), TimeOnly.FromDateTime(value));

    /// <summary>A <see cref="DateTime"/> with <see cref="DateTimeKind.Unspecified"/>.</summary>
    public DateTime ToDateTime() => Date.ToDateTime(Time, DateTimeKind.Unspecified);

    /// <summary>The instant at which the clocks in <paramref name="zone"/> show this date and time.</summary>
    public DateTimeZone InZone(TimeZoneInfo zone, ClockChange clockChange = ClockChange.PreviousOffset) =>
        new(Date, Time, zone, clockChange);

    /// <summary>The instant at which the clocks in <paramref name="zone"/> show this date and time.</summary>
    public DateTimeZone InZone(Zone zone, ClockChange clockChange = ClockChange.PreviousOffset) =>
        new(Date, Time, Zones.Resolve(zone), clockChange);

    /// <summary>Reads this date and time as UTC. Only when you know it is.</summary>
    public DateTimeZone AssumeUtc() => DateTimeZone.FromInstant(ToDateTime().Ticks, TimeZoneInfo.Utc);

    /// <summary>Adds years (29 February → 28 February).</summary>
    public UnzonedDateTime AddYears(int years) => FromDateTime(ToDateTime().AddYears(years));

    /// <summary>Adds months; the day is clamped to the end of the month.</summary>
    public UnzonedDateTime AddMonths(int months) => FromDateTime(ToDateTime().AddMonths(months));

    /// <summary>Adds days.</summary>
    public UnzonedDateTime AddDays(int days) => FromDateTime(ToDateTime().AddDays(days));

    /// <summary>Adds a time span. There is no zone, so there are no DST gaps.</summary>
    public UnzonedDateTime Add(TimeSpan value) => FromDateTime(ToDateTime().Add(value));

    /// <summary>Adds hours.</summary>
    public UnzonedDateTime AddHours(double hours) => FromDateTime(ToDateTime().AddHours(hours));

    /// <summary>Adds minutes.</summary>
    public UnzonedDateTime AddMinutes(double minutes) => FromDateTime(ToDateTime().AddMinutes(minutes));

    /// <summary>Adds seconds.</summary>
    public UnzonedDateTime AddSeconds(double seconds) => FromDateTime(ToDateTime().AddSeconds(seconds));

    /// <summary>Adds milliseconds.</summary>
    public UnzonedDateTime AddMilliseconds(double milliseconds) => FromDateTime(ToDateTime().AddMilliseconds(milliseconds));

    /// <summary>Adds ticks.</summary>
    public UnzonedDateTime AddTicks(long ticks) => FromDateTime(ToDateTime().AddTicks(ticks));

    /// <summary>Compares the clocks (there is no zone, so this is not a comparison of instants).</summary>
    public int CompareTo(UnzonedDateTime other) => ToDateTime().CompareTo(other.ToDateTime());

    int IComparable.CompareTo(object? obj) => obj switch
    {
        null => 1,
        UnzonedDateTime other => CompareTo(other),
        _ => throw new ArgumentException($"Object must be of type {nameof(UnzonedDateTime)}.", nameof(obj))
    };

    /// <summary>Difference between the clocks.</summary>
    public static TimeSpan operator -(UnzonedDateTime left, UnzonedDateTime right) => left.ToDateTime() - right.ToDateTime();

    /// <summary>Adds a time span.</summary>
    public static UnzonedDateTime operator +(UnzonedDateTime left, TimeSpan right) => left.Add(right);

    /// <summary>Subtracts a time span.</summary>
    public static UnzonedDateTime operator -(UnzonedDateTime left, TimeSpan right) => left.Add(-right);

    /// <summary>Earlier on the clock.</summary>
    public static bool operator <(UnzonedDateTime left, UnzonedDateTime right) => left.CompareTo(right) < 0;

    /// <summary>Later on the clock.</summary>
    public static bool operator >(UnzonedDateTime left, UnzonedDateTime right) => left.CompareTo(right) > 0;

    /// <summary>Earlier or the same on the clock.</summary>
    public static bool operator <=(UnzonedDateTime left, UnzonedDateTime right) => left.CompareTo(right) <= 0;

    /// <summary>Later or the same on the clock.</summary>
    public static bool operator >=(UnzonedDateTime left, UnzonedDateTime right) => left.CompareTo(right) >= 0;

    /// <summary>ISO 8601 without an offset: <c>2026-10-18T10:00:00</c> (fraction only when non-zero).</summary>
    public override string ToString()
    {
        Span<char> buffer = stackalloc char[40];
        Rfc9557.TryFormatWall(ToDateTime(), buffer, out var written);
        return new string(buffer[..written]);
    }

    /// <summary>With no format: <see cref="ToString()"/>. With a format: the <see cref="DateTime"/> formatted with it.</summary>
    public string ToString(string? format, IFormatProvider? formatProvider = null) =>
        string.IsNullOrEmpty(format) ? ToString() : ToDateTime().ToString(format, formatProvider);

    /// <inheritdoc />
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider) =>
        format.IsEmpty ? Rfc9557.TryFormatWall(ToDateTime(), destination, out charsWritten) : ToDateTime().TryFormat(destination, out charsWritten, format, provider);

    /// <summary>Reads ISO 8601 text without an offset or zone (<c>2026-10-18T10:00:00</c>).</summary>
    /// <exception cref="FormatException">Invalid text, or the text has an offset or a zone (use <see cref="DateTimeZone.Parse(string)"/>).</exception>
    public static UnzonedDateTime Parse(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return TryParseCore(s, out var result, out var error) ? result : throw new FormatException($"'{s}' is not a valid UnzonedDateTime: {error}.");
    }

    /// <summary>Like <see cref="Parse(string)"/>, but returns <c>false</c> instead of throwing.</summary>
    public static bool TryParse([NotNullWhen(true)] string? s, out UnzonedDateTime result) => TryParseCore(s.AsSpan(), out result, out _) && s is not null;

    /// <summary>Like <see cref="Parse(string)"/>, but returns <c>false</c> instead of throwing.</summary>
    public static bool TryParse(ReadOnlySpan<char> s, out UnzonedDateTime result) => TryParseCore(s, out result, out _);

    static UnzonedDateTime IParsable<UnzonedDateTime>.Parse(string s, IFormatProvider? provider) => Parse(s);

    static bool IParsable<UnzonedDateTime>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out UnzonedDateTime result) => TryParse(s, out result);

    static UnzonedDateTime ISpanParsable<UnzonedDateTime>.Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        TryParseCore(s, out var result, out var error) ? result : throw new FormatException($"'{s.ToString()}' is not a valid UnzonedDateTime: {error}.");

    static bool ISpanParsable<UnzonedDateTime>.TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out UnzonedDateTime result) => TryParse(s, out result);

    private static bool TryParseCore(ReadOnlySpan<char> s, out UnzonedDateTime result, out string error)
    {
        result = default;
        if (!Rfc9557.TryParse(s, out var parsed, out error))
        {
            return false;
        }

        if (parsed.Offset is not null || parsed.Zone is not null)
        {
            error = "it has an offset or a zone; use DateTimeZone.Parse";
            return false;
        }

        result = FromDateTime(parsed.Wall);
        return true;
    }
}
