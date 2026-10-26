# SolTechnology.DateTimeZone documentation

```
dotnet add package SolTechnology.DateTimeZone --prerelease
```

Namespace: `SolTechnology.Time`. Targets: net8.0 and net10.0. No dependencies.

- [The two types](#the-two-types)
- [Creating a value](#creating-a-value)
- [Equality and ordering](#equality-and-ordering)
- [Arithmetic](#arithmetic)
- [Gaps and overlaps](#gaps-and-overlaps)
- [Changing the zone](#changing-the-zone)
- [UnzonedDateTime](#unzoneddatetime)
- [Zones](#zones)
- [Text format](#text-format)
- [JSON](#json)
- [Limits](#limits)
- [Upgrading from 1.x](#upgrading-from-1x)

## The two types

`DateTimeZone` is a point in time together with the time zone it is seen in. Inside it is a `DateTimeOffset` and a `TimeZoneInfo`, kept consistent: the offset is always the one the zone has at that instant.

`UnzonedDateTime` is a date and time on a clock with no zone recorded. It is a `DateOnly` and a `TimeOnly`. It has no `Now`, no `ToUtc` and cannot be compared with a `DateTimeZone`. It becomes an instant only when you give it a zone.

## Creating a value

```csharp
using SolTechnology.Time;

// wall time in a zone
var meeting = new DateTimeZone(new DateOnly(2026, 10, 24), new TimeOnly(10, 0), "Europe/Warsaw");
var same = new DateTimeZone(new DateOnly(2026, 10, 24), new TimeOnly(10, 0), TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw"));

// from BCL types
DateTimeZone.FromDateTimeOffset(dto, zone);   // the instant of dto, shown in zone
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
UnzonedDateTime.FromDateTime(dateTime).InZone("Europe/Warsaw");
UnzonedDateTime.FromDateTime(dateTime).AssumeUtc();
```

A `Kind.Local` value in the repeated hour of a DST change is converted through `ToUniversalTime()`, which keeps the hidden "ambiguous daylight" bit of `DateTime`. A `Kind.Local` value that went through a database or a serializer has already lost that bit.

Reading back: `ToDateTimeOffset()`, `UtcDateTime`, `Date`, `Time`, `Offset`, `Zone`, `ToUnzoned()`.

## Equality and ordering

`==`, `<`, `CompareTo` and `GetHashCode` compare the instant, like `DateTimeOffset`. 10:00 in Warsaw and 08:00 in UTC are equal and take one slot in a `HashSet`.

```csharp
var warsaw = new DateTimeZone(new DateOnly(2026, 7, 1), new TimeOnly(10, 0), "Europe/Warsaw");
var utc = new DateTimeZone(new DateOnly(2026, 7, 1), new TimeOnly(8, 0), "UTC");

warsaw == utc;                        // true
warsaw.SameRepresentation(utc);       // false, different zone
new HashSet<DateTimeZone>(DateTimeZoneComparer.Representation) { warsaw, utc }.Count;   // 2
```

So a `Dictionary<DateTimeZone, T>` or `Distinct()` keeps the zone of whichever element came first. When the zone matters, use `SameRepresentation` or `DateTimeZoneComparer.Representation`. Zone IDs are compared in canonical form: `Etc/UTC` and `UTC` are the same representation, `Europe/Kiev` and `Europe/Kyiv` too.

## Arithmetic

Two kinds, named differently on purpose.

Instant arithmetic moves the point in time. The offset is read again from the zone afterwards.

```csharp
meeting.AddHours(24);      // 2026-10-25T09:00:00+01:00[Europe/Warsaw], the night had 25 hours
meeting.Add(TimeSpan.FromMinutes(90));
meeting + TimeSpan.FromDays(1);
meeting.AddMinutes(1); meeting.AddSeconds(1); meeting.AddMilliseconds(1); meeting.AddTicks(1);
```

Calendar arithmetic moves the date on the clock in the zone and resolves the result through the gap and overlap policies.

```csharp
meeting.AddDays(7);        // 2026-10-31T10:00:00+01:00[Europe/Warsaw], same time on the clock, new offset
meeting.AddMonths(1);
meeting.AddYears(1);
meeting.AtTime(new TimeOnly(15, 30));
```

Consequences: across a DST night `AddDays(1)` is 23 or 25 hours. `AddMonths` clamps to the end of the month, 31 January plus one month is 28 February, so `AddMonths(1).AddMonths(1)` is not always `AddMonths(2)`. `a - b` is a `TimeSpan` between the instants.

## Gaps and overlaps

When the clocks move forward, some wall times do not exist. When they move back, some exist twice. Every operation that starts from a wall time (the constructor, `InZone`, calendar arithmetic, `Parse` of a text without an offset) takes two optional policies.

| Policy | Values | Default |
|---|---|---|
| `GapPolicy` | `ShiftForward` (move forward by the length of the gap), `Throw` | `ShiftForward` |
| `OverlapPolicy` | `Earlier` (the first occurrence), `Later`, `Throw` | `Earlier` |

```csharp
new DateTimeZone(new DateOnly(2026, 3, 29), new TimeOnly(2, 30), "Europe/Warsaw");
// 03:30+02:00, the clocks skip 02:00-03:00

new DateTimeZone(new DateOnly(2026, 3, 29), new TimeOnly(2, 30), "Europe/Warsaw", gap: GapPolicy.Throw);
// DateTimeZoneException: 2026-03-29 02:30:00 does not exist in Europe/Warsaw: the clocks skip it (+01:00 → +02:00).

new DateTimeZone(new DateOnly(2026, 10, 25), new TimeOnly(2, 30), "Europe/Warsaw");
// 02:30+02:00, the first 02:30

new DateTimeZone(new DateOnly(2026, 10, 25), new TimeOnly(2, 30), "Europe/Warsaw", overlap: OverlapPolicy.Later);
// 02:30+01:00, the second 02:30
```

This also covers changes of the base offset, which `TimeZoneInfo.IsInvalidTime` does not report. Samoa skipped 30 December 2011 when it crossed the date line; 30 December 12:00 in `Pacific/Apia` becomes 31 December 12:00+14:00, or throws with `GapPolicy.Throw`.

## Changing the zone

```csharp
meeting.ToTimeZone("Asia/Tokyo");   // 2026-10-24T17:00:00+09:00[Asia/Tokyo], same instant
meeting.ToTimeZone(TimeZoneInfo.Utc);
meeting.ToUtc();
```

## UnzonedDateTime

This is what Avro `local-timestamp-*`, Iceberg `timestamp` and Parquet `isAdjustedToUTC=false` store: a date and time, zone unknown.

```csharp
var local = new UnzonedDateTime(2026, 10, 24, 10, 0);
local = UnzonedDateTime.Parse("2026-10-24T10:00:00");
local = UnzonedDateTime.FromDateTime(dateTime);          // any Kind, the Kind is dropped
local = meeting.ToUnzoned();

local.InZone("Europe/Warsaw");                            // DateTimeZone
local.InZone(zone, GapPolicy.Throw, OverlapPolicy.Later);
local.AssumeUtc();

local.AddDays(1); local.AddHours(1); local.Date; local.Time;   // plain calendar and clock arithmetic, no DST
```

`UnzonedDateTime` is a `record struct`, so equality is by date and time.

## Zones

A zone is a `TimeZoneInfo`. Every method that takes a zone also has an overload that takes an ID.

```csharp
Zones.Resolve("Europe/Warsaw");                     // IANA
Zones.Resolve("Central European Standard Time");    // Windows, converted through ICU
Zones.Resolve("Poland");                            // alias, resolved to Europe/Warsaw
Zones.TryResolve(id, out var zone);
Zones.Utc;
Zones.FixedOffset(TimeSpan.FromHours(2));           // ID "UTC+02:00"
```

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
| `…T10:00:00+02:00[Europe/Warsaw]` | that instant, in that zone |
| `…T10:00:00+01:00[Europe/Warsaw]` (offset wrong for the zone) | throws; `DateTimeZone.Parse(text, OffsetConflict.UseOffset)` keeps the instant, `OffsetConflict.UseZone` keeps the wall time |
| `…T08:00:00Z[Europe/Warsaw]` | the UTC instant, shown in Warsaw |
| `…T10:00:00[Europe/Warsaw]` | wall time resolved in Warsaw (gap and overlap policies apply) |
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

- Avro `timestamp-*`, Parquet `isAdjustedToUTC=true` and Iceberg `timestamptz` store only the instant. A `DateTimeZone` written there comes back in UTC: same instant, `==` is true, zone gone. The zone survives only if the data has a separate field for it.
- Zones come from the operating system. Chiseled and distroless .NET images and Alpine have no tzdata; you get a `DateTimeZoneException` that says so. Install `tzdata` or use an `-extra` image.
- Windows keeps less history than tzdb. It does not know that Samoa skipped 30 December 2011, so on Windows that day simply exists. Results for old dates can differ between Windows and Linux.
- In globalization invariant mode, Windows and IANA IDs cannot be converted into each other. On Linux IANA IDs still work; on Windows in invariant mode they do not.
- Before 1972 offsets can differ from other libraries by up to 59 seconds, because .NET rounds historical offsets to whole minutes (Africa/Monrovia 1970: -00:44 in .NET, -00:44:30 in tzdb). Data before 1970 depends on the tzdata build.
- Values that would fall outside the `DateTime` range when shown in the zone throw instead of being clamped (9999-12-31 23:00Z in Pacific/Kiritimati).
- No non-ISO calendars, no `Period`/`Duration`. NodaTime has them.
- NodaTime also has a type called `DateTimeZone` (a zone, not an instant). With both packages: `using NodaDateTimeZone = NodaTime.DateTimeZone;`.

## Upgrading from 1.x

2.0 is a rewrite. 1.x calculated offsets without DST and assumed UTC for values without a zone, so do not stay on it.

| 1.x | 2.0 |
|---|---|
| `new DateTimeZone(DateTime)` | `DateTimeZone.FromDateTime(dt)` (`Kind.Utc` / `Kind.Local`), or `UnzonedDateTime.FromDateTime(dt).InZone(zone)` |
| `ZoneSelector.…` | IANA IDs or `Zones.Resolve` |
| `DateTimeZone.Now`, `UtcNow` (properties) | `Now()`, `UtcNow()`, `Now(TimeProvider)` |
| `Add(TimeOnly)` | `AtTime(TimeOnly)` |
| `Add(DateOnly)` | removed |
| record equality by date, time and zone | equality by instant; `SameRepresentation` for the old meaning |
