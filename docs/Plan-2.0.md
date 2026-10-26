# SolTechnology.DateTimeZone 2.0 – plan

Status: 26 Oct 2026 (rewritten after the premortem and design review in the appendices). Package `SolTechnology.DateTimeZone` 1.0.1 (~100 downloads), repository `Sol-Technology/SolTechnology.DateTimeZone`, project `SolTechnology.Time`, net6.0.

The promise: **a point in time, together with the time zone it is seen in.** `DateTimeZone` holds the instant (as a `DateTimeOffset`) and the zone, so the offset is always the right one for that instant in that zone, also after arithmetic. A date and time without a recorded zone is a different type, `UnzonedDateTime`, never an implicit UTC.

MIT, free. Role in the SolTechnology family: the time model for AvroConvert 4.x (`timestamp-*` / `local-timestamp-*`) and Lake (Parquet `isAdjustedToUTC`, Iceberg `timestamp` vs `timestamptz`).

## Purpose and expectations

`DateTime.Kind` loses the zone on first serialization. `DateTimeOffset` keeps the instant but only an offset: after `AddMonths` across a DST change the offset is wrong for the zone, and there is no zone to fix it with. `DateTimeZone` is `DateTimeOffset` + `TimeZoneInfo`, kept consistent.

Not a NodaTime competitor (MIT, ~100M downloads, own tzdb). The difference: built on BCL types (`DateTimeOffset`, `TimeZoneInfo`, `DateOnly`, `TimeOnly`), no own tzdb, converts to and from them without ceremony, small API.

Success metric: AvroConvert and Lake map their timestamp types to these two types without loss of what the format can carry, and no wrong-instant bug is reported in 2.x. Not downloads.

Honest limit, to state in the README: Avro `timestamp-*`, Parquet `isAdjustedToUTC=true` and Iceberg `timestamptz` store **only the instant**. A `DateTimeZone` written there comes back in UTC (same instant, `==` is true, zone gone). The zone survives only if the data has a separate field for it.

## Why 2.0 rather than patches

The current code breaks its own promise in four places; each yields a different instant than the user expects:

| # | Problem | Where | Effect |
|---|---|---|---|
| 1 | Conversions use `BaseUtcOffset`, ignoring DST; `ToUtc()` adds the offset instead of subtracting | `DateTimeZone.Conversion.cs` | Warsaw in July: result 1–3 h off |
| 2 | Arithmetic on wall-clock fields (`Date.AddDays`, `Time.AddHours`) | `DateTimeZone.Arithmetic.cs` | `AddHours(24)` ≠ `AddDays(1)` across a DST night; no decision for a wall time that does not exist or exists twice |
| 3 | `new DateTimeZone(DateTime)` with `Kind.Unspecified` silently assumes UTC | `DateTimeZone.cs` | values from databases and files (almost always `Unspecified`) get the wrong zone without warning |
| 4 | Record equality compares `Date/Time/Zone`; `<`/`>` compare by UTC | `DateTimeZone.cs`, `Arithmetic.cs` | same instant in two zones: `==` false, while `>=` and `<=` are both true |

Also: `ZoneSelector` holds Windows IDs, `Parse` with an offset picks "the first zone with that offset", `AddSeconds` goes through `AddMinutes(s/60)`, net6.0 is out of support, CI actions are deprecated.

## Design decisions

1. **`DateTimeZone` wraps a `DateTimeOffset` and a `TimeZoneInfo`.** `readonly struct` with two fields: `DateTimeOffset _value` (instant + offset) and `TimeZoneInfo _zone`. Invariant, enforced by every constructor and operation: `_value.Offset == _zone.GetUtcOffset(_value.UtcDateTime)`. `Date`, `Time`, `Offset`, `UtcDateTime` come straight from `_value`, so reading them costs nothing (review B1: `GetUtcOffset` is ~74 ns, called once per value instead of per property). `ToDateTimeOffset()` returns `_value`. `default` has a `null` zone and is treated as `0001-01-01T00:00Z` in UTC.
2. **Equality and ordering by instant**, like `DateTimeOffset`. Written by hand, not a `record` (a record would compare the zone too). `SameRepresentation(other)` = same instant and same canonical zone ID. `DateTimeZoneComparer.Representation` for dictionaries and `Distinct` that must keep zones apart.
3. **Two kinds of arithmetic, named explicitly.**
   - Instant: `Add(TimeSpan)`, `AddHours/Minutes/Seconds/Milliseconds/Ticks`, `+`/`-` with `TimeSpan`. Implemented as `_value.Add(x)` and then re-reading the offset in `_zone`. Always correct across DST.
   - Calendar: `AddDays/AddMonths/AddYears`, `AtTime(TimeOnly)`. Wall clock → `DateOnly`/`TimeOnly` arithmetic → back to an instant through `ZoneResolver` (decision 4).
   - `a - b` → `TimeSpan` by instant. Documented: `a.AddDays(1) - a` is 23 h or 25 h across a DST night; `AddMonths` clamps (31 Jan + 1 month = 28 Feb), so `AddMonths(1).AddMonths(1)` ≠ `AddMonths(2)`.
4. **One place that turns wall time into an instant: `ZoneResolver`.** `Resolve(DateOnly, TimeOnly, TimeZoneInfo, GapPolicy, OverlapPolicy) → DateTimeOffset`. Everything that starts from a wall clock goes through it (calendar arithmetic, `UnzonedDateTime.InZone`, the wall-time constructor, `Parse` with a zone). It does **not** use `TimeZoneInfo.ConvertTimeToUtc` or `IsInvalidTime` (review B5: the first throws on gaps and picks standard time on overlaps; the second misses base-offset changes such as Pacific/Apia skipping 30 Dec 2011). Algorithm:
   1. `before = zone.GetUtcOffset(wall - 1 day as UTC)`, `after = zone.GetUtcOffset(wall + 1 day as UTC)` (plus the offset at `wall` itself).
   2. For each distinct candidate offset `o`: instant `= wall - o`; keep it if `zone.GetUtcOffset(instant) == o`.
   3. One candidate → done. Two → overlap: `Earlier` = the larger offset, `Later` = the smaller (never by index), `Throw`. None → gap: `ShiftForward` = `wall - before` (moves forward by the gap length, 02:30 → 03:30, Apia 30 Dec 12:00 → 31 Dec 12:00), `Throw`.
   Defaults: `GapPolicy.ShiftForward`, `OverlapPolicy.Earlier`. Optional parameters, no global state.
5. **`UnzonedDateTime`**: `readonly record struct (DateOnly Date, TimeOnly Time)`. A date and time whose zone was not recorded. No `Now`, no `ToUtc`, no comparison with `DateTimeZone`. Exits: `InZone(zone, gap, overlap)` → `DateTimeZone`, `AssumeUtc()`. Entry from the other side: `DateTimeZone.ToUnzoned()`. Calendar and clock arithmetic without DST.
6. **No guessing on input.**
   - `FromDateTimeOffset(dto, zone)` → instant from `dto`, shown in `zone`. `FromDateTimeOffset(dto)` → fixed-offset zone from `Zones.FixedOffset(dto.Offset)` (an offset is not a zone; documented).
   - `FromDateTime(dt)`: `Kind.Utc` → UTC; `Kind.Local` → `dt.ToUniversalTime()` + `TimeZoneInfo.Local` (keeps the hidden overlap bit, review B6; never wall time + resolver); `Kind.Unspecified` → `DateTimeZoneException` pointing to `UnzonedDateTime.FromDateTime(dt).InZone(...)`.
   - `Now(TimeProvider? provider = null)` uses `provider.LocalTimeZone` and `provider.GetUtcNow()`.
7. **Zones.** `Zones.Resolve(string id)`: IANA or Windows ID, canonical ID out (`Europe/Kiev` → `Europe/Kyiv`, `Poland` → `Europe/Warsaw`, `Etc/UTC` → `UTC`) from a link table generated once from tzdb `backward` and committed. `Zones.FixedOffset(TimeSpan)`: cached instances with ID `UTC+02:00` style, so `FromDateTimeOffset` and `Parse` produce equal zones. Clear errors: missing tzdata (chiseled/distroless images, Alpine without `tzdata`) and invariant globalization (Windows IDs, IANA on Windows) name the cause and the fix. No static class with hundreds of zone fields.
8. **Text: RFC 9557** (`2026-10-18T10:00:00+02:00[Europe/Warsaw]`). Writing: canonical ID; a zone without a usable ID (`Local` with ID `Local`, a path) is written as offset only. Reading:
   | Input | Result |
   |---|---|
   | offset + `[zone]`, consistent | `DateTimeZone` in that zone |
   | offset + `[zone]`, offset wrong for the zone at that instant | throw (default); `Prefer.Offset` / `Prefer.Zone` option |
   | `Z[zone]` | instant in UTC, shown in `zone` |
   | `Z` or offset, no brackets | fixed-offset zone (`Z` → `UTC`) |
   | no offset, `[zone]` | wall time resolved in `zone` (decision 4) |
   | neither | `UnzonedDateTime` |
   | `[!zone]` | same as `[zone]` |
   | other annotations (`[u-ca=…]`) | ignored; critical (`[!u-ca=…]`) → throw |
   | more than 7 fractional digits | truncated to ticks |
   | `23:59:60` | throw |
9. **Serialization.** `System.Text.Json` converters for both types in the main package, registered with `[JsonConverter]` on the types, including `ReadAsPropertyName`/`WriteAsPropertyName` (dictionary keys). Newtonsoft converters and the Avro mapping live in an AvroConvert-side package after 2.0, not here.
10. **Names.** Package and type stay `SolTechnology.DateTimeZone` / `DateTimeZone`. Namespace stays `SolTechnology.Time`: a namespace equal to the type name does not compile for callers under `SolTechnology.*` (`CS0118`, checked). NodaTime also has a `DateTimeZone` (a zone, not an instant): README shows `using NodaDateTimeZone = NodaTime.DateTimeZone;` for projects with both.
11. **Targets `net8.0;net10.0`**, `Nullable`, `TreatWarningsAsErrors`. No PublicApiAnalyzers until 2.0 stable.

Known limits, documented rather than fixed: offsets before 1972 can differ from tzdb by seconds (.NET truncates to whole minutes, e.g. Africa/Monrovia 1970: −00:44 vs −00:44:30), and pre-1970 data depends on the tzdata build (Europe/Amsterdam 1900 is +00:00 after tzdb merged it into Brussels). Results for old dates can differ between Windows and Linux.

Out of scope for 2.0: non-ISO calendars, `Period`/`Duration`, bundled tzdb, Roslyn analyzer, NodaTime converters.

## Steps

### 0. Transfer the repository to the organisation – done 26 Oct 2026
- [x] Transferred to `Sol-Technology/SolTechnology.DateTimeZone`; remote, `RepositoryUrl`, `PackageProjectUrl`, README badge updated.
- [ ] Publishing: nuget.org **Trusted Publishing** as in AvroConvert (`publish.yml` on `v*` tags, environment `nuget`, repo variable `NUGET_USER`); policy owner `Sol-Technology`, repository `SolTechnology.DateTimeZone`, workflow `publish.yml`. Done with the CI in step 1.

### 1. Project housekeeping
- [ ] Project and solution stay `SolTechnology.Time` (namespace), `PackageId`/`AssemblyName` stay `SolTechnology.DateTimeZone`. Solution file → `.slnx` or keep.
- [ ] `TargetFrameworks` `net8.0;net10.0`; `Version` 2.0.0-preview.1; MIT; `PackageReadmeFile`; `Deterministic`, `ContinuousIntegrationBuild`, SourceLink; `GenerateDocumentationFile`.
- [ ] Remove `todo.cs`, `TestTest.cs`, `.github/runTests.ps1`.
- [ ] Tests: NUnit + AwesomeAssertions (same as AvroConvert).
- [ ] CI `build.yml`: build + test on PR and `main`, matrix `ubuntu-latest`, `windows-latest`, `macos-latest`; one job with `InvariantGlobalization=true`; one job in `mcr.microsoft.com/dotnet/sdk:10.0` with tzdata removed (expects the clear error, not a crash). `publish.yml`: pack + push on `v*` tags only.

### 2. Core
- [ ] `DateTimeZone` struct (decision 1), constructors: `(DateOnly, TimeOnly, TimeZoneInfo, GapPolicy, OverlapPolicy)`, `FromDateTimeOffset(dto[, zone])`, `FromDateTime`, `FromUtc(DateTime utc, TimeZoneInfo)`, `Now(TimeProvider?)`. Range check: wall time must stay within `DateTime` range (no silent clamping, review B1).
- [ ] Equality, `GetHashCode`, `IComparable<DateTimeZone>`, operators by instant; `SameRepresentation`; `DateTimeZoneComparer.Representation`.
- [ ] `ToTimeZone(TimeZoneInfo)`, `ToTimeZone(string)`, `ToUtc()`: same instant, other zone.
- [ ] Remove `Add(TimeOnly)`/`Add(DateOnly)` (their arguments are not quantities); `AtTime(TimeOnly)` replaces the first.

### 3. `ZoneResolver` and arithmetic
- [ ] `ZoneResolver.Resolve` (decision 4), `GapPolicy`, `OverlapPolicy`.
- [ ] Instant and calendar arithmetic (decision 3).

### 4. `UnzonedDateTime` (decision 5)

### 5. Zones (decision 7)
- [ ] `tools/GenerateZoneLinks`: reads tzdb `backward`, writes `ZoneLinks.g.cs`; committed, re-run when tzdb changes.
- [ ] `Zones.Resolve`, `Zones.FixedOffset`, `Zones.Utc`. Remove `ZoneSelector`.

### 6. Text (decision 8)
- [ ] `ToString()`, `Parse`/`TryParse` for both types; `IParsable<T>`, `IFormattable`, `ISpanFormattable`. `ToString(format)` formats `_value` (the `DateTimeOffset`).
- [ ] STJ converters (decision 9), test with a source-generated `JsonSerializerContext`.

### 7. Tests that must pass
- [ ] Gap: Warsaw 2026-03-29 02:30 → `ShiftForward` 03:30+02:00, `Throw` throws. Antarctica/Troll 2026-03-29 01:30 (2-hour gap). **Pacific/Apia 2011-12-30 12:00** (skipped day) → 2011-12-31 12:00+14:00.
- [ ] Overlap: Warsaw 2026-10-25 02:30 → `Earlier` +02:00, `Later` +01:00. Australia/Lord_Howe (30-minute DST). America/Santiago (southern hemisphere).
- [ ] `AddHours(24)` vs `AddDays(1)` across both Warsaw nights: different instants. `AddMonths` from 31 Jan.
- [ ] Equality: `new(10:00, Warsaw) == new(08:00, UTC)`, one element in a `HashSet`; `SameRepresentation` false; `Etc/UTC` and `UTC` are the same representation; `Europe/Kiev` and `Europe/Kyiv` too.
- [ ] `FromDateTime(Kind.Local)` for both 02:30s of the overlap (via `ToLocalTime()`), each keeps its instant. `Kind.Unspecified` throws with the hint.
- [ ] Round-trip `ToString()`/`Parse` for IANA zone, fixed offset, UTC, and every row of the table in decision 8.
- [ ] `default(DateTimeZone)` members do not throw.
- [ ] Range: 9999-12-31 in Pacific/Kiritimati throws instead of clamping.
- [ ] Missing tzdata and invariant globalization: clear error messages (CI jobs from step 1).
- [ ] NodaTime as an oracle (separate, non-blocking test project): random instants 1972–2025 × zones without rule changes in the last 5 years; compare offset and wall clock.

### 8. Release
- [ ] `2.0.0-preview.1` with a README: the two types, the rule "no zone = `UnzonedDateTime`", the format limit (instant-only formats drop the zone), the NodaTime alias.
- [ ] Deprecate 1.0.1 on NuGet with a link to 2.0 ("API rewrite; 1.x has DST bugs").
- [ ] AvroConvert 4.x / Lake: mapping package (`timestamp-*` ↔ `DateTimeZone` in UTC, `local-timestamp-*` ↔ `UnzonedDateTime`), separate plan.

Estimate: step 1 one evening; 2–4 three evenings (`ZoneResolver` with its tests is the biggest part); 5–7 two to three evenings; 8 half a day. Timebox: preview.1 by end of January 2027, otherwise stop and cut scope.

---

The two appendices below are the analysis of the first draft. Their findings are already in the decisions and steps above; they stay as the reason for those decisions.

## Appendix A: premortem (26 Oct 2026)

Assume it is September 2027 and 2.0 failed: few or no users, AvroConvert does not use it, the evenings went elsewhere. The likely reasons, most likely first. "Checked" means verified on 26 Oct 2026, not assumed.

### Most likely story
Lake does not pass its go/no-go in Q1 2027. AvroConvert never takes the dependency because of #100 (fewer dependencies) and because 4.0 already writes `DateTimeOffset` as an instant. 2.0-preview sits on NuGet with a few dozen downloads. The 6–8 evenings came out of November–December, when 4.0 users needed answers.

### Findings

| # | Risk | Evidence | Change to the plan |
|---|---|---|---|
| 1 | **Namespace `SolTechnology.DateTimeZone` + type `DateTimeZone` does not compile in SolTechnology code.** Step 1 makes the namespace equal to the type name. | Checked: in `namespace SolTechnology.Avro` with `using SolTechnology.DateTimeZone;`, `DateTimeZone` resolves to the namespace → `CS0118: 'DateTimeZone' is a namespace but is used like a type`. AvroConvert and Lake both live under `SolTechnology.*`. Outside `SolTechnology.*` it compiles. | Keep package id `SolTechnology.DateTimeZone`, keep the namespace `SolTechnology.Time` (as today). Drop the rename of the namespace from step 1. |
| 2 | **The type name collides with NodaTime, where `DateTimeZone` is a zone, not an instant.** Anyone with both packages gets ambiguous references, and the name means the opposite of what NodaTime users expect. | NodaTime: `DateTimeZone` = zone, `ZonedDateTime` = this concept. | Decide the type name before writing code; 2.0 is the only cheap moment. Options: keep it and document, or e.g. `ZonedTime`. Not `ZonedDateTime` (same clash). |
| 3 | **The problem it solves is not visible in AvroConvert issues.** Purpose says "dates shifted by an hour are a recurring topic". | Checked: 94 issues in total, ~5 about dates (#21 nullable DateTime, #69 BigQuery logical type, #108 rounding, #153 millis vs micros, #118 ADX). None about zones or DST. 4.0 already fixed the `DateTimeOffset` instant. | Remove the claim. The success metric "fewer date-related issues" cannot move from a baseline of ~0. Real driver is Lake (Iceberg `timestamp` vs `timestamptz`, Parquet `isAdjustedToUTC`). Start 2.0 **after** the Lake go/no-go, or limit it to what Lake needs. |
| 4 | **"Optional dependency" of AvroConvert does not exist in NuGet.** Either AvroConvert depends on it (against #100, one more package for every 4.x user) or there is an adapter package. | — | Decide now: adapter `SolTechnology.Avro.Time` (or similar), AvroConvert core stays without it. |
| 5 | **No tz database = every IANA zone throws.** Chiseled and distroless .NET images ship without tzdata (only the `-extra` variants have it), Alpine needs `apk add tzdata`. | Checked: with the tz database hidden (`TZDIR=/nonexistent`), `FindSystemTimeZoneById("Europe/Warsaw")` → `TimeZoneNotFoundException`, `Local.Id` → `UTC`. | Error message that names the cause and the fix (install tzdata / use an `-extra` image). CI job in a chiseled image. README section. |
| 6 | **Zone IDs are not canonical, so RFC 9557 output and equality of zones differ between machines.** | Checked: on macOS `TimeZoneInfo.Local.Id` = `Poland` (a tzdb link), not `Europe/Warsaw`. Linux containers give `UTC` or `Etc/UTC`; .NET can also report `Local`. | Canonicalize through the tzdb `backward` link list (small generated table), never write `[Local]`; test `ToString()` of `Local` on all three OSes. |
| 7 | **NodaTime oracle tests will be flaky.** NodaTime bundles its own tzdb version, runners use the OS tzdata/ICU; zones changed recently (Mexico 2022, Greenland 2023, Kazakhstan 2024, Paraguay 2024) disagree between versions. Windows has fewer historical rules. | — | Oracle only for zones without changes in the last 5 years and dates 2000–2025, or load the same tzdb version into NodaTime. Mark the oracle project as non-blocking. |
| 8 | **RFC 9557 edge cases are not decided.** Offset inconsistent with the zone (`…T10:00+01:00[Europe/Warsaw]` in July), critical flag `[!Europe/Warsaw]`, `[u-ca=…]` annotations, historical sub-minute offsets (Warsaw LMT +01:24 before 1915, while `DateTimeOffset` offsets are whole minutes). | — | Table in step 6: inconsistent offset → throw by default; `!` accepted; unknown annotations ignored unless critical; pre-1900 sub-minute offsets rounded or rejected, documented. |
| 9 | **`default` value.** If `DateTimeZone` is a struct, `default(DateTimeZone).Zone` is `null`. | Checked: `default(...)` of a struct with a `TimeZoneInfo` field has `Zone == null`. | Decide class vs struct in step 2. If struct: treat `null` as UTC, or throw a clear error from every member. |
| 10 | **AvroConvert JSON goes through Newtonsoft, the plan only has System.Text.Json converters.** `Avro2Json` of a model with these types would serialize the `TimeZoneInfo` object. | AvroConvert 4.0 keeps Newtonsoft in the public API (V4ReleasePlan). | Newtonsoft converter in the adapter package from point 4, or keep these types out of the Avro JSON paths. |
| 11 | **Scope creep for a ~100-download package.** Zones generator from CLDR, PublicApiAnalyzers, OS matrix, oracle project. | — | Cut for preview.1: `Zones.Resolve(string)` + a handful of constants, no generator; analyzers after 2.0. Timebox: if preview.1 is not out by end of January 2027, stop and reassess. |
| 12 | **Tooling different from the rest of the family.** Plan says xunit + FluentAssertions 7 and an API-key push; AvroConvert uses NUnit + AwesomeAssertions and Trusted Publishing. | — | Same stack as AvroConvert: NUnit, AwesomeAssertions, `publish.yml` with Trusted Publishing (step 0 already says so). |

### Kill criteria
- Lake no-go and no AvroConvert issue about zones by the time preview.1 is ready → ship preview.1 as is, no further work.
- More than 8 evenings spent before preview.1 → stop, cut scope to `UnzonedDateTime` + instant-based core.

## Appendix B: design review of the first draft (26 Oct 2026)

"Checked" = run on .NET 10.0.100, macOS, system tzdata, 26 Oct 2026. Windows was not checked; every Windows point below is a test to add to the CI matrix, not a finding.

### Decision 1: `UtcTicks` + `TimeZoneInfo`, everything else derived
- **Cost per access.** Checked: `GetUtcOffset` is ~74 ns per call, no allocations. `Date`, `Time` and `Offset` each call it, so reading three properties of one value costs ~0.2 µs; a million rows in Lake or Avro is ~0.2 s of offsets alone. → Compute the offset once in the constructor and store it (`long UtcTicks`, `int OffsetSeconds`, `TimeZoneInfo Zone`). The instant stays the truth; the stored offset is a cache, recalculated only by `ToTimeZone`.
- **Silent clamping at the range ends.** Checked: `ConvertTimeFromUtc(9999-12-31 23:00Z, Pacific/Kiritimati)` returns `9999-12-31 23:59:59` instead of overflowing; the wall clock is simply wrong. → Validate in the constructor that `UtcTicks + offset` stays inside `DateTime` range; throw otherwise. Do not go through `ConvertTimeFromUtc` for the derived fields, add the offset yourself.
- **Struct `default`.** If it is a struct, `default` has `Zone == null` (see premortem #9). With the stored offset from the first point, `default` can simply mean 0001-01-01 UTC: treat `null` zone as `TimeZoneInfo.Utc` in the getter.

### Decision 2: equality by instant
- **Not a `record`.** A `record`/`record struct` generates equality over all fields, including `Zone`, which is the opposite of this decision. Write `Equals`/`GetHashCode`/operators by hand; add a test that `new(10:00, Warsaw)` and `new(08:00, UTC)` are one element of a `HashSet`.
- **Consequence to document:** a `Dictionary<DateTimeZone, …>` or `Distinct()` keeps the zone of whichever element came first. Users who group by "same time and same zone" need `SameRepresentation` and a custom comparer; provide `DateTimeZoneComparer.Representation`.
- **`SameRepresentation` must compare canonical zone IDs, not `TimeZoneInfo.Equals`.** Checked: `FindSystemTimeZoneById("Etc/UTC").Equals(TimeZoneInfo.Utc)` → false (`Etc/UTC` vs `UTC`); `Europe/Kiev` vs `Europe/Kyiv` → false; `TimeZoneInfo.Local.Id` on macOS is `Poland`. Same zone, three spellings.

### Decision 3 and step 3: arithmetic
- **Calendar arithmetic clamps and is not associative.** Checked: `2026-01-31 + 1 month` = `2026-02-28`. So `AddMonths(1).AddMonths(1)` ≠ `AddMonths(2)` from the 31st. That is the right behaviour, but tests and docs must state it.
- **Duration between two values (`a - b`) is by instant.** Across a DST night, `a.AddDays(1) - a` = 23 h or 25 h. Document next to `AddDays`.

### Decision 5: gap and overlap policies (the riskiest part)
- **`TimeZoneInfo.ConvertTimeToUtc` cannot be used directly.** Checked: for a gap it throws `ArgumentException` (Warsaw 2026-03-29 02:30); for an overlap it silently picks **standard time** (Warsaw 2026-10-25 02:30 → 01:30Z, i.e. `Later`), while the plan's default is `Earlier`.
- **Order of `GetAmbiguousTimeOffsets`.** Checked: ascending (`+01:00, +02:00`). `Earlier` instant = the **larger** offset, `Later` = the smaller. Choose by value (`Max`/`Min`), never by index.
- **Gaps that `IsInvalidTime` does not see.** Checked: Pacific/Apia skipped 30 Dec 2011 entirely (offset −10 → +14), but `IsInvalidTime(2011-12-30 12:00)` = **false**, and the value round-trips to `2011-12-29 12:00`, a different day, with no error. Base-offset changes are not reported as invalid; DST gaps are (Antarctica/Troll's 2-hour gap is detected, checked). → Do not rely on `IsInvalidTime` alone. Algorithm: take the offsets 24 h before and 24 h after the wall time (`GetUtcOffset` on `wall - offset` candidates), try both, keep candidates whose round-trip gives the same wall time; 0 candidates = gap, 2 = overlap. Gap with `ShiftForward`: instant = `wall - offsetBefore` (shifts by the gap length, 02:30 → 03:30, Apia 12:00 on the 30th → 12:00 on the 31st).
- **Test list must include:** Apia 2011-12-30, Antarctica/Troll (2 h gap), Australia/Lord_Howe (30-minute DST), a zone that changed its base offset without DST (e.g. Europe/Volgograd 2020), southern hemisphere (America/Santiago).

### Decision 6: input
- **`FromDateTime(Kind.Local)` in the overlap hour.** Checked: `DateTime` keeps a hidden "ambiguous daylight" bit, so `00:30Z.ToLocalTime()` and `01:30Z.ToLocalTime()` are both `02:30`, `==` says they are equal, and only `ToUniversalTime()` tells them apart (00:30Z / 01:30Z). Rebuilding the value from its ticks (any database or serializer does that) loses the bit: both become 01:30Z. → Implement `FromDateTime(Local)` as `dt.ToUniversalTime()` + `TimeZoneInfo.Local`, never as wall time + `InZone`. Document that a `Kind.Local` value that went through storage is already ambiguous in that hour.
- **`FromDateTimeOffset` creates a fixed-offset zone that `FindSystemTimeZoneById` will not find.** Checked: `FindSystemTimeZoneById("+02:00")` → `TimeZoneNotFoundException`. → One factory (`Zones.FixedOffset(TimeSpan)`) with cached instances and a fixed ID format, used by both `FromDateTimeOffset` and `Parse`, so `SameRepresentation` works between them.
- **Sub-minute offsets.** Checked: .NET truncates historical offsets to whole minutes (Africa/Monrovia 1970: .NET −00:44, tzdb −00:44:30). Values before 1972 will disagree with Java/Python/NodaTime by up to 59 s. Document as a limitation; keep the NodaTime oracle to 1972+.
- **Pre-1970 data depends on the tzdata build.** Checked: Europe/Amsterdam in 1900 gives +00:00 on this machine (tzdb merged Amsterdam into Brussels in 2022, the original +00:19:32 lives only in `backzone`). The same instant can render differently on two servers. Document; do not test pre-1970 against an oracle.

### Decision 7: IANA as canonical
- **Invariant globalization breaks the fallback.** Checked: with `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`, `FindSystemTimeZoneById("Central European Standard Time")` throws and `TryConvertWindowsIdToIanaId` returns false. Invariant mode is common in containers and NativeAOT. On Linux IANA IDs still work; on Windows in invariant mode IANA IDs will not resolve at all. → Error message that names invariant mode; CI job with `InvariantGlobalization=true`.
- **Canonicalization is needed and .NET has no API for it.** → Generate a small link→canonical table from tzdb `backward` (Europe/Kiev→Europe/Kyiv, Poland→Europe/Warsaw, Etc/UTC→UTC…). Use it in `ToString()`, `SameRepresentation` and `Zones.Resolve`. `TimeZoneInfo.Local` with ID `Local` or a path must never be written as `[Local]`; fall back to the offset only.
- **`Zones` as static fields.** Static readonly fields of one class are initialized together; if one zone is missing on the machine (older tzdata, chiseled image), the whole class throws `TypeInitializationException`. → Properties backed by per-zone `Lazy<TimeZoneInfo>`, or only `Resolve(string)`.

### Decision 8 and step 6: text
- **`Z` vs `+00:00`.** In RFC 9557, `Z` means "instant known, local offset unknown", `+00:00` means "local time is UTC". Decide: `…Z` → UTC zone, `…Z[Europe/Warsaw]` → Warsaw. Tests for both.
- **Precision.** RFC 9557 allows any number of fractional digits; `DateTime` has 7. Decide: truncate (and say so) or reject.
- **Leap second `23:59:60`.** `DateTime` cannot hold it. Decide: map to `23:59:59.9999999` or reject. Java and PostgreSQL can emit it.
- **Inconsistent offset and zone, critical flags, annotations:** see premortem #8.

### Decision 9: System.Text.Json converters
- **Dictionary keys.** A converter without `ReadAsPropertyName`/`WriteAsPropertyName` throws when the type is a dictionary key. Implement both for both types.
- **Source generation and trimming.** `[JsonConverter]` on the type works with source-generated contexts, but add a test with `JsonSerializerContext` and `PublishTrimmed`, since Lake and AvroConvert users publish AOT.
- **Newtonsoft path:** premortem #10.

### Step 2: `Now(TimeProvider?)`
- `DateTimeZone.Now(provider)` must use `provider.LocalTimeZone`, not `TimeZoneInfo.Local`; otherwise tests with a fake provider still depend on the machine's zone. `FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`) in the test project.

### Formats (S7 and Lake): what the types can actually carry
- Avro `timestamp-*`, Parquet `isAdjustedToUTC=true` and Iceberg `timestamptz` store **only the instant**. Writing a `DateTimeZone` and reading it back always returns UTC; with equality by instant, round-trip tests pass and nobody notices the zone is gone. → The mapping must document it, and a test should assert `SameRepresentation` is **false** after the round trip, so the loss is explicit. The zone survives only if the schema has a separate string field for it; offer that as an opt-in pattern (`[AvroZoneField("…")]` or similar) or leave it out of 2.0.
- `local-timestamp-*` / Iceberg `timestamp` / Parquet `isAdjustedToUTC=false` ↔ `UnzonedDateTime` is the mapping that adds real value: today those come back as `DateTime` `Kind.Unspecified` and the user guesses.
