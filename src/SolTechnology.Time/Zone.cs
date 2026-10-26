namespace SolTechnology.Time;

/// <summary>
/// The name of a time zone, so a zone can be named in code without a string: <c>Zone.EuropeWarsaw</c>. One static member per
/// canonical IANA zone, generated from tzdb. It holds only the ID; <see cref="Zones.Resolve(Zone)"/> turns it into a
/// <see cref="TimeZoneInfo"/>, and that is where a zone missing on the machine fails. Equality is by name:
/// <c>new Zone("Poland")</c> is not equal to <c>Zone.EuropeWarsaw</c>, although both resolve to the same zone.
/// </summary>
/// <param name="Id">IANA or Windows zone ID, see <see cref="Zones.Resolve(string)"/>.</param>
public readonly partial record struct Zone(string Id)
{
    /// <inheritdoc/>
    public override string ToString() => Id;
}
