using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using SolTechnology.Time;

public record Event(DateTimeZone At, UnzonedDateTime Local, DateTimeZone? Maybe, Dictionary<DateTimeZone, int> ByTime, Dictionary<UnzonedDateTime, int> ByLocal);

[JsonSerializable(typeof(Event))]
public partial class EventContext : JsonSerializerContext;

public class JsonTests
{
    private static Event Sample() => new(
        TestZones.At("Europe/Warsaw", 2026, 7, 1, 10),
        new UnzonedDateTime(2026, 7, 1, 10),
        null,
        new Dictionary<DateTimeZone, int> { [TestZones.At("Europe/Warsaw", 2026, 7, 1, 10)] = 1 },
        new Dictionary<UnzonedDateTime, int> { [new UnzonedDateTime(2026, 7, 1, 10)] = 2 });

    [Test]
    public void RoundTrip_WithReflection()
    {
        var json = JsonSerializer.Serialize(Sample(), new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        json.Should().Contain("\"At\":\"2026-07-01T10:00:00+02:00[Europe/Warsaw]\"");
        json.Should().Contain("\"Local\":\"2026-07-01T10:00:00\"");
        json.Should().Contain("\"Maybe\":null");
        var back = JsonSerializer.Deserialize<Event>(json)!;
        back.At.SameRepresentation(Sample().At).Should().BeTrue();
        back.Local.Should().Be(Sample().Local);
        back.ByTime.Keys.Single().SameRepresentation(Sample().At).Should().BeTrue();
        back.ByLocal[new UnzonedDateTime(2026, 7, 1, 10)].Should().Be(2);
    }

    [Test]
    public void RoundTrip_WithSourceGeneration()
    {
        var json = JsonSerializer.Serialize(Sample(), EventContext.Default.Event);

        var back = JsonSerializer.Deserialize(json, EventContext.Default.Event)!;
        back.At.SameRepresentation(Sample().At).Should().BeTrue();
        back.ByLocal.Should().ContainKey(new UnzonedDateTime(2026, 7, 1, 10));
    }

    [Test]
    public void NullableWithValue()
    {
        var json = JsonSerializer.Serialize(Sample() with { Maybe = TestZones.At("UTC", 2026, 7, 1, 8) });

        JsonSerializer.Deserialize<Event>(json)!.Maybe.Should().Be(TestZones.At("UTC", 2026, 7, 1, 8));
    }

    [Test]
    public void InvalidText_IsAJsonException()
    {
        var act = () => JsonSerializer.Deserialize<DateTimeZone>("\"2026-07-01T10:00:00\"");
        var number = () => JsonSerializer.Deserialize<DateTimeZone>("42");

        act.Should().Throw<JsonException>().WithMessage("*UnzonedDateTime*");
        number.Should().Throw<JsonException>();
    }
}
