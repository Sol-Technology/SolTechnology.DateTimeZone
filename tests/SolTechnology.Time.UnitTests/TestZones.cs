using SolTechnology.Time;

internal static class TestZones
{
    public static TimeZoneInfo Warsaw => Zones.Resolve("Europe/Warsaw");
    public static TimeZoneInfo Utc => TimeZoneInfo.Utc;

    public static DateTimeZone At(string zone, int year, int month, int day, int hour, int minute = 0,
        GapPolicy gap = GapPolicy.ShiftForward, OverlapPolicy overlap = OverlapPolicy.Earlier) =>
        new(new DateOnly(year, month, day), new TimeOnly(hour, minute), Require(zone), gap, overlap);

    /// <summary>The zone, or the test is ignored when this system does not have it (e.g. Antarctica/Troll on Windows).</summary>
    public static TimeZoneInfo Require(string id)
    {
        if (!Zones.TryResolve(id, out var zone))
        {
            Assert.Ignore($"Time zone {id} is not available on this system.");
        }

        return zone!;
    }

    public static string Offset(this DateTimeZone value) => value.ToDateTimeOffset().ToString("zzz");

    public static string Wall(this DateTimeZone value) => value.ToDateTimeOffset().ToString("yyyy-MM-dd HH:mm");
}
