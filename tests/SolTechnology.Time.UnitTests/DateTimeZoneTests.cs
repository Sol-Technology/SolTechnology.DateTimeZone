using Microsoft.Extensions.Time.Testing;
using SolTechnology.Time;

public class DateTimeZoneTests
{
    [Test]
    public void Equality_IsByInstant()
    {
        var warsaw = TestZones.At("Europe/Warsaw", 2026, 7, 1, 10);
        var utc = TestZones.At("UTC", 2026, 7, 1, 8);

        (warsaw == utc).Should().BeTrue();
        warsaw.Equals(utc).Should().BeTrue();
        warsaw.GetHashCode().Should().Be(utc.GetHashCode());
        new HashSet<DateTimeZone> { warsaw, utc }.Should().HaveCount(1);
        warsaw.SameRepresentation(utc).Should().BeFalse();
        new HashSet<DateTimeZone>(DateTimeZoneComparer.Representation) { warsaw, utc }.Should().HaveCount(2);
    }

    [Test]
    public void Ordering_IsByInstant()
    {
        var warsaw = TestZones.At("Europe/Warsaw", 2026, 7, 1, 10);
        var utc = TestZones.At("UTC", 2026, 7, 1, 8, 1);

        (warsaw < utc).Should().BeTrue();
        (utc >= warsaw).Should().BeTrue();
        warsaw.CompareTo(utc).Should().BeNegative();
        new[] { utc, warsaw }.Order().First().Should().Be(warsaw);
    }

    [Test]
    public void SameRepresentation_TreatsAliasesAsOneZone()
    {
        var instant = new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc);
        var etcUtc = TryFind("Etc/UTC");
        if (etcUtc is not null)
        {
            DateTimeZone.FromUtc(instant, etcUtc).SameRepresentation(DateTimeZone.FromUtc(instant, TimeZoneInfo.Utc)).Should().BeTrue();
        }

        var poland = TryFind("Poland");
        if (poland is not null)
        {
            DateTimeZone.FromUtc(instant, poland).SameRepresentation(DateTimeZone.FromUtc(instant, TestZones.Warsaw)).Should().BeTrue();
        }

        var kiev = TryFind("Europe/Kiev");
        var kyiv = TryFind("Europe/Kyiv");
        if (kiev is not null && kyiv is not null)
        {
            DateTimeZone.FromUtc(instant, kiev).SameRepresentation(DateTimeZone.FromUtc(instant, kyiv)).Should().BeTrue();
        }
    }

    [Test]
    public void Default_IsMinValueInUtc()
    {
        var value = default(DateTimeZone);

        value.Zone.Should().BeSameAs(TimeZoneInfo.Utc);
        value.Offset.Should().Be(TimeSpan.Zero);
        value.Date.Should().Be(DateOnly.MinValue);
        value.Time.Should().Be(TimeOnly.MinValue);
        value.ToString().Should().Be("0001-01-01T00:00:00Z");
        value.AddHours(1).ToString().Should().Be("0001-01-01T01:00:00Z");
    }

    [Test]
    public void Constructor_DerivesDateTimeAndOffset()
    {
        var value = TestZones.At("Europe/Warsaw", 2026, 7, 1, 10, 15);

        value.Date.Should().Be(new DateOnly(2026, 7, 1));
        value.Time.Should().Be(new TimeOnly(10, 15));
        value.Offset.Should().Be(TimeSpan.FromHours(2));
        value.Zone.Id.Should().Be("Europe/Warsaw");
        value.UtcDateTime.Should().Be(new DateTime(2026, 7, 1, 8, 15, 0, DateTimeKind.Utc));
        value.ToDateTimeOffset().Should().Be(new DateTimeOffset(2026, 7, 1, 10, 15, 0, TimeSpan.FromHours(2)));
    }

    [Test]
    public void ToTimeZone_KeepsTheInstant()
    {
        var warsaw = TestZones.At("Europe/Warsaw", 2026, 7, 1, 10);

        var tokyo = warsaw.ToTimeZone(Zones.Resolve("Asia/Tokyo"));

        tokyo.Should().Be(warsaw);
        tokyo.Wall().Should().Be("2026-07-01 17:00");
        warsaw.ToUtc().Wall().Should().Be("2026-07-01 08:00");
        warsaw.ToUtc().Zone.Should().BeSameAs(TimeZoneInfo.Utc);
    }

    [Test]
    public void FromDateTimeOffset_WithoutZone_UsesAFixedOffset()
    {
        var value = DateTimeZone.FromDateTimeOffset(new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.FromHours(2)));

        value.Zone.Id.Should().Be("UTC+02:00");
        value.AddMonths(6).Offset().Should().Be("+02:00", "a fixed offset does not follow DST");
    }

    [Test]
    public void FromDateTimeOffset_WithZone_ShowsTheInstantInTheZone()
    {
        var value = DateTimeZone.FromDateTimeOffset(new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero), TestZones.Warsaw);

        value.Wall().Should().Be("2026-07-01 10:00");
        value.AddMonths(6).Offset().Should().Be("+01:00");
    }

    [Test]
    public void FromDateTime_Unspecified_Throws()
    {
        var act = () => DateTimeZone.FromDateTime(new DateTime(2026, 7, 1, 10, 0, 0, DateTimeKind.Unspecified));

        act.Should().Throw<DateTimeZoneException>().WithMessage("*UnzonedDateTime.FromDateTime*");
    }

    [Test]
    public void FromDateTime_Utc()
    {
        var value = DateTimeZone.FromDateTime(new DateTime(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc));

        value.Zone.Should().BeSameAs(TimeZoneInfo.Utc);
        value.Wall().Should().Be("2026-07-01 10:00");
    }

    [Test]
    public void FromDateTime_Local_KeepsBothOccurrencesOfAnAmbiguousHour()
    {
        // Find an hour that the local zone shows twice; CI runs one job with TZ=Europe/Warsaw.
        var start = new DateTime(DateTime.UtcNow.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime? first = null;
        for (var t = start; t < start.AddYears(1); t = t.AddMinutes(30))
        {
            if (t.ToLocalTime() == t.AddHours(1).ToLocalTime())
            {
                first = t;
                break;
            }
        }

        if (first is null)
        {
            Assert.Ignore($"The local zone ({TimeZoneInfo.Local.Id}) has no ambiguous hour.");
        }

        var a = DateTimeZone.FromDateTime(first.Value.ToLocalTime());
        var b = DateTimeZone.FromDateTime(first.Value.AddHours(1).ToLocalTime());

        a.UtcDateTime.Should().Be(first.Value);
        b.UtcDateTime.Should().Be(first.Value.AddHours(1));
        a.Wall().Should().Be(b.Wall());
    }

    [Test]
    public void FromUtc_Local_Throws()
    {
        var act = () => DateTimeZone.FromUtc(DateTime.Now, TestZones.Warsaw);

        act.Should().Throw<DateTimeZoneException>();
    }

    [Test]
    public void Range_IsCheckedInsteadOfClamped()
    {
        var kiritimati = Zones.Resolve("Pacific/Kiritimati");

        var late = () => DateTimeZone.FromUtc(new DateTime(9999, 12, 31, 23, 0, 0, DateTimeKind.Utc), kiritimati);
        var early = () => DateTimeZone.FromUtc(new DateTime(1, 1, 1, 1, 0, 0, DateTimeKind.Utc), Zones.Resolve("America/New_York"));
        var max = () => new DateTimeZone(DateOnly.MaxValue, new TimeOnly(23, 0), kiritimati);

        late.Should().Throw<DateTimeZoneException>().WithMessage("*outside the range*Pacific/Kiritimati*");
        early.Should().Throw<DateTimeZoneException>();
        max.Should().NotThrow();
    }

    [Test]
    public void Now_UsesTheTimeProvider()
    {
        var provider = new FakeTimeProvider(new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero));
        provider.SetLocalTimeZone(TestZones.Warsaw);

        var now = DateTimeZone.Now(provider);

        now.Zone.Should().BeSameAs(TestZones.Warsaw);
        now.Wall().Should().Be("2026-07-01 10:00");
        DateTimeZone.UtcNow(provider).Wall().Should().Be("2026-07-01 08:00");
    }

    [Test]
    public void ToUnzoned_DropsTheZone()
    {
        var unzoned = TestZones.At("Europe/Warsaw", 2026, 7, 1, 10).ToUnzoned();

        unzoned.Should().Be(new UnzonedDateTime(2026, 7, 1, 10));
    }

    private static TimeZoneInfo? TryFind(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
    }
}
