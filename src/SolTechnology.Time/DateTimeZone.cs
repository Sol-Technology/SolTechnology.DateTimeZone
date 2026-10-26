using System.Text.Json.Serialization;
using SolTechnology.Time.Serialization;

namespace SolTechnology.Time;

/// <summary>
/// A point in time, together with the time zone it is seen in. Holds a <see cref="DateTimeOffset"/> (the instant and the
/// offset) and a <see cref="TimeZoneInfo"/>, and keeps them consistent: the offset is always the zone's offset at that
/// instant, also after arithmetic.
/// </summary>
/// <remarks>
/// Equality and ordering are by instant, like <see cref="DateTimeOffset"/>: 10:00 in Europe/Warsaw (summer) equals
/// 08:00 in UTC. Use <see cref="SameRepresentation"/> or <see cref="DateTimeZoneComparer.Representation"/> when the zone
/// matters too. <c>default(DateTimeZone)</c> is 0001-01-01T00:00:00 in UTC.
/// </remarks>
[JsonConverter(typeof(DateTimeZoneJsonConverter))]
public readonly partial struct DateTimeZone : IEquatable<DateTimeZone>, IComparable<DateTimeZone>, IComparable
{
    private readonly DateTimeOffset _value;
    private readonly TimeZoneInfo? _zone;

    /// <summary>Trusted constructor: <paramref name="value"/> must already have the zone's offset at its instant.</summary>
    private DateTimeZone(DateTimeOffset value, TimeZoneInfo zone)
    {
        _value = value;
        _zone = zone;
    }

    /// <summary>
    /// The instant at which the clocks in <paramref name="zone"/> show <paramref name="date"/> and <paramref name="time"/>.
    /// </summary>
    /// <param name="date">Date on the clock in the zone.</param>
    /// <param name="time">Time on the clock in the zone.</param>
    /// <param name="zone">The zone.</param>
    /// <param name="gap">What to do if the clocks skip this time (spring forward).</param>
    /// <param name="overlap">What to do if the clocks show this time twice (fall back).</param>
    public DateTimeZone(DateOnly date, TimeOnly time, TimeZoneInfo zone, GapPolicy gap = GapPolicy.ShiftForward, OverlapPolicy overlap = OverlapPolicy.Earlier)
    {
        ArgumentNullException.ThrowIfNull(zone);
        _value = ZoneResolver.Resolve(date.ToDateTime(time), zone, gap, overlap);
        _zone = zone;
    }

    /// <summary>
    /// The instant at which the clocks in the zone <paramref name="zoneId"/> show <paramref name="date"/> and <paramref name="time"/>.
    /// </summary>
    /// <param name="date">Date on the clock in the zone.</param>
    /// <param name="time">Time on the clock in the zone.</param>
    /// <param name="zoneId">IANA or Windows zone ID, see <see cref="Zones.Resolve"/>.</param>
    /// <param name="gap">What to do if the clocks skip this time (spring forward).</param>
    /// <param name="overlap">What to do if the clocks show this time twice (fall back).</param>
    public DateTimeZone(DateOnly date, TimeOnly time, string zoneId, GapPolicy gap = GapPolicy.ShiftForward, OverlapPolicy overlap = OverlapPolicy.Earlier)
        : this(date, time, Zones.Resolve(zoneId), gap, overlap)
    {
    }

    /// <summary>The zone the instant is seen in.</summary>
    public TimeZoneInfo Zone => _zone ?? TimeZoneInfo.Utc;

    /// <summary>The zone's offset from UTC at this instant.</summary>
    public TimeSpan Offset => _value.Offset;

    /// <summary>Date on the clock in the zone.</summary>
    public DateOnly Date => DateOnly.FromDateTime(_value.DateTime);

    /// <summary>Time on the clock in the zone.</summary>
    public TimeOnly Time => TimeOnly.FromDateTime(_value.DateTime);

    /// <summary>The instant as a UTC <see cref="DateTime"/> (<see cref="DateTimeKind.Utc"/>).</summary>
    public DateTime UtcDateTime => _value.UtcDateTime;

    /// <summary>The instant in ticks since 0001-01-01T00:00:00Z.</summary>
    public long UtcTicks => _value.UtcTicks;

    /// <summary>
    /// <c>true</c> if both values are the same instant in the same zone. Zone aliases count as the same zone
    /// (<c>Poland</c> and <c>Europe/Warsaw</c>, <c>Etc/UTC</c> and <c>UTC</c>).
    /// </summary>
    public bool SameRepresentation(DateTimeZone other) =>
        UtcTicks == other.UtcTicks &&
        string.Equals(Zones.RepresentationKey(Zone), Zones.RepresentationKey(other.Zone), StringComparison.OrdinalIgnoreCase);

    /// <summary>Same instant (the zone is not compared).</summary>
    public bool Equals(DateTimeZone other) => UtcTicks == other.UtcTicks;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is DateTimeZone other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => UtcTicks.GetHashCode();

    /// <summary>Compares instants.</summary>
    public int CompareTo(DateTimeZone other) => UtcTicks.CompareTo(other.UtcTicks);

    int IComparable.CompareTo(object? obj) => obj switch
    {
        null => 1,
        DateTimeZone other => CompareTo(other),
        _ => throw new ArgumentException($"Object must be of type {nameof(DateTimeZone)}.", nameof(obj))
    };

    /// <summary>Same instant.</summary>
    public static bool operator ==(DateTimeZone left, DateTimeZone right) => left.Equals(right);

    /// <summary>Different instants.</summary>
    public static bool operator !=(DateTimeZone left, DateTimeZone right) => !left.Equals(right);

    /// <summary>Earlier instant.</summary>
    public static bool operator <(DateTimeZone left, DateTimeZone right) => left.UtcTicks < right.UtcTicks;

    /// <summary>Later instant.</summary>
    public static bool operator >(DateTimeZone left, DateTimeZone right) => left.UtcTicks > right.UtcTicks;

    /// <summary>Earlier or the same instant.</summary>
    public static bool operator <=(DateTimeZone left, DateTimeZone right) => left.UtcTicks <= right.UtcTicks;

    /// <summary>Later or the same instant.</summary>
    public static bool operator >=(DateTimeZone left, DateTimeZone right) => left.UtcTicks >= right.UtcTicks;

    /// <summary>The instant <paramref name="utcTicks"/> seen in <paramref name="zone"/>.</summary>
    internal static DateTimeZone FromInstant(long utcTicks, TimeZoneInfo zone)
    {
        var offset = ZoneResolver.OffsetAt(zone, utcTicks);
        return new DateTimeZone(ZoneResolver.Create(utcTicks + offset.Ticks, offset, zone), zone);
    }

    private static DateTimeZone FromResolved(DateTimeOffset value, TimeZoneInfo zone) => new(value, zone);
}
