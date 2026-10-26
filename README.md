<p align="center">
    <img alt="SolTechnology-logo" src="./docs/logo.png" width="200">
</p>

<h2 align="center">
  SolTechnology.DateTimeZone
</h2>

<p align="center">
 <a href="https://www.nuget.org/packages/SolTechnology.DateTimeZone"><img src="https://img.shields.io/nuget/vpre/SolTechnology.DateTimeZone?logo=nuget"></a>
 <a href="https://github.com/Sol-Technology/SolTechnology.DateTimeZone/actions/workflows/build.yml"><img src="https://github.com/Sol-Technology/SolTechnology.DateTimeZone/actions/workflows/build.yml/badge.svg"></a>
</p>

**A point in time, together with the time zone it is seen in.** `DateTimeZone` holds a `DateTimeOffset` and a `TimeZoneInfo` and keeps them consistent: the offset is always the right one for that instant in that zone, also after `AddDays` across a DST night. A date and time without a recorded zone is a different type, `UnzonedDateTime`, never an implicit UTC.

```
dotnet add package SolTechnology.DateTimeZone --prerelease
```

```csharp
using SolTechnology.Time;

var meeting = new DateTimeZone(new DateOnly(2026, 10, 24), new TimeOnly(10, 0), "Europe/Warsaw");
meeting.ToString();               // 2026-10-24T10:00:00+02:00[Europe/Warsaw]

var nextWeek = meeting.AddDays(7); // 2026-10-31T10:00:00+01:00[Europe/Warsaw], same time on the clock, new offset
var in24h = meeting.AddHours(24);  // 2026-10-25T09:00:00+01:00[Europe/Warsaw], 24 hours later (the night had 25)

meeting.ToTimeZone("Asia/Tokyo");  // same instant: 2026-10-24T17:00:00+09:00[Asia/Tokyo]
meeting == meeting.ToUtc();        // true, equality is by instant
```

## The rules

- **The instant is the truth.** `==`, `<`, `GetHashCode` compare instants, like `DateTimeOffset`. `SameRepresentation` (or `DateTimeZoneComparer.Representation`) also compares the zone.
- **Two kinds of arithmetic.** `AddHours`, `AddMinutes`, `Add(TimeSpan)` move the instant. `AddDays`, `AddMonths`, `AddYears`, `AtTime` move the date on the clock in the zone. Across a DST night `AddDays(1)` is 23 or 25 hours.
- **No guessing.** `DateTimeZone.FromDateTime` takes `Kind.Utc` and `Kind.Local`; `Kind.Unspecified` throws, because the zone is not known. Use `UnzonedDateTime.FromDateTime(value).InZone(zone)`.
- **Times that don't exist or exist twice** (DST changes, Samoa skipping 30 December 2011) are resolved by `GapPolicy` (`ShiftForward` by default, or `Throw`) and `OverlapPolicy` (`Earlier` by default, `Later`, `Throw`).

```csharp
new DateTimeZone(new DateOnly(2026, 3, 29), new TimeOnly(2, 30), "Europe/Warsaw");   // 03:30+02:00, the clocks skip 02:00-03:00
new DateTimeZone(new DateOnly(2026, 10, 25), new TimeOnly(2, 30), "Europe/Warsaw",
    overlap: OverlapPolicy.Later);                                                     // 02:30+01:00, the second 02:30
```

## UnzonedDateTime

A date and time on a clock, without a zone. This is what Avro `local-timestamp-*`, Iceberg `timestamp` and Parquet `isAdjustedToUTC=false` store.

```csharp
var local = UnzonedDateTime.Parse("2026-10-24T10:00:00");
local.InZone("Europe/Warsaw");   // becomes an instant only with a zone
local.AssumeUtc();               // or when you know it is UTC
```

## Text and JSON

RFC 9557: `2026-10-24T10:00:00+02:00[Europe/Warsaw]`. UTC is written as `…Z`, a fixed offset as `…+02:00`. `Parse` accepts `…Z[Europe/Warsaw]` (an instant seen in the zone) and `…T10:00[Europe/Warsaw]` (a wall time in the zone). If the offset is wrong for the zone, `Parse` throws; `OffsetConflict.UseOffset` / `UseZone` choose one side instead.

Both types have `System.Text.Json` converters built in (also as dictionary keys and with source generation).

Zone IDs: IANA (`Europe/Warsaw`) or Windows (`Central European Standard Time`). Aliases are written in their canonical form (`Poland` → `Europe/Warsaw`, `Europe/Kiev` → `Europe/Kyiv`).

## Limits

- Avro `timestamp-*`, Parquet `isAdjustedToUTC=true` and Iceberg `timestamptz` store only the instant. A `DateTimeZone` written there comes back in UTC: same instant, zone gone.
- Zones come from the operating system (no bundled tz database). Chiseled and distroless .NET images and Alpine have no tzdata; you get an exception that says so. Install `tzdata` or use an `-extra` image.
- In globalization invariant mode, Windows and IANA IDs cannot be converted into each other.
- Before 1972 offsets can differ from other libraries by seconds (.NET rounds them to minutes), and data before 1970 depends on the tzdata build.
- NodaTime also has a type called `DateTimeZone` (a zone, not an instant). With both packages: `using NodaDateTimeZone = NodaTime.DateTimeZone;`.

## Version 1.x

2.0 is a rewrite. 1.x calculated offsets without DST and assumed UTC for values without a zone, so do not stay on it.

MIT licence.
