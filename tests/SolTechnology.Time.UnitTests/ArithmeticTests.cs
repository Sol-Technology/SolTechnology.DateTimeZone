using SolTechnology.Time;

public class ArithmeticTests
{
    [Test]
    public void AddHours24_And_AddDays1_DifferAcrossSpringForward()
    {
        var start = TestZones.At("Europe/Warsaw", 2026, 3, 28, 12);

        var hours = start.AddHours(24);
        var days = start.AddDays(1);

        hours.Wall().Should().Be("2026-03-29 13:00");
        days.Wall().Should().Be("2026-03-29 12:00");
        (days - start).Should().Be(TimeSpan.FromHours(23));
        days.Offset().Should().Be("+02:00");
    }

    [Test]
    public void AddHours24_And_AddDays1_DifferAcrossFallBack()
    {
        var start = TestZones.At("Europe/Warsaw", 2026, 10, 24, 12);

        var hours = start.AddHours(24);
        var days = start.AddDays(1);

        hours.Wall().Should().Be("2026-10-25 11:00");
        days.Wall().Should().Be("2026-10-25 12:00");
        (days - start).Should().Be(TimeSpan.FromHours(25));
        days.Offset().Should().Be("+01:00");
    }

    [Test]
    public void AddHours_OverTheGap_MovesTheInstant()
    {
        var value = TestZones.At("Europe/Warsaw", 2026, 3, 29, 1, 30).AddHours(1);

        value.Wall().Should().Be("2026-03-29 03:30");
    }

    [Test]
    public void AddDays_IntoAGap_UsesThePolicy()
    {
        var start = TestZones.At("Europe/Warsaw", 2026, 3, 28, 2, 30);

        start.AddDays(1).Wall().Should().Be("2026-03-29 03:30");
        var act = () => start.AddDays(1, gap: GapPolicy.Throw);
        act.Should().Throw<DateTimeZoneException>();
    }

    [Test]
    public void AddMonths_ClampsAndIsNotAssociative()
    {
        var start = TestZones.At("Europe/Warsaw", 2026, 1, 31, 10);

        start.AddMonths(1).Wall().Should().Be("2026-02-28 10:00");
        start.AddMonths(1).AddMonths(1).Wall().Should().Be("2026-03-28 10:00");
        start.AddMonths(2).Wall().Should().Be("2026-03-31 10:00");
        start.AddMonths(6).Offset().Should().Be("+02:00");
    }

    [Test]
    public void AddYears_FromLeapDay()
    {
        TestZones.At("Europe/Warsaw", 2028, 2, 29, 10).AddYears(1).Wall().Should().Be("2029-02-28 10:00");
    }

    [Test]
    public void ZeroCalendarStep_ReturnsTheSameInstant()
    {
        var later = TestZones.At("Europe/Warsaw", 2026, 10, 25, 2, 30, overlap: OverlapPolicy.Later);

        later.AddDays(0).Should().Be(later);
        later.AddMonths(0).Should().Be(later);
        later.AddYears(0).Should().Be(later);
    }

    [Test]
    public void AtTime_KeepsTheDateInTheZone()
    {
        TestZones.At("Europe/Warsaw", 2026, 7, 1, 10).AtTime(new TimeOnly(23, 45)).Wall().Should().Be("2026-07-01 23:45");
    }

    [Test]
    public void InstantArithmetic_SmallUnits()
    {
        var start = TestZones.At("UTC", 2026, 7, 1, 10);

        start.AddMinutes(90).Wall().Should().Be("2026-07-01 11:30");
        start.AddSeconds(30).UtcDateTime.Should().Be(new DateTime(2026, 7, 1, 10, 0, 30, DateTimeKind.Utc));
        start.AddMilliseconds(1.5).UtcTicks.Should().Be(start.UtcTicks + 15_000);
        start.AddTicks(7).UtcTicks.Should().Be(start.UtcTicks + 7);
        (start + TimeSpan.FromDays(1) - TimeSpan.FromDays(1)).Should().Be(start);
    }

    [Test]
    public void Add_OutOfRange_Throws()
    {
        var act = () => DateTimeZone.FromUtc(DateTime.MaxValue, TimeZoneInfo.Utc).AddTicks(1);
        var nan = () => default(DateTimeZone).AddHours(double.NaN);

        act.Should().Throw<ArgumentOutOfRangeException>();
        nan.Should().Throw<ArgumentOutOfRangeException>();
    }
}
