using System.Diagnostics.CodeAnalysis;

namespace SolTechnology.Time;

public readonly partial struct DateTimeZone : ISpanFormattable, ISpanParsable<DateTimeZone>
{
    /// <summary>
    /// RFC 9557 text: <c>2026-10-18T10:00:00+02:00[Europe/Warsaw]</c>. UTC is written as <c>…Z</c>, a fixed-offset zone as
    /// the offset only. A zone without an IANA name (for example a local zone reported as <c>Local</c>) is also written as
    /// the offset only, so reading it back gives a fixed-offset zone.
    /// </summary>
    public override string ToString()
    {
        Span<char> buffer = stackalloc char[96];
        return TryFormatDefault(buffer, out var written) ? new string(buffer[..written]) : ToStringSlow();
    }

    /// <summary>
    /// With no format: the RFC 9557 text of <see cref="ToString()"/>. With a format: the <see cref="DateTimeOffset"/>
    /// of this value formatted with it (the zone is not part of the output).
    /// </summary>
    public string ToString(string? format, IFormatProvider? formatProvider = null) =>
        string.IsNullOrEmpty(format) ? ToString() : _value.ToString(format, formatProvider);

    /// <inheritdoc />
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider) =>
        format.IsEmpty ? TryFormatDefault(destination, out charsWritten) : _value.TryFormat(destination, out charsWritten, format, provider);

    /// <summary>Reads RFC 9557 text. If the offset does not match the zone at that instant, <see cref="FormatException"/> is thrown.</summary>
    public static DateTimeZone Parse(string s) => Parse(s, OffsetConflict.Throw);

    /// <summary>
    /// Reads RFC 9557 text:
    /// <list type="bullet">
    /// <item><c>…+02:00[Europe/Warsaw]</c>: that instant in that zone; <paramref name="conflict"/> decides when the offset is wrong for the zone.</item>
    /// <item><c>…Z[Europe/Warsaw]</c>: the instant in UTC, seen in the zone.</item>
    /// <item><c>…+02:00</c> or <c>…Z</c>: a fixed-offset zone, or UTC.</item>
    /// <item><c>…T10:00[Europe/Warsaw]</c>: the wall time in the zone (gap: shift forward, overlap: earlier).</item>
    /// </list>
    /// Text without an offset and a zone is an <see cref="UnzonedDateTime"/>, not a <see cref="DateTimeZone"/>.
    /// </summary>
    public static DateTimeZone Parse(string s, OffsetConflict conflict)
    {
        ArgumentNullException.ThrowIfNull(s);
        return TryParseCore(s, conflict, out var result, out var error) ? result : throw new FormatException($"'{s}' is not a valid DateTimeZone: {error}.");
    }

    /// <summary>Like <see cref="Parse(string)"/>; <c>false</c> for invalid text or an unknown zone.</summary>
    public static bool TryParse([NotNullWhen(true)] string? s, out DateTimeZone result) => TryParse(s.AsSpan(), out result) && s is not null;

    /// <summary>Like <see cref="Parse(string)"/>; <c>false</c> for invalid text or an unknown zone.</summary>
    public static bool TryParse(ReadOnlySpan<char> s, out DateTimeZone result) => TryParseCore(s, OffsetConflict.Throw, out result, out _);

    static DateTimeZone IParsable<DateTimeZone>.Parse(string s, IFormatProvider? provider) => Parse(s);

    static bool IParsable<DateTimeZone>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out DateTimeZone result) => TryParse(s, out result);

    static DateTimeZone ISpanParsable<DateTimeZone>.Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        TryParseCore(s, OffsetConflict.Throw, out var result, out var error) ? result : throw new FormatException($"'{s.ToString()}' is not a valid DateTimeZone: {error}.");

    static bool ISpanParsable<DateTimeZone>.TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out DateTimeZone result) => TryParse(s, out result);

    private static bool TryParseCore(ReadOnlySpan<char> s, OffsetConflict conflict, out DateTimeZone result, out string error)
    {
        result = default;
        if (!Rfc9557.TryParse(s, out var parsed, out error))
        {
            return false;
        }

        try
        {
            if (parsed.Zone is not null)
            {
                if (!Zones.TryResolve(parsed.Zone, out var zone))
                {
                    error = $"unknown time zone '{parsed.Zone}'";
                    return false;
                }

                if (parsed.Offset is null)
                {
                    result = FromResolved(ZoneResolver.Resolve(parsed.Wall, zone, GapPolicy.ShiftForward, OverlapPolicy.Earlier), zone);
                    return true;
                }

                var utcTicks = parsed.Wall.Ticks - parsed.Offset.Value.Ticks;
                if (parsed.IsZ || ZoneResolver.OffsetAt(zone, utcTicks) == parsed.Offset.Value)
                {
                    result = FromInstant(utcTicks, zone);
                    return true;
                }

                switch (conflict)
                {
                    case OffsetConflict.UseOffset:
                        result = FromInstant(utcTicks, zone);
                        return true;
                    case OffsetConflict.UseZone:
                        result = FromResolved(ZoneResolver.Resolve(parsed.Wall, zone, GapPolicy.ShiftForward, OverlapPolicy.Earlier), zone);
                        return true;
                    default:
                        error = $"the offset {Format(parsed.Offset.Value)} is not the offset of {parsed.Zone} at that time ({Format(ZoneResolver.OffsetAt(zone, utcTicks))})";
                        return false;
                }
            }

            if (parsed.Offset is not null)
            {
                var zone = parsed.IsZ ? TimeZoneInfo.Utc : Zones.FixedOffset(parsed.Offset.Value);
                result = FromInstant(parsed.Wall.Ticks - parsed.Offset.Value.Ticks, zone);
                return true;
            }

            error = "no offset and no zone; this is an UnzonedDateTime";
            return false;
        }
        catch (DateTimeZoneException e)
        {
            error = e.Message;
            return false;
        }
    }

    private bool TryFormatDefault(Span<char> destination, out int written)
    {
        written = 0;
        if (!Rfc9557.TryFormatWall(_value.DateTime, destination, out var n))
        {
            return false;
        }

        var zone = Zone;
        if (Zones.IsUtc(zone))
        {
            if (destination.Length <= n)
            {
                return false;
            }

            destination[n++] = 'Z';
            written = n;
            return true;
        }

        if (!Rfc9557.TryFormatOffset(Offset, destination[n..], out var m))
        {
            return false;
        }

        n += m;
        var id = Zones.IsFixed(zone) ? null : Zones.GetCanonicalId(zone);
        if (id is not null)
        {
            if (destination.Length < n + id.Length + 2)
            {
                return false;
            }

            destination[n++] = '[';
            id.AsSpan().CopyTo(destination[n..]);
            n += id.Length;
            destination[n++] = ']';
        }

        written = n;
        return true;
    }

    private string ToStringSlow()
    {
        var buffer = new char[512];
        return TryFormatDefault(buffer, out var written) ? new string(buffer, 0, written) : _value.ToString("O");
    }

    private static string Format(TimeSpan offset)
    {
        Span<char> buffer = stackalloc char[6];
        Rfc9557.TryFormatOffset(offset, buffer, out _);
        return new string(buffer);
    }
}
