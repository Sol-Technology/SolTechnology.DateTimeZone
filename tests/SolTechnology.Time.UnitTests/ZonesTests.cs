using SolTechnology.Time;

public class ZonesTests
{
    private static bool Invariant =>
        Environment.GetEnvironmentVariable("DOTNET_SYSTEM_GLOBALIZATION_INVARIANT") is "1" or "true" or "True";

    [TestCase("UTC")]
    [TestCase("Etc/UTC")]
    [TestCase("Z")]
    [TestCase("Zulu")]
    public void Utc(string id)
    {
        Zones.Resolve(id).Should().BeSameAs(TimeZoneInfo.Utc);
    }

    [TestCase("Poland", "Europe/Warsaw")]
    [TestCase("Europe/Kiev", "Europe/Kyiv")]
    [TestCase("US/Eastern", "America/New_York")]
    [TestCase("europe/warsaw", "Europe/Warsaw")]
    public void Aliases_ResolveToCanonical(string id, string canonical)
    {
        Zones.GetCanonicalId(Zones.Resolve(id)).Should().Be(canonical);
    }

    [TestCase("Europe/Oslo")]
    [TestCase("Atlantic/Reykjavik")]
    [TestCase("Asia/Kuwait")]
    [TestCase("Europe/Amsterdam")]
    public void MergedLocations_AreNotAliases(string id)
    {
        var zone = Zones.Resolve(id);

        zone.Id.Should().Be(id);
        Zones.GetCanonicalId(zone).Should().Be(id);
    }

    [Test]
    public void WindowsId_ResolvesToIana()
    {
        if (Invariant)
        {
            Assert.Ignore("Windows/IANA conversion needs ICU.");
        }

        Zones.GetCanonicalId(Zones.Resolve("Central European Standard Time")).Should().Be("Europe/Warsaw");
    }

    [Test]
    public void WindowsId_InInvariantMode_ExplainsWhy()
    {
        if (!Invariant || OperatingSystem.IsWindows())
        {
            Assert.Ignore("Runs in the invariant-globalization CI job on Linux/macOS.");
        }

        var act = () => Zones.Resolve("Central European Standard Time");

        act.Should().Throw<DateTimeZoneException>().WithMessage("*invariant mode*");
    }

    [Test]
    public void FixedOffset()
    {
        var zone = Zones.FixedOffset(TimeSpan.FromHours(2));

        zone.Id.Should().Be("UTC+02:00");
        Zones.FixedOffset(TimeSpan.FromHours(2)).Should().BeSameAs(zone);
        Zones.Resolve("+02:00").Should().BeSameAs(zone);
        Zones.Resolve("UTC+02:00").Should().BeSameAs(zone);
        Zones.FixedOffset(TimeSpan.FromMinutes(-330)).Id.Should().Be("UTC-05:30");
        Zones.FixedOffset(TimeSpan.Zero).Should().BeSameAs(TimeZoneInfo.Utc);
        Zones.GetCanonicalId(zone).Should().BeNull();
    }

    [Test]
    public void FixedOffset_Invalid()
    {
        var seconds = () => Zones.FixedOffset(TimeSpan.FromSeconds(30));
        var tooFar = () => Zones.FixedOffset(TimeSpan.FromHours(15));

        seconds.Should().Throw<ArgumentOutOfRangeException>();
        tooFar.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void Unknown_ThrowsWithAHint()
    {
        if (Environment.GetEnvironmentVariable("EXPECT_NO_TZDATA") == "1")
        {
            Assert.Ignore("No tz database in this job.");
        }

        var act = () => Zones.Resolve("Mars/Olympus");

        act.Should().Throw<DateTimeZoneException>().WithMessage("*Mars/Olympus*IANA*");
        Zones.TryResolve("Mars/Olympus", out _).Should().BeFalse();
        Zones.TryResolve(null, out _).Should().BeFalse();
    }

    [Test]
    public void NoTimeZoneDatabase_ExplainsWhy()
    {
        if (Environment.GetEnvironmentVariable("EXPECT_NO_TZDATA") != "1")
        {
            Assert.Ignore("Runs in the CI job without tzdata (TZDIR pointing to an empty directory).");
        }

        var act = () => Zones.Resolve("Europe/Warsaw");

        act.Should().Throw<DateTimeZoneException>().WithMessage("*no time zone database*tzdata*");
    }
}
