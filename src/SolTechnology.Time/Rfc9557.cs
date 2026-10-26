using System.Globalization;

namespace SolTechnology.Time;

/// <summary>Result of reading an RFC 9557 / ISO 8601 date-time: the wall time, the offset and the zone, each optional.</summary>
internal readonly record struct ParsedDateTime(DateTime Wall, TimeSpan? Offset, bool IsZ, string? Zone);

/// <summary>
/// Reader and writer for RFC 9557 (RFC 3339 + <c>[zone]</c> suffix): <c>2026-10-18T10:00:00+02:00[Europe/Warsaw]</c>.
/// Lenient where it costs nothing: seconds are optional, <c>t</c> or a space may separate date and time,
/// more than 7 fractional digits are truncated. Leap seconds (<c>:60</c>) are rejected.
/// </summary>
internal static class Rfc9557
{
    public static bool TryParse(ReadOnlySpan<char> s, out ParsedDateTime result, out string error)
    {
        result = default;
        error = string.Empty;
        s = s.Trim();
        var i = 0;

        if (!Digits(s, ref i, 4, out var year) || !Expect(s, ref i, '-') || !Digits(s, ref i, 2, out var month) ||
            !Expect(s, ref i, '-') || !Digits(s, ref i, 2, out var day))
        {
            error = "expected a date as yyyy-MM-dd";
            return false;
        }

        if (i >= s.Length || s[i] is not ('T' or 't' or ' '))
        {
            error = "expected 'T' and a time after the date";
            return false;
        }

        i++;
        if (!Digits(s, ref i, 2, out var hour) || !Expect(s, ref i, ':') || !Digits(s, ref i, 2, out var minute))
        {
            error = "expected a time as HH:mm[:ss[.fffffff]]";
            return false;
        }

        var second = 0;
        long fractionTicks = 0;
        if (i < s.Length && s[i] == ':')
        {
            i++;
            if (!Digits(s, ref i, 2, out second))
            {
                error = "expected seconds as two digits";
                return false;
            }

            if (i < s.Length && s[i] is '.' or ',')
            {
                i++;
                var start = i;
                long scale = TimeSpan.TicksPerSecond;
                while (i < s.Length && char.IsAsciiDigit(s[i]))
                {
                    if (scale > 1)
                    {
                        scale /= 10;
                        fractionTicks += (s[i] - '0') * scale;
                    }

                    i++;
                }

                if (i == start)
                {
                    error = "expected digits after the decimal separator";
                    return false;
                }
            }
        }

        if (second == 60)
        {
            error = "leap seconds (:60) cannot be represented";
            return false;
        }

        if (hour > 23 || minute > 59 || second > 59 || month is < 1 or > 12 || year < 1 || day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            error = "the date or time is out of range";
            return false;
        }

        var wall = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified).AddTicks(fractionTicks);

        TimeSpan? offset = null;
        var isZ = false;
        if (i < s.Length && s[i] is 'Z' or 'z')
        {
            offset = TimeSpan.Zero;
            isZ = true;
            i++;
        }
        else if (i < s.Length && s[i] is '+' or '-')
        {
            if (i + 6 > s.Length || !Zones.TryParseOffset(s.Slice(i, 6), out var parsed))
            {
                error = "expected an offset as +HH:mm or -HH:mm, at most 14 hours";
                return false;
            }

            // RFC 9557 reads -00:00 like Z: the instant is known, the local offset is not.
            isZ = parsed == TimeSpan.Zero && s[i] == '-';
            offset = parsed;
            i += 6;
        }

        string? zone = null;
        var first = true;
        while (i < s.Length && s[i] == '[')
        {
            var end = s[i..].IndexOf(']');
            if (end < 0)
            {
                error = "missing ']'";
                return false;
            }

            var content = s.Slice(i + 1, end - 1);
            i += end + 1;
            var critical = content.Length > 0 && content[0] == '!';
            if (critical)
            {
                content = content[1..];
            }

            var equals = content.IndexOf('=');
            if (equals < 0)
            {
                if (!first || zone is not null)
                {
                    error = "the time zone must be the first suffix and appear once";
                    return false;
                }

                if (!IsTimeZoneName(content) && !Zones.TryParseOffset(content, out _))
                {
                    error = $"'[{content.ToString()}]' is not a time zone name or offset";
                    return false;
                }

                zone = content.ToString();
            }
            else
            {
                var key = content[..equals];
                var value = content[(equals + 1)..];
                if (!IsAnnotationKey(key) || !IsAnnotationValue(value))
                {
                    error = $"'[{content.ToString()}]' is not a valid annotation";
                    return false;
                }

                var known = key.SequenceEqual("u-ca") && value.Equals("iso8601", StringComparison.OrdinalIgnoreCase);
                if (critical && !known)
                {
                    error = $"critical annotation [!{content.ToString()}] is not supported";
                    return false;
                }
            }

            first = false;
        }

        if (i != s.Length)
        {
            error = $"unexpected text '{s[i..].ToString()}'";
            return false;
        }

        result = new ParsedDateTime(wall, offset, isZ, zone);
        return true;
    }

    /// <summary>Writes <c>yyyy-MM-ddTHH:mm:ss[.fffffff]</c> (fraction only when non-zero, trailing zeros trimmed).</summary>
    public static bool TryFormatWall(DateTime wall, Span<char> destination, out int written) =>
        wall.TryFormat(destination, out written,
            wall.Ticks % TimeSpan.TicksPerSecond == 0 ? "yyyy'-'MM'-'dd'T'HH':'mm':'ss" : "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'FFFFFFF",
            CultureInfo.InvariantCulture);

    /// <summary>Writes <c>+HH:mm</c> or <c>-HH:mm</c>.</summary>
    public static bool TryFormatOffset(TimeSpan offset, Span<char> destination, out int written)
    {
        written = 0;
        if (destination.Length < 6)
        {
            return false;
        }

        var duration = offset.Duration();
        destination[0] = offset < TimeSpan.Zero ? '-' : '+';
        destination[1] = (char)('0' + duration.Hours / 10);
        destination[2] = (char)('0' + duration.Hours % 10);
        destination[3] = ':';
        destination[4] = (char)('0' + duration.Minutes / 10);
        destination[5] = (char)('0' + duration.Minutes % 10);
        written = 6;
        return true;
    }

    // RFC 9557: time-zone-name = time-zone-part *("/" time-zone-part); a part starts with ALPHA, "." or "_",
    // continues with ALPHA, DIGIT, ".", "_", "-" or "+", and is not "." or "..".
    private static bool IsTimeZoneName(ReadOnlySpan<char> name)
    {
        if (name.IsEmpty)
        {
            return false;
        }

        while (true)
        {
            var slash = name.IndexOf('/');
            var part = slash < 0 ? name : name[..slash];
            if (part.IsEmpty || part is "." or ".." || !(char.IsAsciiLetter(part[0]) || part[0] is '.' or '_'))
            {
                return false;
            }

            foreach (var c in part)
            {
                if (!(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or '+'))
                {
                    return false;
                }
            }

            if (slash < 0)
            {
                return true;
            }

            name = name[(slash + 1)..];
        }
    }

    // annotation-key = key-initial *key-char; key-initial = lcalpha / "_"; key-char = key-initial / DIGIT / "-".
    private static bool IsAnnotationKey(ReadOnlySpan<char> key)
    {
        if (key.IsEmpty || !(char.IsAsciiLetterLower(key[0]) || key[0] == '_'))
        {
            return false;
        }

        foreach (var c in key)
        {
            if (!(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '_' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    // annotation-value = 1*alphanum *("-" 1*alphanum).
    private static bool IsAnnotationValue(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty || value[0] == '-' || value[^1] == '-' || value.IndexOf("--") >= 0)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c == '-'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Digits(ReadOnlySpan<char> s, ref int i, int count, out int value)
    {
        value = 0;
        if (i + count > s.Length)
        {
            return false;
        }

        for (var k = 0; k < count; k++)
        {
            var c = s[i + k];
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }

            value = value * 10 + (c - '0');
        }

        i += count;
        return true;
    }

    private static bool Expect(ReadOnlySpan<char> s, ref int i, char c)
    {
        if (i < s.Length && s[i] == c)
        {
            i++;
            return true;
        }

        return false;
    }
}
