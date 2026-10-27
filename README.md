<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="./docs/logo-dark.png">
    <img alt="SolTechnology / DateTimeZone" src="./docs/logo.png" width="300">
  </picture>
</p>

<p align="center">
  A point in time, surviving clock changes
</p>

<p align="center">
 <a href="https://www.nuget.org/packages/SolTechnology.DateTimeZone"><img src="https://img.shields.io/nuget/v/SolTechnology.DateTimeZone?label=NuGet&logo=nuget&color=blue"></a>
 <a href="https://github.com/Sol-Technology/SolTechnology.DateTimeZone/actions/workflows/build.yml"><img src="https://img.shields.io/github/actions/workflow/status/Sol-Technology/SolTechnology.DateTimeZone/build.yml?label=Build&logo=github"></a>
 <a href="./LICENSE"><img src="https://img.shields.io/badge/licence-MIT-2F7D7A.svg"></a>
</p>

## Docs

[Documentation](./docs/Documentation.md)

[Release notes](./docs/release-notes)

[RFC 9557, the text format](https://www.rfc-editor.org/rfc/rfc9557)


## Why DateTimeZone

**`DateTimeOffset` remembers the offset, not the zone.** Add a week to a 10:00 meeting in Warsaw across the last Sunday of October and the result still carries +02:00, an offset Warsaw no longer has; on a Warsaw clock it is 09:00. `DateTimeZone` is a `DateTimeOffset` plus a `TimeZoneInfo`, kept consistent, so the offset is always the one the zone has at that point in time. A date and time with no zone recorded is a separate type, `UnzonedDateTime`, never a silent UTC.

|                                                           | DateTimeZone | DateTimeOffset |     DateTime      | NodaTime |
|-----------------------------------------------------------|:------------:|:--------------:|:-----------------:|:--------:|
| Keeps the point in time                                   |      ✔️      |       ✔️       |  only `Kind.Utc`  |    ✔️    |
| Keeps the zone                                            |      ✔️      |  offset only   |        ❌         |    ✔️    |
| Offset stays right after `AddDays` across a DST change    |      ✔️      |       ❌       |        ❌         |    ✔️    |
| Clock change (DST): you choose how a skipped or repeated time is read |  ✔️  |       ❌       |        ❌         |    ✔️    |
| Date and time without a zone is a separate type           | `UnzonedDateTime` |    ❌     | `Kind.Unspecified` | `LocalDateTime` |
| Text keeps the zone                                       | RFC 9557 `[Europe/Warsaw]` | ❌ |        ❌         | own patterns |
| Built on `DateTimeOffset`, `TimeZoneInfo`, `DateOnly`, `TimeOnly` | ✔️  |       ✔️       |        ✔️         | own types, own tzdb |
| Dependencies                                              |  0, 88 KB    |       0        |         0         | 1 package, 524 KB with its own tzdb (3.3.6) |

What it means for your code:
* **One type for "when and where"** – the zone travels with the value through methods, JSON and logs, so nobody has to remember which `DateTime` was in which zone
* **Arithmetic you can reason about** – `AddHours(24)` moves the point in time, `AddDays(1)` moves the clock; across a DST night they differ and the names say so
* **No guessing** – `Kind.Unspecified` is rejected, and on the night of a clock change you say whether 02:30 means the first one or the second one, instead of whatever `TimeZoneInfo` felt like

NodaTime is the complete solution. This one is for code that already lives on BCL types and wants the zone to stay attached, without adopting a second time model.


## Features

* Create

```csharp
using SolTechnology.Time;

var meeting = new DateTimeZone(new DateOnly(2026, 10, 24), new TimeOnly(10, 0), Zone.EuropeWarsaw);
meeting.ToString();     // 2026-10-24T10:00:00+02:00[Europe/Warsaw]

// Zone has a named value for every IANA zone, so a typo does not compile. It holds the ID, not a number,
// so it is safe across versions. A TimeZoneInfo works too; Zones.Resolve("Europe/Warsaw") is for IDs from configuration.
```
<br/>

* Two kinds of arithmetic

```csharp
meeting.AddDays(7);     // 2026-10-31T10:00:00+01:00[Europe/Warsaw]   same clock time, new offset
meeting.AddHours(24);   // 2026-10-25T09:00:00+01:00[Europe/Warsaw]   24 hours later, the night had 25
```
<br/>

* Same point in time, another zone

```csharp
meeting.ToTimeZone(Zone.AsiaTokyo);   // 2026-10-24T17:00:00+09:00[Asia/Tokyo]
meeting == meeting.ToUtc();           // true, equality is by point in time
```
<br/>

* Clock change (DST): a time the clock skipped, or showed twice

```csharp
// 29 March, clocks go from 02:00 straight to 03:00: there is no 02:30. Read with the previous offset: 03:30+02:00
new DateTimeZone(new DateOnly(2026, 3, 29), new TimeOnly(2, 30), Zone.EuropeWarsaw);

// 25 October, clocks go back from 03:00 to 02:00: 02:30 happens twice. The new offset means the second one: 02:30+01:00
new DateTimeZone(new DateOnly(2026, 10, 25), new TimeOnly(2, 30), Zone.EuropeWarsaw, ClockChange.NewOffset);

// ClockChange.Throw: an exception instead of a guess
```
<br/>

* No zone recorded? Say so

```csharp
var local = UnzonedDateTime.Parse("2026-10-24T10:00:00");
local.InZone(Zone.EuropeWarsaw); // becomes a point in time only with a zone
local.AssumeUtc();               // or when you know it is UTC
```
<br/>

* Text and JSON

```csharp
DateTimeZone.Parse("2026-10-24T10:00:00+02:00[Europe/Warsaw]");
JsonSerializer.Serialize(meeting);   // "2026-10-24T10:00:00+02:00[Europe/Warsaw]", converters are built in
```

**Extended list:** <br>
1. [Creating a value](./docs/Documentation.md#creating-a-value) from wall time, `DateTimeOffset`, `DateTime`, `TimeProvider`
2. [Equality and ordering](./docs/Documentation.md#equality-and-ordering) by point in time, `SameRepresentation` when the zone matters
3. [Arithmetic](./docs/Documentation.md#arithmetic): point in time vs calendar
4. [Clock change](./docs/Documentation.md#clock-change): `ClockChange.PreviousOffset`, `NewOffset`, `Throw`, Samoa 2011
5. [UnzonedDateTime](./docs/Documentation.md#unzoneddatetime) for Avro `local-timestamp-*`, Iceberg `timestamp`, Parquet `isAdjustedToUTC=false`
6. [Zones](./docs/Documentation.md#zones): `Zone.EuropeWarsaw`, IANA and Windows IDs, aliases, fixed offsets
7. [Text format](./docs/Documentation.md#text-format) and [JSON](./docs/Documentation.md#json)
8. [Limits](./docs/Documentation.md#limits): formats that drop the zone, no tzdata in chiseled images, Windows history
9. [Upgrading from 1.x](./docs/Documentation.md#upgrading-from-1x)

[Full documentation](./docs/Documentation.md)


## Limits worth knowing before you start

- Zones come from the operating system. Chiseled and distroless images have no tzdata; the exception says so.
- Avro `timestamp-*`, Parquet `isAdjustedToUTC=true` and Iceberg `timestamptz` store only the point in time. The zone does not survive a round trip through them.
- 1.x calculated offsets without DST. 2.0 is a rewrite with a different API, do not stay on 1.x.

The rest is in [Limits](./docs/Documentation.md#limits).


## License

MIT. Free for everyone, see [LICENSE](./LICENSE).


## Contribution

If you have an idea, hit a zone that behaves differently than you expected, or found a better way to do something, leave an issue or a pull request.


## Related Work

- [![Nuget](https://img.shields.io/nuget/dt/AvroConvert?label=AvroConvert&logo=nuget)](https://github.com/AdrianStrugala/AvroConvert) – Apache Avro serializer for .NET, from the same author
- [soltechnology.dev](https://soltechnology.dev) – the SolTechnology libraries
