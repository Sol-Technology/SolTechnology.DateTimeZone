# SolTechnology.DateTimeZone 2.0, the plan

Status: 26 Oct 2026. Steps 1-7 are implemented, the release (step 8) waits for the Trusted Publishing setup on nuget.org. Package `SolTechnology.DateTimeZone` 1.0.1 has ~100 downloads, repository `Sol-Technology/SolTechnology.DateTimeZone`, project `SolTechnology.Time`, was net6.0.

What the library promises: **a point in time, together with the time zone it is seen in.** `DateTimeZone` holds the instant (as a `DateTimeOffset`) and the zone, so the offset is always the right one for that instant in that zone, also after arithmetic. A date and time without a recorded zone is a different type, `UnzonedDateTime`, never an implicit UTC.

MIT, free. In the SolTechnology family it is the time model for AvroConvert 4.x (`timestamp-*` / `local-timestamp-*`) and Lake (Parquet `isAdjustedToUTC`, Iceberg `timestamp` vs `timestamptz`).

## Why

`DateTime.Kind` loses the zone on first serialization. `DateTimeOffset` keeps the instant but only an offset: after `AddMonths` across a DST change the offset is wrong for the zone, and there is no zone to fix it with. `DateTimeZone` is `DateTimeOffset` + `TimeZoneInfo`, kept consistent.

This is not a NodaTime competitor (MIT, ~100M downloads, own tzdb). The difference: built on BCL types (`DateTimeOffset`, `TimeZoneInfo`, `DateOnly`, `TimeOnly`), no own tzdb, converts to and from them without ceremony, small API.

Done means: AvroConvert and Lake map their timestamp types to these two types without losing what the format can carry, and no wrong-instant bug is reported in 2.x. Downloads are not the measure.

One limit has to be in the README from day one: Avro `timestamp-*`, Parquet `isAdjustedToUTC=true` and Iceberg `timestamptz` store **only the instant**. A `DateTimeZone` written there comes back in UTC (same instant, `==` is true, zone gone). The zone survives only if the data has a separate field for it.

## Why 2.0 rather than patches

1.x breaks its own promise in four places. Each gives a different instant than the user expects:

| # | Problem | Where | Effect |
|---|---|---|---|
| 1 | Conversions use `BaseUtcOffset`, ignoring DST; `ToUtc()` adds the offset instead of subtracting | `DateTimeZone.Conversion.cs` | Warsaw in July: result 1-3 h off |
| 2 | Arithmetic on wall-clock fields (`Date.AddDays`, `Time.AddHours`) | `DateTimeZone.Arithmetic.cs` | `AddHours(24)` ≠ `AddDays(1)` across a DST night; no decision for a wall time that does not exist or exists twice |
| 3 | `new DateTimeZone(DateTime)` with `Kind.Unspecified` silently assumes UTC | `DateTimeZone.cs` | values from databases and files (almost always `Unspecified`) get the wrong zone without warning |
| 4 | Record equality compares `Date/Time/Zone`; `<`/`>` compare by UTC | `DateTimeZone.cs`, `Arithmetic.cs` | same instant in two zones: `==` false, while `>=` and `<=` are both true |

Also: `ZoneSelector` holds Windows IDs, `Parse` with an offset picks "the first zone with that offset", `AddSeconds` goes through `AddMinutes(s/60)`, net6.0 is out of support, CI actions are deprecated.

## Design decisions

1. **`DateTimeZone` wraps a `DateTimeOffset` and a `TimeZoneInfo`.** `readonly struct` with two fields: `DateTimeOffset _value` (instant + offset) and `TimeZoneInfo _zone`. Invariant, enforced by every constructor and operation: `_value.Offset == _zone.GetUtcOffset(_value.UtcDateTime)`. `Date`, `Time`, `Offset`, `UtcDateTime` come straight from `_value`, so reading them costs nothing. Checked: `GetUtcOffset` is ~74 ns per call, so it is called once per value, not once per property; three properties on a million rows would be ~0.2 s of offsets alone. `ToDateTimeOffset()` returns `_value`. `default` has a `null` zone and is treated as `0001-01-01T00:00Z` in UTC.
2. **Equality and ordering by instant**, like `DateTimeOffset`. Written by hand, not a `record` (a record would compare the zone too). `SameRepresentation(other)` = same instant and same canonical zone ID. `DateTimeZoneComparer.Representation` for dictionaries and `Distinct` that must keep zones apart. Consequence: a `Dictionary<DateTimeZone, …>` or `Distinct()` keeps the zone of whichever element came first. `SameRepresentation` compares canonical IDs, not `TimeZoneInfo.Equals`. Checked: `FindSystemTimeZoneById("Etc/UTC").Equals(TimeZoneInfo.Utc)` is false, `Europe/Kiev` vs `Europe/Kyiv` is false, and `TimeZoneInfo.Local.Id` on macOS is `Poland`. Same zone, three spellings.
3. **Two kinds of arithmetic, named explicitly.**
   - Instant: `Add(TimeSpan)`, `AddHours/Minutes/Seconds/Milliseconds/Ticks`, `+`/`-` with `TimeSpan`. Implemented as `_value.Add(x)` and then re-reading the offset in `_zone`. Always correct across DST.
   - Calendar: `AddDays/AddMonths/AddYears`, `AtTime(TimeOnly)`. Wall clock → `DateOnly`/`TimeOnly` arithmetic → back to an instant through `ZoneResolver` (decision 4).
   - `a - b` → `TimeSpan` by instant. Documented: `a.AddDays(1) - a` is 23 h or 25 h across a DST night; `AddMonths` clamps (31 Jan + 1 month = 28 Feb), so `AddMonths(1).AddMonths(1)` ≠ `AddMonths(2)`.
4. **One place that turns wall time into an instant: `ZoneResolver`.** `Resolve(DateTime wall, TimeZoneInfo, ClockChange) → DateTimeOffset`. Everything that starts from a wall clock goes through it (calendar arithmetic, `UnzonedDateTime.InZone`, the wall-time constructor, `Parse` with a zone). It does **not** use `TimeZoneInfo.ConvertTimeToUtc` or `IsInvalidTime`. Checked: `ConvertTimeToUtc` throws `ArgumentException` on a gap (Warsaw 2026-03-29 02:30) and silently picks standard time on an overlap (Warsaw 2026-10-25 02:30 → 01:30Z, i.e. the second occurrence); `IsInvalidTime(2011-12-30 12:00)` in Pacific/Apia is **false** although Samoa skipped that whole day (offset -10 → +14), and the value round-trips to 29 Dec with no error. DST gaps are reported (Antarctica/Troll's 2-hour gap is), base-offset changes are not. Algorithm:
   1. `before = zone.GetUtcOffset(wall - 1 day as UTC)`, `after = zone.GetUtcOffset(wall + 1 day as UTC)` (plus the offset at `wall` itself).
   2. For each distinct candidate offset `o`: instant `= wall - o`; keep it if `zone.GetUtcOffset(instant) == o`.
   3. One candidate → done. Two → the time is repeated: `PreviousOffset` = the larger offset (first occurrence), `NewOffset` = the smaller (by value, never by index; `GetAmbiguousTimeOffsets` returns ascending), `Throw`. None → the time is skipped: `PreviousOffset` = `wall - before` (lands after the gap, 02:30 → 03:30, Apia 30 Dec 12:00 → 31 Dec 12:00), `NewOffset` = `wall - after` (lands before the gap, 02:30 → 01:30), `Throw`.
   One enum, `ClockChange { PreviousOffset, NewOffset, Throw }`, default `PreviousOffset`. The first draft had two (`GapPolicy` + `OverlapPolicy`); one parameter named after what changes on the clock (the offset) reads better, and the lost combination (throw on a skipped time, first occurrence on a repeated one) was not worth a second parameter. Optional parameter, no global state.
5. **`UnzonedDateTime`**: `readonly record struct (DateOnly Date, TimeOnly Time)`. A date and time whose zone was not recorded. No `Now`, no `ToUtc`, no comparison with `DateTimeZone`. Exits: `InZone(zone, clockChange)` → `DateTimeZone`, `AssumeUtc()`. Entry from the other side: `DateTimeZone.ToUnzoned()`. Calendar and clock arithmetic without DST.
6. **No guessing on input.**
   - `FromDateTimeOffset(dto, zone)` → instant from `dto`, shown in `zone`. `FromDateTimeOffset(dto)` → fixed-offset zone from `Zones.FixedOffset(dto.Offset)` (an offset is not a zone; documented). Checked: `FindSystemTimeZoneById("+02:00")` throws, so fixed-offset zones need their own cached factory with a stable ID, shared by `FromDateTimeOffset` and `Parse`, otherwise `SameRepresentation` fails between them.
   - `FromDateTime(dt)`: `Kind.Utc` → UTC; `Kind.Local` → `dt.ToUniversalTime()` + `TimeZoneInfo.Local`, never wall time + resolver. Checked: `DateTime` keeps a hidden "ambiguous daylight" bit, so `00:30Z.ToLocalTime()` and `01:30Z.ToLocalTime()` are both `02:30` and `==`, yet `ToUniversalTime()` tells them apart. Rebuilding the value from ticks (any database or serializer does) loses the bit. `Kind.Unspecified` → `DateTimeZoneException` pointing to `UnzonedDateTime.FromDateTime(dt).InZone(...)`.
   - `Now(TimeProvider? provider = null)` uses `provider.LocalTimeZone` and `provider.GetUtcNow()`, not `TimeZoneInfo.Local`, so tests with `FakeTimeProvider` do not depend on the machine's zone.
7. **Zones.** `Zones.Resolve(string id)`: IANA or Windows ID, canonical ID out (`Europe/Kiev` → `Europe/Kyiv`, `Poland` → `Europe/Warsaw`, `Etc/UTC` → `UTC`) from a link table generated once from tzdb and committed; .NET has no API for this. Only renamed and legacy names are aliases; every location in `zone.tab` keeps its own name even when tzdb merged its data into another zone (`Europe/Oslo` is not `Europe/Berlin`, `Atlantic/Reykjavik` is not `Africa/Abidjan`; Debian/Ubuntu tzdata keeps their pre-1970 history apart). `TimeZoneInfo.Local` with ID `Local` or a path is never written as `[Local]`, it falls back to the offset. `Zones.FixedOffset(TimeSpan)`: cached instances with ID `UTC+02:00` style. Clear errors: missing tzdata and invariant globalization name the cause and the fix. Checked: with `TZDIR=/nonexistent`, `FindSystemTimeZoneById("Europe/Warsaw")` throws `TimeZoneNotFoundException` and `Local.Id` is `UTC`; with `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`, Windows IDs throw and `TryConvertWindowsIdToIanaId` returns false. No static class with hundreds of `TimeZoneInfo` fields: static readonly fields initialize together, and one missing zone would throw `TypeInitializationException` for the whole class. Instead `Zone`, a `readonly record struct` around the ID with a generated static member per canonical name (`Zone.EuropeWarsaw => new("Europe/Warsaw")`), and `Zones.Resolve(Zone)`: a zone named in code cannot have a typo, nothing runs at type initialization, and a zone missing on the machine fails only when it is asked for. An enum was tried first and dropped: its numbers shift when tzdb adds a zone, so data written with one version would read as another zone in the next.
8. **Text: RFC 9557** (`2026-10-18T10:00:00+02:00[Europe/Warsaw]`). Writing: canonical ID; a zone without a usable ID is written as offset only. `Z` means "instant known, local offset unknown", `+00:00` means "local time is UTC". Reading:
   | Input | Result |
   |---|---|
   | offset + `[zone]`, consistent | `DateTimeZone` in that zone |
   | offset + `[zone]`, offset wrong for the zone at that instant | throw (default); `OffsetConflict.UseOffset` / `UseZone` option |
   | `Z[zone]` | instant in UTC, shown in `zone` |
   | `Z` or offset, no brackets | UTC or a fixed-offset zone |
   | no offset, `[zone]` | wall time resolved in `zone` (decision 4) |
   | neither | `UnzonedDateTime` |
   | `[!zone]` | same as `[zone]` |
   | other annotations (`[u-ca=…]`) | ignored; critical (`[!u-ca=…]`) → throw |
   | more than 7 fractional digits | truncated to ticks |
   | `23:59:60` | throw (Java and PostgreSQL can emit it, `DateTime` cannot hold it) |
9. **Serialization.** `System.Text.Json` converters for both types in the main package, registered with `[JsonConverter]` on the types, including `ReadAsPropertyName`/`WriteAsPropertyName` (a converter without them throws when the type is a dictionary key). Tested with a source-generated `JsonSerializerContext`, since Lake and AvroConvert users publish AOT. Newtonsoft converters and the Avro mapping live in an AvroConvert-side package after 2.0, not here: AvroConvert 4.0 keeps Newtonsoft in its public API, and `Avro2Json` of a model with these types would otherwise serialize the `TimeZoneInfo` object.
10. **Names.** Package and type stay `SolTechnology.DateTimeZone` / `DateTimeZone`. Namespace stays `SolTechnology.Time`. Checked: in `namespace SolTechnology.Avro` with `using SolTechnology.DateTimeZone;`, `DateTimeZone` resolves to the namespace (`CS0118`), and AvroConvert and Lake both live under `SolTechnology.*`. NodaTime also has a `DateTimeZone` (a zone, not an instant); the README shows `using NodaDateTimeZone = NodaTime.DateTimeZone;` for projects with both. `ZonedDateTime` was rejected as the new name for the same clash.
11. **Targets `net8.0;net10.0`**, `Nullable`, `TreatWarningsAsErrors`. No PublicApiAnalyzers until 2.0 stable.

Known limits, documented rather than fixed:
- Offsets before 1972 can differ from tzdb by seconds. Checked: .NET truncates to whole minutes, Africa/Monrovia 1970 is -00:44 in .NET and -00:44:30 in tzdb. Java, Python and NodaTime will disagree by up to 59 s.
- Pre-1970 data depends on the tzdata build. Checked: Europe/Amsterdam in 1900 gives +00:00 on this machine (tzdb merged Amsterdam into Brussels in 2022, the original +00:19:32 lives only in `backzone`). The same instant can render differently on two servers.
- Windows has fewer historical rules than tzdb. Checked in CI: on `windows-latest` Pacific/Apia has no 2011 date-line change, so 30 Dec 2011 exists there. The Apia test is skipped when the system's data has no jump.
- Range ends: `ConvertTimeFromUtc(9999-12-31 23:00Z, Pacific/Kiritimati)` silently returns `9999-12-31 23:59:59`. The constructor checks that the wall time stays inside `DateTime` range and throws instead.

Out of scope for 2.0: non-ISO calendars, `Period`/`Duration`, bundled tzdb, Roslyn analyzer, NodaTime converters.

## Steps

### 0. Transfer the repository to the organisation, done 26 Oct 2026
- [x] Transferred to `Sol-Technology/SolTechnology.DateTimeZone`; remote, `RepositoryUrl`, `PackageProjectUrl`, README badge updated.
- [ ] Publishing: `publish.yml` written (tag `v*`, environment `nuget`, repo variable `NUGET_USER`). To do on nuget.org and GitHub: Trusted Publishing policy (owner `Sol-Technology`, repository `SolTechnology.DateTimeZone`, workflow `publish.yml`, environment `nuget`), environment `nuget` and variable `NUGET_USER` in the repository settings.

### 1. Project housekeeping
- [x] Project and solution stay `SolTechnology.Time` (namespace), `PackageId`/`AssemblyName` stay `SolTechnology.DateTimeZone`. Solution file `SolTechnology.Time.slnx` (migrated from `.sln`).
- [x] `TargetFrameworks` `net8.0;net10.0`; `Version` 2.0.0-preview.1; MIT; `PackageReadmeFile`; `Deterministic`, `ContinuousIntegrationBuild`, SourceLink; `GenerateDocumentationFile`.
- [x] Remove `todo.cs`, `TestTest.cs`, `.github/runTests.ps1`.
- [x] Tests: NUnit + AwesomeAssertions, the same stack as AvroConvert (the first draft had xunit + FluentAssertions 7 and an API-key push).
- [x] CI `build.yml`: build + test on PR and `main`, matrix `ubuntu-latest`, `windows-latest`, `macos-latest`; one job with `InvariantGlobalization=true`; one job with `TZDIR` pointing to an empty directory instead of a container without tzdata (same effect, checked locally: .NET finds no zones and the error names tzdata); NodaTime oracle as a non-blocking job. `publish.yml`: pack + push on `v*` tags only.
- [x] README as a short card (why, comparison table, six snippets), details in `docs/Documentation.md`.
- [x] Icon and logo from the SolTechnology brand (`sol-technology.github.io/scripts/brand-datetimezone.py`): chevrons with a clock dial, `docs/icon.png` in the package, `docs/logo.png` in the README.

### 2. Core
- [x] `DateTimeZone` struct (decision 1), constructors: `(DateOnly, TimeOnly, TimeZoneInfo, ClockChange)`, `FromDateTimeOffset(dto[, zone])`, `FromDateTime`, `FromUtc(DateTime utc, TimeZoneInfo)`, `Now(TimeProvider?)`. Range check: wall time must stay within `DateTime` range, no silent clamping.
- [x] Equality, `GetHashCode`, `IComparable<DateTimeZone>`, operators by instant; `SameRepresentation`; `DateTimeZoneComparer.Representation`.
- [x] `ToTimeZone(TimeZoneInfo)`, `ToUtc()`: same point in time, other zone.
- [x] Zone parameters are `Zone` (a name) or `TimeZoneInfo`, no string overloads (constructor, `FromDateTimeOffset`, `ToTimeZone`, `InZone`). Strings go through `Zones.Resolve`, so a typo fails at the resolve call and not inside a calculation.
- [x] Remove `Add(TimeOnly)`/`Add(DateOnly)` (their arguments are not quantities); `AtTime(TimeOnly)` replaces the first.

### 3. `ZoneResolver` and arithmetic
- [x] `ZoneResolver.Resolve` (decision 4), `ClockChange`.
- [x] Instant and calendar arithmetic (decision 3).

### 4. `UnzonedDateTime` (decision 5)
- [x] Text without offset and zone is read by `UnzonedDateTime.Parse`; `DateTimeZone.Parse` rejects it with a hint (a parser cannot return two types).

### 5. Zones (decision 7)
- [x] `tools/GenerateZoneLinks.cs` (`dotnet run tools/GenerateZoneLinks.cs`): reads tzdata-latest (`Zone`/`Link` lines and `zone.tab`), writes `ZoneLinks.g.cs` and `Zone.g.cs` (the named `Zone` members); committed, re-run when tzdb changes. tzdata 2026e: 450 names, 147 aliases.
- [x] `Zones.Resolve`, `Zones.FixedOffset`, `Zones.Utc`. Remove `ZoneSelector`.

### 6. Text (decision 8)
- [x] Bracket contents are checked against the RFC 9557 grammar (zone name or offset; annotation key and value), so a malformed suffix is an error instead of a silently dropped zone.
- [x] `ToString()`, `Parse`/`TryParse` for both types; `IParsable<T>`, `IFormattable`, `ISpanFormattable`. `ToString(format)` formats `_value` (the `DateTimeOffset`).
- [x] STJ converters (decision 9), test with a source-generated `JsonSerializerContext`.

### 7. Tests that must pass: 128 unit tests on net8.0 and net10.0, 22 oracle cases (≈50 000 wall times around transitions agree with NodaTime, tzdb 2026e)
- [x] Skipped time: Warsaw 2026-03-29 02:30 → `PreviousOffset` 03:30+02:00, `NewOffset` 01:30+01:00, `Throw` throws. Antarctica/Troll 2026-03-29 01:30 (2-hour gap). **Pacific/Apia 2011-12-30 12:00** (skipped day) → 2011-12-31 12:00+14:00.
- [x] Repeated time: Warsaw 2026-10-25 02:30 → `PreviousOffset` +02:00, `NewOffset` +01:00. Australia/Lord_Howe (30-minute DST). America/Santiago (southern hemisphere).
- [x] `AddHours(24)` vs `AddDays(1)` across both Warsaw nights: different instants. `AddMonths` from 31 Jan.
- [x] Equality: `new(10:00, Warsaw) == new(08:00, UTC)`, one element in a `HashSet`; `SameRepresentation` false; `Etc/UTC` and `UTC` are the same representation; `Europe/Kiev` and `Europe/Kyiv` too.
- [x] `FromDateTime(Kind.Local)` for both 02:30s of the overlap (via `ToLocalTime()`), each keeps its instant. `Kind.Unspecified` throws with the hint.
- [x] Round-trip `ToString()`/`Parse` for IANA zone, fixed offset, UTC, and every row of the table in decision 8.
- [x] `default(DateTimeZone)` members do not throw.
- [x] Range: 9999-12-31 in Pacific/Kiritimati throws instead of clamping.
- [x] Missing tzdata and invariant globalization: clear error messages (CI jobs from step 1).
- [x] NodaTime as an oracle (separate, non-blocking test project): every transition 1972-2025 in 22 zones without rule changes in the last 5 years, ±3 h around it in 15-minute steps; compare the instant. NodaTime bundles its own tzdb and the runners use the OS one, so recently changed zones (Mexico 2022, Greenland 2023, Kazakhstan 2024, Paraguay 2024) are not in the list.

### 8. Release
- [ ] `2.0.0` straight away, without a preview (decision 27 Oct 2026): tag `v2.0.0`, notes in `docs/release-notes/v2.0.0.md`. Package icon on a white background, like the GitHub and Polar avatars.
- [ ] Deprecate 1.0.1 on NuGet with a link to 2.0 ("API rewrite; 1.x has DST bugs").
- [ ] AvroConvert 4.x / Lake: mapping package (`timestamp-*` ↔ `DateTimeZone` in UTC, `local-timestamp-*` ↔ `UnzonedDateTime`), separate plan. A NuGet package cannot have an optional dependency, so it is an adapter (`SolTechnology.Avro.Time` or similar), AvroConvert core stays without it (#100 asks for fewer dependencies). The adapter's round-trip test should assert `SameRepresentation` is **false** after writing to an instant-only format, so the loss of the zone is explicit. The mapping that adds real value is `local-timestamp-*` / Iceberg `timestamp` / Parquet `isAdjustedToUTC=false` ↔ `UnzonedDateTime`: today those come back as `DateTime` `Kind.Unspecified` and the user guesses.

Estimate was: step 1 one evening; 2-4 three evenings; 5-7 two to three evenings; 8 half a day. Actual: steps 1-7 in one day on 26 Oct 2026.

## Risks and when to stop

- The real driver is Lake (Iceberg `timestamp` vs `timestamptz`, Parquet `isAdjustedToUTC`), not AvroConvert issues. Checked: 94 AvroConvert issues in total, ~5 about dates (#21 nullable DateTime, #69 BigQuery logical type, #108 rounding, #153 millis vs micros, #118 ADX), none about zones or DST. 4.0 already writes `DateTimeOffset` as an instant. So "fewer date-related issues" cannot be the measure, the baseline is ~0.
- AvroConvert may never take the dependency (#100, and 4.0 already handles the instant). Then 2.0 stays a small MIT library with a few dozen downloads. That is acceptable; the cost was one day.
- If Lake gets a no-go and there is no AvroConvert issue about zones: ship preview.1 as is, no further work.
- More than 8 evenings before preview.1: stop, cut scope to `UnzonedDateTime` + the instant-based core. (Not triggered.)
