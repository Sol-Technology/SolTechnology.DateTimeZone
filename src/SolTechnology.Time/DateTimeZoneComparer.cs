namespace SolTechnology.Time;

/// <summary>Equality comparers for <see cref="DateTimeZone"/>.</summary>
public static class DateTimeZoneComparer
{
    /// <summary>
    /// Same instant and same zone (aliases count as the same zone). For dictionaries and <c>Distinct</c> that must keep
    /// 10:00 in Warsaw apart from 08:00 in UTC. The default equality compares only the instant.
    /// </summary>
    public static IEqualityComparer<DateTimeZone> Representation { get; } = new RepresentationComparer();

    private sealed class RepresentationComparer : IEqualityComparer<DateTimeZone>
    {
        public bool Equals(DateTimeZone x, DateTimeZone y) => x.SameRepresentation(y);

        public int GetHashCode(DateTimeZone obj) =>
            HashCode.Combine(obj.UtcTicks, StringComparer.OrdinalIgnoreCase.GetHashCode(Zones.RepresentationKey(obj.Zone)));
    }
}
