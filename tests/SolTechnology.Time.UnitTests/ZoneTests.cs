using System.Reflection;
using SolTechnology.Time;

public class ZoneTests
{
    private static IEnumerable<(string Name, Zone Value)> Members() =>
        typeof(Zone).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(Zone))
            .Select(p => (p.Name, (Zone)p.GetValue(null)!));

    [Test]
    public void EveryMember_HasACanonicalId()
    {
        var members = Members().ToList();

        members.Should().HaveCount(ZoneLinks.Zones.Count);
        foreach (var (name, value) in members)
        {
            (value.Id == "UTC" || ZoneLinks.Zones.Contains(value.Id)).Should().BeTrue($"{name} maps to {value.Id}");
        }
    }

    [Test]
    public void Resolve_GivesTheSameZoneAsTheId()
    {
        Zones.Resolve(Zone.EuropeWarsaw).Should().BeSameAs(Zones.Resolve("Europe/Warsaw"));
        Zones.Resolve(Zone.Utc).Should().BeSameAs(Zones.Utc);
        Zones.Resolve(Zone.EtcGmtPlus5).Id.Should().Be(Zones.Resolve("Etc/GMT+5").Id);
        Zones.Resolve(new Zone("Poland")).Should().BeSameAs(Zones.Resolve(Zone.EuropeWarsaw));
    }

    [Test]
    public void IsAName_NotANumber()
    {
        Zone.EuropeWarsaw.Id.Should().Be("Europe/Warsaw");
        Zone.EuropeWarsaw.ToString().Should().Be("Europe/Warsaw");
        Zone.EuropeWarsaw.Should().Be(new Zone("Europe/Warsaw"));
        Zone.EuropeWarsaw.Should().NotBe(new Zone("Poland"));
    }

    [Test]
    public void Overloads_TakeTheZone()
    {
        var value = new DateTimeZone(new DateOnly(2026, 7, 1), new TimeOnly(10, 0), Zone.EuropeWarsaw);

        value.ToTimeZone(Zone.AsiaTokyo).ToString().Should().Be("2026-07-01T17:00:00+09:00[Asia/Tokyo]");
        DateTimeZone.FromDateTimeOffset(value.ToDateTimeOffset(), Zone.Utc).Should().Be(value.ToUtc());
        new UnzonedDateTime(2026, 7, 1, 10).InZone(Zone.EuropeWarsaw).Should().Be(value);
    }

    [Test]
    public void UnknownName_FailsAtResolve()
    {
        var act = () => Zones.Resolve(new Zone("Europe/Warsw"));

        act.Should().Throw<DateTimeZoneException>().WithMessage("*Europe/Warsw*");
    }
}
