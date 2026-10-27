# SolTechnology.DateTimeZone documentation

```
dotnet add package SolTechnology.DateTimeZone
```

Namespace: `SolTechnology.Time`. Targets: net8.0 and net10.0. No dependencies.

- [The two types](#the-two-types)
- [Creating a value](#creating-a-value)
- [Equality and ordering](#equality-and-ordering)
- [Arithmetic](#arithmetic)
- [Clock change](#clock-change)
- [Changing the zone](#changing-the-zone)
- [UnzonedDateTime](#unzoneddatetime)
- [Zones](#zones)
- [Text format](#text-format)
- [JSON](#json)
- [Limits](#limits)
- [Upgrading from 1.x](#upgrading-from-1x)

## The two types

`DateTimeZone` is a point in time together with the time zone it is seen in. Inside it is a `DateTimeOffset` and a `TimeZoneInfo`, kept consistent: the offset is always the one the zone has at that point in time.

`UnzonedDateTime` is a date and time on a clock with no zone recorded. It is a `DateOnly` and a `TimeOnly`. It has no `Now`, no `ToUtc` and cannot be compared with a `DateTimeZone`. It becomes a point in time only when you give it a zone.

## Creating a value

```csharp
using SolTechnology.Time;

// wall time in a zone; the zone is a Zone name or a TimeZoneInfo, see Zones below
var meeting = new DateTimeZone(new DateOnly(2026, 10, 24), new TimeOnly(10, 0), Zone.EuropeWarsaw);
var warsaw = Zones.Resolve(Zone.EuropeWarsaw);   // TimeZoneInfo, when you need it more than once

// from BCL types
DateTimeZone.FromDateTimeOffset(dto, zone);   // the point in time of dto, shown in zone
DateTimeZone.FromDateTimeOffset(dto);         // a fixed-offset zone, because an offset is not a zone
DateTimeZone.FromUtc(utcDateTime, zone);
DateTimeZone.FromDateTime(dateTime);          // Kind.Utc or Kind.Local only

// now
DateTimeZone.Now();                           // TimeZoneInfo.Local
DateTimeZone.UtcNow();
DateTimeZone.Now(timeProvider);               // provider.LocalTimeZone and provider.GetUtcNow()
```

`FromDateTime` throws for `Kind.Unspecified`. Nobody knows the zone of such a value, so the library does not guess. Say it yourself:

```csharp
UnzonedDateTime.FromDateTime(dateTime).InZone(warsaw);
UnzonedDateTime.FromDateTime(dateTime).AssumeUtc();
```

A `Kind.Local` value in the repeated hour of a DST change is converted through `ToUniversalTime()`, which keeps the hidden "ambiguous daylight" bit of `DateTime`. A `Kind.Local` value that went through a database or a serializer has already lost that bit.

Reading back: `ToDateTimeOffset()`, `UtcDateTime`, `Date`, `Time`, `Offset`, `Zone`, `ToUnzoned()`.

## Equality and ordering

`==`, `<`, `CompareTo` and `GetHashCode` compare the point in time, like `DateTimeOffset`. 10:00 in Warsaw and 08:00 in UTC are equal and take one slot in a `HashSet`.

```csharp
var a = new DateTimeZone(new DateOnly(2026, 7, 1), new TimeOnly(10, 0), warsaw);
var b = new DateTimeZone(new DateOnly(2026, 7, 1), new TimeOnly(8, 0), Zones.Utc);

a == b;                        // true
a.SameRepresentation(b);       // false, different zone
new HashSet<DateTimeZone>(DateTimeZoneComparer.Representation) { a, b }.Count;   // 2
```

So a `Dictionary<DateTimeZone, T>` or `Distinct()` keeps the zone of whichever element came first. When the zone matters, use `SameRepresentation` or `DateTimeZoneComparer.Representation`. Zone IDs are compared in canonical form: `Etc/UTC` and `UTC` are the same representation, `Europe/Kiev` and `Europe/Kyiv` too.

## Arithmetic

Two kinds, named differently on purpose.

Point-in-time arithmetic moves the point in time itself. The offset is read again from the zone afterwards.

```csharp
meeting.AddHours(24);      // 2026-10-25T09:00:00+01:00[Europe/Warsaw], the night had 25 hours
meeting.Add(TimeSpan.FromMinutes(90));
meeting + TimeSpan.FromDays(1);
meeting.AddMinutes(1); meeting.AddSeconds(1); meeting.AddMilliseconds(1); meeting.AddTicks(1);
```

Calendar arithmetic moves the date on the clock in the zone. If the result lands in a clock change, the optional `ClockChange` argument decides (see below).

```csharp
meeting.AddDays(7);        // 2026-10-31T10:00:00+01:00[Europe/Warsaw], same time on the clock, new offset
meeting.AddMonths(1);
meeting.AddYears(1);
meeting.AtTime(new TimeOnly(15, 30));
```

Consequences: across a DST night `AddDays(1)` is 23 or 25 hours. `AddMonths` clamps to the end of the month, 31 January plus one month is 28 February, so `AddMonths(1).AddMonths(1)` is not always `AddMonths(2)`. `a - b` is the `TimeSpan` between the two points in time.

## Clock change

Twice a year the clock in a DST zone changes its UTC offset. Europe/Warsaw in 2026: on 29 March it goes from +01:00 to +02:00, on 25 October back to +01:00. On those nights a time on the clock is either skipped (29 March, 02:00 jumps to 03:00, there is no 02:30) or shown twice (25 October, 03:00 goes back to 02:00, 02:30 happens at +02:00 and again at +01:00). The same happens, once, when a country changes its base offset.

Every operation that starts from a time on the clock (the constructor, `InZone`, calendar arithmetic, `Parse` of a text without an offset) takes an optional `ClockChange` that says which offset to read such a time with.

| `ClockChange` | Skipped 02:30 (29 March) | Repeated 02:30 (25 October) |
|---|---|---|
| `PreviousOffset` (default) | read as 02:30+01:00, which the new clock shows as **03:30+02:00** | the first one, **02:30+02:00** |
| `NewOffset` | read as 02:30+02:00, which the old clock shows as **01:30+01:00** | the second one, **02:30+01:00** |
| `Throw` | `DateTimeZoneException` | `DateTimeZoneException` |

The default is what Java's `ZonedDateTime` and NodaTime's lenient resolver do, so values agree with them.

```csharp
new DateTimeZone(new DateOnly(2026, 3, 29), new TimeOnly(2, 30), warsaw);
// 03:30+02:00

new DateTimeZone(new DateOnly(2026, 3, 29), new TimeOnly(2, 30), warsaw, ClockChange.NewOffset);
// 01:30+01:00

new DateTimeZone(new DateOnly(2026, 3, 29), new TimeOnly(2, 30), warsaw, ClockChange.Throw);
// DateTimeZoneException: 2026-03-29 02:30:00 does not exist in Europe/Warsaw: the clocks skip it (+01:00 → +02:00).

new DateTimeZone(new DateOnly(2026, 10, 25), new TimeOnly(2, 30), warsaw);
// 02:30+02:00, the first 02:30

new DateTimeZone(new DateOnly(2026, 10, 25), new TimeOnly(2, 30), warsaw, ClockChange.NewOffset);
// 02:30+01:00, the second 02:30

new DateTimeZone(new DateOnly(2026, 10, 25), new TimeOnly(2, 30), warsaw, ClockChange.Throw);
// DateTimeZoneException: 2026-10-25 02:30:00 is ambiguous in Europe/Warsaw: it occurs at +02:00 and again at +01:00.
```

Base-offset changes are covered too, although `TimeZoneInfo.IsInvalidTime` does not report them. Samoa skipped 30 December 2011 when it crossed the date line: 30 December 12:00 in `Pacific/Apia` becomes 31 December 12:00+14:00, or throws with `ClockChange.Throw`.

## Changing the zone

```csharp
meeting.ToTimeZone(Zones.Resolve("Asia/Tokyo"));   // 2026-10-24T17:00:00+09:00[Asia/Tokyo], same point in time
meeting.ToTimeZone(Zones.Utc);
meeting.ToUtc();
```

## UnzonedDateTime

This is what Avro `local-timestamp-*`, Iceberg `timestamp` and Parquet `isAdjustedToUTC=false` store: a date and time, zone unknown.

```csharp
var local = new UnzonedDateTime(2026, 10, 24, 10, 0);
local = UnzonedDateTime.Parse("2026-10-24T10:00:00");
local = UnzonedDateTime.FromDateTime(dateTime);          // any Kind, the Kind is dropped
local = meeting.ToUnzoned();

local.InZone(warsaw);                                     // DateTimeZone
local.InZone(warsaw, ClockChange.Throw);
local.AssumeUtc();

local.AddDays(1); local.AddHours(1); local.Date; local.Time;   // plain calendar and clock arithmetic, no DST
```

`UnzonedDateTime` is a `record struct`, so equality is by date and time.

## Zones

Every method that takes a zone has two overloads: `Zone` and `TimeZoneInfo`. There are no string overloads. A zone named in code is a `Zone` value and a typo does not compile; a name that comes from configuration or data goes through `Zones.Resolve(string)` or `new Zone(string)`, so it fails at that line and not deep inside a calculation.

`Zone` is a `readonly record struct` around the zone ID, with a static member for every canonical IANA zone (450 in tzdata 2026e). It is a name, not a number: `Zone.EuropeWarsaw.Id` is `"Europe/Warsaw"`, equality is by name, and nothing changes when tzdb adds a zone.

```csharp
Zone.EuropeWarsaw;                                  // Europe/Warsaw -> EuropeWarsaw
Zone.AmericaArgentinaBuenosAires;                   // America/Argentina/Buenos_Aires; Etc/GMT+5 -> EtcGmtPlus5, Etc/UTC -> Utc
new Zone(configuredId);                             // a name from outside, checked at Zones.Resolve
Zones.Resolve(Zone.EuropeWarsaw);                   // TimeZoneInfo

Zones.Resolve("Europe/Warsaw");                     // IANA
Zones.Resolve("Central European Standard Time");    // Windows, converted through ICU
Zones.Resolve("Poland");                            // alias, resolved to Europe/Warsaw
Zones.TryResolve(id, out var zone);
Zones.Utc;
Zones.FixedOffset(TimeSpan.FromHours(2));           // ID "UTC+02:00"
```

The named members are generated from tzdb together with the alias table (`dotnet run tools/GenerateZoneLinks.cs`).

Aliases are written in their canonical tzdb name: `Poland` → `Europe/Warsaw`, `Europe/Kiev` → `Europe/Kyiv`, `Etc/UTC` → `UTC`. The link table comes from tzdata 2026e (450 names, 147 aliases) and lives in `ZoneLinks.g.cs`; `dotnet run tools/GenerateZoneLinks.cs` regenerates it. Only renamed and legacy names are aliases. Every location in `zone.tab` keeps its own name, so `Europe/Oslo` is not written as `Europe/Berlin`.

Zone data itself comes from the operating system. There is no bundled tz database.

## Text format

`ToString()` and `Parse` use RFC 9557, the extension of ISO 8601 with the zone in brackets.

```
2026-10-24T10:00:00+02:00[Europe/Warsaw]    zone
2026-10-24T08:00:00Z                        UTC
2026-10-24T10:00:00+02:00                   fixed offset
```

`Parse` accepts more than `ToString` writes:

| Input | Result |
|---|---|
| `…T10:00:00+02:00[Europe/Warsaw]` | that point in time, in that zone |
| `…T10:00:00+01:00[Europe/Warsaw]` (offset wrong for the zone) | throws; `DateTimeZone.Parse(text, OffsetConflict.UseOffset)` keeps the point in time, `OffsetConflict.UseZone` keeps the wall time |
| `…T08:00:00Z[Europe/Warsaw]` | the UTC point in time, shown in Warsaw |
| `…T10:00:00[Europe/Warsaw]` | wall time resolved in Warsaw (`ClockChange.PreviousOffset`) |
| `…T10:00:00` | not a `DateTimeZone`; use `UnzonedDateTime.Parse` |
| `[!Europe/Warsaw]` | the same as without `!` |
| `[u-ca=gregory]` | ignored; `[!u-ca=…]` throws |
| more than 7 fractional digits | truncated to ticks |
| `23:59:60` | throws |

`TryParse`, `IParsable<T>`, `IFormattable` and `ISpanFormattable` are implemented. `ToString(format)` formats the inner `DateTimeOffset`, so all the usual format strings work.

A zone without a usable ID (for example `TimeZoneInfo.Local` reported as `Local`) is written as an offset only, never as `[Local]`.

## JSON

Both types carry a `[JsonConverter]`, so `System.Text.Json` works with no setup, also with source generation (`JsonSerializerContext`) and as dictionary keys.

```csharp
JsonSerializer.Serialize(meeting);   // "2026-10-24T10:00:00+02:00[Europe/Warsaw]"
JsonSerializer.Deserialize<DateTimeZone>("\"2026-10-24T10:00:00+02:00[Europe/Warsaw]\"");
```

There is no Newtonsoft.Json converter in this package.

## Limits

- Avro `timestamp-*`, Parquet `isAdjustedToUTC=true` and Iceberg `timestamptz` store only the point in time. A `DateTimeZone` written there comes back in UTC: same point in time, `==` is true, zone gone. The zone survives only if the data has a separate field for it.
- Zones come from the operating system. Chiseled and distroless .NET images and Alpine have no tzdata; you get a `DateTimeZoneException` that says so. Install `tzdata` or use an `-extra` image.
- Windows keeps less history than tzdb. It does not know that Samoa skipped 30 December 2011, so on Windows that day simply exists. Results for old dates can differ between Windows and Linux.
- In globalization invariant mode, Windows and IANA IDs cannot be converted into each other. On Linux IANA IDs still work; on Windows in invariant mode they do not.
- Before 1972 offsets can differ from other libraries by up to 59 seconds, because .NET rounds historical offsets to whole minutes (Africa/Monrovia 1970: -00:44 in .NET, -00:44:30 in tzdb). Data before 1970 depends on the tzdata build.
- Values that would fall outside the `DateTime` range when shown in the zone throw instead of being clamped (9999-12-31 23:00Z in Pacific/Kiritimati).
- No non-ISO calendars, no `Period`/`Duration`. NodaTime has them.
- NodaTime also has a type called `DateTimeZone` (a zone, not a point in time). With both packages: `using NodaDateTimeZone = NodaTime.DateTimeZone;`.

## Upgrading from 1.x

2.0 is a rewrite. 1.x calculated offsets without DST and assumed UTC for values without a zone, so do not stay on it.

| 1.x | 2.0 |
|---|---|
| `new DateTimeZone(DateTime)` | `DateTimeZone.FromDateTime(dt)` (`Kind.Utc` / `Kind.Local`), or `UnzonedDateTime.FromDateTime(dt).InZone(zone)` |
| `ZoneSelector.…` | `Zone.EuropeWarsaw`, or `Zones.Resolve("Europe/Warsaw")` for IDs from configuration |
| `DateTimeZone.Now`, `UtcNow` (properties) | `Now()`, `UtcNow()`, `Now(TimeProvider)` |
| `Add(TimeOnly)` | `AtTime(TimeOnly)` |
| `Add(DateOnly)` | removed |
| record equality by date, time and zone | equality by point in time; `SameRepresentation` for the old meaning |
