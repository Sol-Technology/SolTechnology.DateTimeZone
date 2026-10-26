using SolTechnology.Time;

/// <summary>The examples from README.md, so the README cannot drift from the code.</summary>
public class ReadmeTests
{
    [Test]
    public void Example()
    {
        var meeting = new DateTimeZone(new DateOnly(2026, 10, 24), new TimeOnly(10, 0), Zone.EuropeWarsaw);

        meeting.ToString().Should().Be("2026-10-24T10:00:00+02:00[Europe/Warsaw]");
        meeting.AddDays(7).ToString().Should().Be("2026-10-31T10:00:00+01:00[Europe/Warsaw]");
        meeting.AddHours(24).ToString().Should().Be("2026-10-25T09:00:00+01:00[Europe/Warsaw]");
        meeting.ToTimeZone(Zone.AsiaTokyo).ToString().Should().Be("2026-10-24T17:00:00+09:00[Asia/Tokyo]");
        (meeting == meeting.ToUtc()).Should().BeTrue();
    }

    [Test]
    public void Rules()
    {
        new DateTimeZone(new DateOnly(2026, 3, 29), new TimeOnly(2, 30), Zone.EuropeWarsaw).ToString()
            .Should().Be("2026-03-29T03:30:00+02:00[Europe/Warsaw]");
        new DateTimeZone(new DateOnly(2026, 10, 25), new TimeOnly(2, 30), Zone.EuropeWarsaw, ClockChange.NewOffset).ToString()
            .Should().Be("2026-10-25T02:30:00+01:00[Europe/Warsaw]");
    }

    [Test]
    public void Unzoned()
    {
        var local = UnzonedDateTime.Parse("2026-10-24T10:00:00");

        local.InZone(Zone.EuropeWarsaw).ToString().Should().Be("2026-10-24T10:00:00+02:00[Europe/Warsaw]");
        local.AssumeUtc().ToString().Should().Be("2026-10-24T10:00:00Z");
    }
}
