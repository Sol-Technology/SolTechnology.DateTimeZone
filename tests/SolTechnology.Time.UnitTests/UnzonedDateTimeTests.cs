using SolTechnology.Time;

public class UnzonedDateTimeTests
{
    [Test]
    public void InZone_ResolvesTheWallTime()
    {
        var value = new UnzonedDateTime(2026, 3, 29, 2, 30);

        value.InZone("Europe/Warsaw").Wall().Should().Be("2026-03-29 03:30");
        var act = () => value.InZone("Europe/Warsaw", GapPolicy.Throw);
        act.Should().Throw<DateTimeZoneException>();
    }

    [Test]
    public void AssumeUtc()
    {
        var value = new UnzonedDateTime(2026, 7, 1, 10).AssumeUtc();

        value.Zone.Should().BeSameAs(TimeZoneInfo.Utc);
        value.ToString().Should().Be("2026-07-01T10:00:00Z");
    }

    [Test]
    public void FromDateTime_DropsTheKind()
    {
        var value = UnzonedDateTime.FromDateTime(new DateTime(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc));

        value.ToDateTime().Kind.Should().Be(DateTimeKind.Unspecified);
        value.ToDateTime().Should().Be(new DateTime(2026, 7, 1, 10, 0, 0));
    }

    [Test]
    public void Arithmetic_HasNoDst()
    {
        var value = new UnzonedDateTime(2026, 3, 29, 1, 30);

        value.AddHours(1).Should().Be(new UnzonedDateTime(2026, 3, 29, 2, 30));
        value.AddDays(1).Should().Be(new UnzonedDateTime(2026, 3, 30, 1, 30));
        new UnzonedDateTime(2026, 1, 31).AddMonths(1).Should().Be(new UnzonedDateTime(2026, 2, 28));
        (new UnzonedDateTime(2026, 3, 30) - new UnzonedDateTime(2026, 3, 29)).Should().Be(TimeSpan.FromDays(1));
        (value + TimeSpan.FromMinutes(30)).Should().Be(new UnzonedDateTime(2026, 3, 29, 2));
    }

    [Test]
    public void Comparison_IsByClock()
    {
        (new UnzonedDateTime(2026, 7, 1, 10) < new UnzonedDateTime(2026, 7, 1, 11)).Should().BeTrue();
        new[] { new UnzonedDateTime(2026, 7, 2), new UnzonedDateTime(2026, 7, 1) }.Order().First().Should().Be(new UnzonedDateTime(2026, 7, 1));
    }

    [TestCase("2026-07-01T10:00:00")]
    [TestCase("2026-07-01T10:00:00.25")]
    public void Text_RoundTrip(string text)
    {
        UnzonedDateTime.Parse(text).ToString().Should().Be(text);
    }

    [TestCase("2026-07-01T10:00:00Z")]
    [TestCase("2026-07-01T10:00:00+02:00")]
    [TestCase("2026-07-01T10:00:00[Europe/Warsaw]")]
    public void Text_WithOffsetOrZone_IsRejected(string text)
    {
        var act = () => UnzonedDateTime.Parse(text);

        act.Should().Throw<FormatException>().WithMessage("*DateTimeZone.Parse*");
        UnzonedDateTime.TryParse(text, out _).Should().BeFalse();
    }
}
