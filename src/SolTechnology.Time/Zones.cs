using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace SolTechnology.Time;

/// <summary>
/// Finds time zones by IANA or Windows ID and creates fixed-offset zones. IANA IDs are the canonical form:
/// aliases such as <c>Poland</c>, <c>Europe/Kiev</c> or <c>Etc/UTC</c> resolve to <c>Europe/Warsaw</c>,
/// <c>Europe/Kyiv</c> and <c>UTC</c>.
/// </summary>
public static class Zones
{
    private static readonly ConcurrentDictionary<string, TimeZoneInfo> Resolved = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<int, TimeZoneInfo> FixedZones = new();
    private static readonly TimeSpan MaxOffset = TimeSpan.FromHours(14);
    private const string UtcId = "UTC";

    /// <summary>The UTC zone (<see cref="TimeZoneInfo.Utc"/>).</summary>
    public static TimeZoneInfo Utc => TimeZoneInfo.Utc;

    /// <summary>
    /// Returns the zone for an IANA ID (<c>Europe/Warsaw</c>), a Windows ID (<c>Central European Standard Time</c>),
    /// <c>UTC</c>, or a fixed offset (<c>+02:00</c>, <c>UTC+02:00</c>).
    /// </summary>
    /// <exception cref="DateTimeZoneException">The zone does not exist on this system. The message says why, for example a missing tz database.</exception>
    /// <remarks>For a zone known at compile time use <see cref="Resolve(Zone)"/>; a typo in a string shows up only at run time.</remarks>
    public static TimeZoneInfo Resolve(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        id = id.Trim();
        if (Resolved.TryGetValue(id, out var cached))
        {
            return cached;
        }

        var zone = ResolveUncached(id);
        Resolved.TryAdd(id, zone);
        return zone;
    }

    /// <summary>The zone for a <see cref="Zone"/> name.</summary>
    /// <exception cref="DateTimeZoneException">The zone does not exist on this system, for example an old tzdata.</exception>
    public static TimeZoneInfo Resolve(Zone zone) => Resolve(zone.Id);

    /// <summary>Like <see cref="Resolve(string)"/>, but returns <c>false</c> instead of throwing.</summary>
    public static bool TryResolve(string? id, [NotNullWhen(true)] out TimeZoneInfo? zone)
    {
        zone = null;
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        try
        {
            zone = Resolve(id);
            return true;
        }
        catch (DateTimeZoneException)
        {
            return false;
        }
    }

    /// <summary>
    /// A zone with a constant offset and no DST. An offset is not a zone: <c>+02:00</c> in July can be Warsaw,
    /// Johannesburg or Kaliningrad, which differ in January. Use it only when nothing else is known.
    /// </summary>
    /// <param name="offset">Whole minutes, between -14:00 and +14:00. Zero returns <see cref="Utc"/>.</param>
    public static TimeZoneInfo FixedOffset(TimeSpan offset)
    {
        if (offset.Ticks % TimeSpan.TicksPerMinute != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), offset, "The offset must be a whole number of minutes.");
        }

        if (offset > MaxOffset || offset < -MaxOffset)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), offset, "The offset must be between -14:00 and +14:00.");
        }

        if (offset == TimeSpan.Zero)
        {
            return TimeZoneInfo.Utc;
        }

        return FixedZones.GetOrAdd((int)offset.TotalMinutes, static (_, o) =>
        {
            var id = FixedId(o);
            return TimeZoneInfo.CreateCustomTimeZone(id, o, id, id);
        }, offset);
    }

    /// <summary>
    /// The canonical IANA ID of a zone (<c>Poland</c> → <c>Europe/Warsaw</c>, <c>Etc/UTC</c> → <c>UTC</c>,
    /// a Windows ID → its IANA ID when the conversion is available), or <c>null</c> when the zone has no IANA name
    /// (fixed offsets, a local zone reported as <c>Local</c>).
    /// </summary>
    public static string? GetCanonicalId(TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var id = zone.Id;
        if (ReferenceEquals(zone, TimeZoneInfo.Utc) || IsUtcId(id))
        {
            return UtcId;
        }

        if (IsFixed(zone))
        {
            return null;
        }

        var canonical = Canonicalize(id);
        if (canonical is not null)
        {
            return canonical;
        }

        return TryConvertWindowsToIana(id, out var iana) ? Canonicalize(iana) ?? iana : null;
    }

    internal static bool IsFixed(TimeZoneInfo zone) =>
        zone.Id.Length == 9 && zone.Id.StartsWith("UTC", StringComparison.Ordinal) && zone.Id[3] is '+' or '-';

    internal static bool IsUtc(TimeZoneInfo zone) => ReferenceEquals(zone, TimeZoneInfo.Utc) || IsUtcId(zone.Id);

    /// <summary>Key that identifies "the same zone" for <see cref="DateTimeZone.SameRepresentation"/>.</summary>
    internal static string RepresentationKey(TimeZoneInfo zone) => GetCanonicalId(zone) ?? zone.Id;

    internal static bool TryParseOffset(ReadOnlySpan<char> text, out TimeSpan offset)
    {
        offset = default;
        if (text.Length != 6 || text[0] is not ('+' or '-') || text[3] != ':' ||
            !TwoDigits(text[1..3], out var hours) || !TwoDigits(text[4..6], out var minutes) || hours > 23 || minutes > 59)
        {
            return false;
        }

        offset = new TimeSpan(hours, minutes, 0);
        if (text[0] == '-')
        {
            offset = -offset;
        }

        return offset <= MaxOffset && offset >= -MaxOffset;
    }

    private static TimeZoneInfo ResolveUncached(string id)
    {
        if (IsUtcId(id) || id is "Z" or "z")
        {
            return TimeZoneInfo.Utc;
        }

        var offsetText = id.StartsWith("UTC", StringComparison.OrdinalIgnoreCase) && id.Length == 9 ? id.AsSpan(3) : id.AsSpan();
        if (TryParseOffset(offsetText, out var offset))
        {
            return FixedOffset(offset);
        }

        var canonical = Canonicalize(id);
        if (canonical is not null && TryFind(canonical, out var zone))
        {
            return zone;
        }

        if (TryFind(id, out zone))
        {
            return zone;
        }

        // A Windows ID on a system with IANA zones, or the other way round; needs ICU.
        if (TryConvertWindowsToIana(id, out var iana) && TryFind(iana, out zone))
        {
            return zone;
        }

        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(canonical ?? id, out var windows) && TryFind(windows, out zone))
        {
            return zone;
        }

        throw NotFound(id);
    }

    private static DateTimeZoneException NotFound(string id)
    {
        if (!HasTimeZoneDatabase())
        {
            return new DateTimeZoneException(
                $"Time zone '{id}' was not found, and this system has no time zone database. " +
                "Install tzdata (Debian/Ubuntu: apt-get install tzdata, Alpine: apk add tzdata) " +
                "or use a .NET container image that includes it (the '-extra' variants of the chiseled images).");
        }

        if (IsInvariantGlobalization())
        {
            return new DateTimeZoneException(
                $"Time zone '{id}' was not found. Globalization invariant mode is on, so Windows and IANA zone IDs " +
                "cannot be converted into each other. Use the ID form of this operating system " +
                $"({(OperatingSystem.IsWindows() ? "Windows IDs" : "IANA IDs such as 'Europe/Warsaw'")}), or turn invariant mode off.");
        }

        return new DateTimeZoneException($"Time zone '{id}' was not found. Use an IANA ID such as 'Europe/Warsaw'.");
    }

    private static bool TryFind(string id, [NotNullWhen(true)] out TimeZoneInfo? zone)
    {
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException or System.Security.SecurityException)
        {
            zone = null;
            return false;
        }
    }

    private static bool TryConvertWindowsToIana(string id, [NotNullWhen(true)] out string? iana)
    {
        iana = null;
        return !ZoneLinks.Zones.Contains(id) && !ZoneLinks.Links.ContainsKey(id) &&
               TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out iana);
    }

    private static string? Canonicalize(string id)
    {
        if (ZoneLinks.Links.TryGetValue(id, out var target))
        {
            return target == "Etc/UTC" ? UtcId : target;
        }

        return ZoneLinks.Zones.TryGetValue(id, out var zone) ? zone == "Etc/UTC" ? UtcId : zone : null;
    }

    private static bool IsUtcId(string id) =>
        string.Equals(id, UtcId, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(id, "Etc/UTC", StringComparison.OrdinalIgnoreCase) ||
        (ZoneLinks.Links.TryGetValue(id, out var target) && target == "Etc/UTC");

    private static string FixedId(TimeSpan offset) =>
        string.Create(CultureInfo.InvariantCulture, $"UTC{(offset < TimeSpan.Zero ? '-' : '+')}{offset.Duration():hh\\:mm}");

    private static bool TwoDigits(ReadOnlySpan<char> text, out int value)
    {
        value = 0;
        if (text.Length != 2 || !char.IsAsciiDigit(text[0]) || !char.IsAsciiDigit(text[1]))
        {
            return false;
        }

        value = (text[0] - '0') * 10 + (text[1] - '0');
        return true;
    }

    private static bool HasTimeZoneDatabase()
    {
        try
        {
            return TimeZoneInfo.GetSystemTimeZones().Count > 1;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsInvariantGlobalization() =>
        (AppContext.TryGetSwitch("System.Globalization.Invariant", out var invariant) && invariant) ||
        Environment.GetEnvironmentVariable("DOTNET_SYSTEM_GLOBALIZATION_INVARIANT") is "1" or "true" or "True";
}
