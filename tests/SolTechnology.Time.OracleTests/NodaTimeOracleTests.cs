using NodaTime;
using SolTechnology.Time;

/// <summary>
/// NodaTime as an oracle. Its lenient resolver has the same defaults as this library (overlap: earlier,
/// gap: shift forward by the gap), so every wall time around every transition must give the same instant.
/// Range 1972–2025 and zones without recent rule changes: older data and recent changes differ between tzdb builds.
/// </summary>
public class NodaTimeOracleTests
{
    private static readonly string[] ZoneIds =
    [
        "Europe/Warsaw", "Europe/London", "Europe/Dublin", "America/New_York", "America/Chicago", "America/Denver",
        "America/Los_Angeles", "America/Sao_Paulo", "America/Halifax", "America/St_Johns", "Asia/Tokyo", "Asia/Kolkata",
        "Asia/Shanghai", "Asia/Jerusalem", "Australia/Sydney", "Australia/Adelaide", "Australia/Lord_Howe",
        "Pacific/Auckland", "Pacific/Chatham", "Africa/Johannesburg", "Pacific/Apia", "Antarctica/Troll"
    ];

    private static readonly Instant From = Instant.FromUtc(1972, 1, 1, 0, 0);
    private static readonly Instant To = Instant.FromUtc(2025, 12, 31, 0, 0);

    [TestCaseSource(nameof(ZoneIds))]
    public void WallTimesAroundEveryTransition_ResolveLikeNodaTime(string zoneId)
    {
        var noda = DateTimeZoneProviders.Tzdb[zoneId];
        var zone = Zones.Resolve(zoneId);
        var mismatches = new List<string>();
        var checkedCount = 0;

        foreach (var interval in noda.GetZoneIntervals(From, To).Skip(1))
        {
            var transition = interval.Start.InZone(noda).LocalDateTime;
            for (var minutes = -180; minutes <= 180; minutes += 15)
            {
                var local = transition.PlusMinutes(minutes);
                var expected = local.InZoneLeniently(noda).ToInstant().ToDateTimeUtc();
                var actual = new SolTechnology.Time.DateTimeZone(
                    new DateOnly(local.Year, local.Month, local.Day), new TimeOnly(local.Hour, local.Minute), zone).UtcDateTime;
                checkedCount++;
                if (actual != expected)
                {
                    mismatches.Add($"{local:yyyy-MM-dd HH:mm}: expected {expected:u}, got {actual:u}");
                }
            }
        }

        TestContext.Out.WriteLine($"{zoneId}: {checkedCount} wall times, NodaTime tzdb {DateTimeZoneProviders.Tzdb.VersionId}");
        mismatches.Should().BeEmpty();
    }

    [TestCaseSource(nameof(ZoneIds))]
    public void RandomInstants_HaveTheSameOffsetAndWallTime(string zoneId)
    {
        var noda = DateTimeZoneProviders.Tzdb[zoneId];
        var zone = Zones.Resolve(zoneId);
        var random = new Random(StringComparer.Ordinal.GetHashCode(zoneId));
        var span = (To - From).TotalTicks;

        for (var i = 0; i < 2_000; i++)
        {
            var instant = From.PlusTicks((long)(random.NextDouble() * span));
            var expected = instant.InZone(noda);
            var actual = SolTechnology.Time.DateTimeZone.FromUtc(instant.ToDateTimeUtc(), zone);

            actual.Offset.Should().Be(expected.Offset.ToTimeSpan(), $"offset at {instant}");
            actual.ToDateTimeOffset().DateTime.Should().Be(expected.LocalDateTime.ToDateTimeUnspecified(), $"wall time at {instant}");
        }
    }
}
