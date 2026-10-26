using SolTechnology.Time;

public class ZoneResolverTests
{
    [Test]
    public void Gap_ShiftForward_MovesByTheLengthOfTheGap()
    {
        var value = TestZones.At("Europe/Warsaw", 2026, 3, 29, 2, 30);

        value.Wall().Should().Be("2026-03-29 03:30");
        value.Offset().Should().Be("+02:00");
        value.UtcDateTime.Should().Be(new DateTime(2026, 3, 29, 1, 30, 0, DateTimeKind.Utc));
    }

    [Test]
    public void Gap_Throw_Throws()
    {
        var act = () => TestZones.At("Europe/Warsaw", 2026, 3, 29, 2, 30, gap: GapPolicy.Throw);

        act.Should().Throw<DateTimeZoneException>().WithMessage("*does not exist in Europe/Warsaw*");
    }

    [Test]
    public void Gap_TwoHours_Troll()
    {
        var value = TestZones.At("Antarctica/Troll", 2026, 3, 29, 1, 30);

        value.Wall().Should().Be("2026-03-29 03:30");
        value.Offset().Should().Be("+02:00");
    }

    [Test]
    public void Gap_SkippedDay_Apia()
    {
        // Samoa moved across the date line: 29 Dec 2011 23:59 (-10:00) was followed by 31 Dec 2011 00:00 (+14:00).
        // TimeZoneInfo.IsInvalidTime reports 30 Dec as valid; the resolver must not.
        var value = TestZones.At("Pacific/Apia", 2011, 12, 30, 12);

        value.Wall().Should().Be("2011-12-31 12:00");
        value.Offset().Should().Be("+14:00");

        var act = () => TestZones.At("Pacific/Apia", 2011, 12, 30, 12, gap: GapPolicy.Throw);
        act.Should().Throw<DateTimeZoneException>();
    }

    [Test]
    public void Gap_HalfHourDst_LordHowe()
    {
        var value = TestZones.At("Australia/Lord_Howe", 2026, 10, 4, 2, 15);

        value.Wall().Should().Be("2026-10-04 02:45");
        value.Offset().Should().Be("+11:00");
    }

    [Test]
    public void Overlap_EarlierAndLater()
    {
        var earlier = TestZones.At("Europe/Warsaw", 2026, 10, 25, 2, 30);
        var later = TestZones.At("Europe/Warsaw", 2026, 10, 25, 2, 30, overlap: OverlapPolicy.Later);

        earlier.Offset().Should().Be("+02:00");
        earlier.UtcDateTime.Should().Be(new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc));
        later.Offset().Should().Be("+01:00");
        later.UtcDateTime.Should().Be(new DateTime(2026, 10, 25, 1, 30, 0, DateTimeKind.Utc));
        earlier.Wall().Should().Be(later.Wall());
    }

    [Test]
    public void Overlap_Throw_Throws()
    {
        var act = () => TestZones.At("Europe/Warsaw", 2026, 10, 25, 2, 30, overlap: OverlapPolicy.Throw);

        act.Should().Throw<DateTimeZoneException>().WithMessage("*ambiguous in Europe/Warsaw*+02:00*+01:00*");
    }

    [Test]
    public void Overlap_HalfHourDst_LordHowe()
    {
        TestZones.At("Australia/Lord_Howe", 2026, 4, 5, 1, 45).Offset().Should().Be("+11:00");
        TestZones.At("Australia/Lord_Howe", 2026, 4, 5, 1, 45, overlap: OverlapPolicy.Later).Offset().Should().Be("+10:30");
    }

    [Test]
    public void SouthernHemisphere_Santiago()
    {
        // Chile ends DST at 24:00 on Saturday: 23:xx on 4 April 2026 happens twice; starts it at 24:00 on Saturday: 00:xx on 6 September 2026 is skipped.
        TestZones.At("America/Santiago", 2026, 4, 4, 23, 30).Offset().Should().Be("-03:00");
        TestZones.At("America/Santiago", 2026, 4, 4, 23, 30, overlap: OverlapPolicy.Later).Offset().Should().Be("-04:00");

        var gap = TestZones.At("America/Santiago", 2026, 9, 6, 0, 30);
        gap.Wall().Should().Be("2026-09-06 01:30");
        gap.Offset().Should().Be("-03:00");
    }

    [Test]
    public void NormalTime_SingleOffset()
    {
        TestZones.At("Europe/Warsaw", 2026, 1, 15, 12).Offset().Should().Be("+01:00");
        TestZones.At("Europe/Warsaw", 2026, 7, 15, 12).Offset().Should().Be("+02:00");
        TestZones.At("Asia/Kolkata", 2026, 7, 15, 12).Offset().Should().Be("+05:30");
    }
}
