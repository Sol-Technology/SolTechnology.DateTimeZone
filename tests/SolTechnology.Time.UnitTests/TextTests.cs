using SolTechnology.Time;

public class TextTests
{
    [TestCase("2026-07-01T10:00:00+02:00[Europe/Warsaw]")]
    [TestCase("2026-01-15T10:00:00+01:00[Europe/Warsaw]")]
    [TestCase("2026-07-01T10:00:00.1234567+05:30[Asia/Kolkata]")]
    [TestCase("2026-07-01T10:00:00.5-04:00[America/New_York]")]
    [TestCase("2026-07-01T10:00:00+02:00")]
    [TestCase("2026-07-01T08:00:00Z")]
    [TestCase("0001-01-01T00:00:00Z")]
    public void RoundTrip(string text)
    {
        var value = DateTimeZone.Parse(text);

        value.ToString().Should().Be(text);
        DateTimeZone.Parse(value.ToString()).SameRepresentation(value).Should().BeTrue();
    }

    [Test]
    public void ToString_WritesCanonicalZoneIds()
    {
        var poland = TryFind("Poland");
        if (poland is null)
        {
            Assert.Ignore("No 'Poland' zone on this system.");
        }

        DateTimeZone.FromUtc(new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc), poland).ToString()
            .Should().Be("2026-07-01T10:00:00+02:00[Europe/Warsaw]");
    }

    [Test]
    public void ZoneWithoutIanaName_IsWrittenAsOffset()
    {
        var custom = TimeZoneInfo.CreateCustomTimeZone("My Zone", TimeSpan.FromHours(3), "My Zone", "My Zone");

        DateTimeZone.FromUtc(new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc), custom).ToString()
            .Should().Be("2026-07-01T11:00:00+03:00");
    }

    [Test]
    public void OffsetAndZone_Conflict()
    {
        const string text = "2026-07-01T10:00:00+01:00[Europe/Warsaw]";

        var act = () => DateTimeZone.Parse(text);
        act.Should().Throw<FormatException>().WithMessage("*+01:00 is not the offset of Europe/Warsaw*(+02:00)*");
        DateTimeZone.TryParse(text, out _).Should().BeFalse();

        DateTimeZone.Parse(text, OffsetConflict.UseOffset).ToString().Should().Be("2026-07-01T11:00:00+02:00[Europe/Warsaw]");
        DateTimeZone.Parse(text, OffsetConflict.UseZone).ToString().Should().Be("2026-07-01T10:00:00+02:00[Europe/Warsaw]");
    }

    [Test]
    public void Z_WithZone_IsTheInstantSeenInTheZone()
    {
        DateTimeZone.Parse("2026-07-01T08:00:00Z[Europe/Warsaw]").ToString().Should().Be("2026-07-01T10:00:00+02:00[Europe/Warsaw]");
        DateTimeZone.Parse("2026-07-01T08:00:00-00:00[Europe/Warsaw]").ToString().Should().Be("2026-07-01T10:00:00+02:00[Europe/Warsaw]");
    }

    [Test]
    public void ZoneWithoutOffset_IsAWallTimeInTheZone()
    {
        DateTimeZone.Parse("2026-07-01T10:00:00[Europe/Warsaw]").ToString().Should().Be("2026-07-01T10:00:00+02:00[Europe/Warsaw]");
        DateTimeZone.Parse("2026-03-29T02:30[Europe/Warsaw]").ToString().Should().Be("2026-03-29T03:30:00+02:00[Europe/Warsaw]");
    }

    [Test]
    public void NoOffsetNoZone_IsNotADateTimeZone()
    {
        var act = () => DateTimeZone.Parse("2026-07-01T10:00:00");

        act.Should().Throw<FormatException>().WithMessage("*UnzonedDateTime*");
        UnzonedDateTime.Parse("2026-07-01T10:00:00").Should().Be(new UnzonedDateTime(2026, 7, 1, 10));
    }

    [TestCase("2026-07-01T10:00:00+02:00[!Europe/Warsaw]")]
    [TestCase("2026-07-01T10:00:00+02:00[Europe/Warsaw][u-ca=iso8601]")]
    [TestCase("2026-07-01T10:00:00+02:00[Europe/Warsaw][!u-ca=iso8601]")]
    [TestCase("2026-07-01T10:00:00+02:00[Europe/Warsaw][u-ca=hebrew]")]
    [TestCase("2026-07-01T10:00:00+02:00[Europe/Warsaw][foo=bar]")]
    [TestCase("2026-07-01t10:00:00+02:00[Europe/Warsaw]")]
    [TestCase("2026-07-01 10:00:00+02:00[Europe/Warsaw]")]
    [TestCase("2026-07-01T10:00+02:00[Europe/Warsaw]")]
    [TestCase("2026-07-01T10:00:00,0+02:00[Europe/Warsaw]")]
    [TestCase("2026-07-01T10:00:00+02:00[Poland]")]
    public void Accepted(string text)
    {
        DateTimeZone.Parse(text).ToString().Should().Be("2026-07-01T10:00:00+02:00[Europe/Warsaw]");
    }

    [TestCase("2026-07-01T10:00:00+02:00[Europe/Warsaw][!u-ca=hebrew]", "critical")]
    [TestCase("2026-07-01T10:00:00+02:00[Europe/Warsaw][!foo=bar]", "critical")]
    [TestCase("2026-07-01T10:00:00+02:00[Europe/Warsaw][Europe/Berlin]", "first suffix")]
    [TestCase("2026-07-01T10:00:00+02:00[u-ca=iso8601][Europe/Warsaw]", "first suffix")]
    [TestCase("2026-07-01T10:00:00+02:00[Mars/Olympus]", "unknown time zone")]
    [TestCase("2026-07-01T10:00:00+02:00[Europe/Warsaw", "missing")]
    [TestCase("2026-06-30T23:59:60Z", "leap second")]
    [TestCase("2026-02-30T10:00:00Z", "out of range")]
    [TestCase("2026-07-01T24:00:00Z", "out of range")]
    [TestCase("2026-07-01T10:00:00+15:00", "offset")]
    [TestCase("2026-07-01T10:00:00+0200", "offset")]
    [TestCase("2026-07-01", "time")]
    [TestCase("2026-07-01T10:00:00Zjunk", "unexpected")]
    [TestCase("", "date")]
    [TestCase("2026-07-01T10:00:00+02:00[Europe/Warsaw=1]", "annotation")]
    [TestCase("2026-07-01T10:00:00+02:00[=]", "annotation")]
    [TestCase("2026-07-01T10:00:00+02:00[Europe/Warsaw][U-CA=iso8601]", "annotation")]
    [TestCase("2026-07-01T10:00:00+02:00[Europe/Warsaw][u-ca=]", "annotation")]
    [TestCase("2026-07-01T10:00:00+02:00[Europe//Warsaw]", "time zone name")]
    [TestCase("2026-07-01T10:00:00+02:00[ Europe/Warsaw ]", "time zone name")]
    [TestCase("2026-07-01T10:00:00+02:00[]", "time zone name")]
    [TestCase("2026-07-01T10:00:00+02:00[Central European Standard Time]", "time zone name")]
    public void Rejected(string text, string reason)
    {
        var act = () => DateTimeZone.Parse(text);

        act.Should().Throw<FormatException>().WithMessage($"*{reason}*");
        DateTimeZone.TryParse(text, out _).Should().BeFalse();
    }

    [TestCase("2026-07-01T10:00:00+02:00[Europe/Oslo]")]
    [TestCase("2026-07-01T08:00:00+00:00[Atlantic/Reykjavik]")]
    [TestCase("2026-07-01T10:00:00+02:00[Arctic/Longyearbyen]")]
    [TestCase("2026-07-01T10:00:00+02:00[Etc/GMT-2]")]
    public void MergedLocations_KeepTheirOwnName(string text)
    {
        DateTimeZone.Parse(text).ToString().Should().Be(text);
    }

    [Test]
    public void MoreThanSevenFractionalDigits_AreTruncated()
    {
        DateTimeZone.Parse("2026-07-01T10:00:00.123456789Z").ToString().Should().Be("2026-07-01T10:00:00.1234567Z");
    }

    [Test]
    public void BracketedOffset_IsAFixedZone()
    {
        DateTimeZone.Parse("2026-07-01T10:00:00+02:00[+02:00]").Zone.Id.Should().Be("UTC+02:00");
    }

    [Test]
    public void Format_DelegatesToDateTimeOffset()
    {
        var value = TestZones.At("Europe/Warsaw", 2026, 7, 1, 10);

        value.ToString("yyyy-MM-dd HH:mm zzz", null).Should().Be("2026-07-01 10:00 +02:00");
        $"{value:HH:mm}".Should().Be("10:00");
        $"{value}".Should().Be("2026-07-01T10:00:00+02:00[Europe/Warsaw]");
    }

    [Test]
    public void TryFormat_SmallBuffer_ReturnsFalse()
    {
        var value = TestZones.At("Europe/Warsaw", 2026, 7, 1, 10);

        value.TryFormat(new char[10], out _, default, null).Should().BeFalse();
        var buffer = new char[64];
        value.TryFormat(buffer, out var written, default, null).Should().BeTrue();
        new string(buffer, 0, written).Should().Be(value.ToString());
    }

    [Test]
    public void GenericParsing_UsesTheSameRules()
    {
        Parse<DateTimeZone>("2026-07-01T10:00:00+02:00[Europe/Warsaw]").Offset.Should().Be(TimeSpan.FromHours(2));
        Parse<UnzonedDateTime>("2026-07-01T10:00:00").Should().Be(new UnzonedDateTime(2026, 7, 1, 10));
    }

    private static T Parse<T>(string s) where T : IParsable<T> => T.Parse(s, null);

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
