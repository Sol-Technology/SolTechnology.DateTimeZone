namespace SolTechnology.Time;

public readonly partial struct DateTimeZone
{
    private const long MaxTicks = 3155378975999999999; // DateTime.MaxValue.Ticks

    // Instant arithmetic: moves the instant, then reads the zone's offset again. Always correct across DST.

    /// <summary>Moves the instant by <paramref name="value"/>. Across a DST change the wall clock moves by a different amount.</summary>
    public DateTimeZone Add(TimeSpan value)
    {
        var ticks = UtcTicks;
        if (value.Ticks > MaxTicks - ticks || value.Ticks < -ticks)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "The result is outside the range of DateTime.");
        }

        return FromInstant(ticks + value.Ticks, Zone);
    }

    /// <summary>Moves the instant back by <paramref name="value"/>.</summary>
    public DateTimeZone Subtract(TimeSpan value) => Add(-value);

    /// <summary>Time between two instants. Across a DST night, <c>a.AddDays(1) - a</c> is 23 or 25 hours.</summary>
    public TimeSpan Subtract(DateTimeZone other) => TimeSpan.FromTicks(UtcTicks - other.UtcTicks);

    /// <summary>Moves the instant by a number of hours.</summary>
    public DateTimeZone AddHours(double hours) => AddScaled(hours, TimeSpan.TicksPerHour);

    /// <summary>Moves the instant by a number of minutes.</summary>
    public DateTimeZone AddMinutes(double minutes) => AddScaled(minutes, TimeSpan.TicksPerMinute);

    /// <summary>Moves the instant by a number of seconds.</summary>
    public DateTimeZone AddSeconds(double seconds) => AddScaled(seconds, TimeSpan.TicksPerSecond);

    /// <summary>Moves the instant by a number of milliseconds.</summary>
    public DateTimeZone AddMilliseconds(double milliseconds) => AddScaled(milliseconds, TimeSpan.TicksPerMillisecond);

    /// <summary>Moves the instant by a number of ticks.</summary>
    public DateTimeZone AddTicks(long ticks) => Add(TimeSpan.FromTicks(ticks));

    /// <summary>Moves the instant by <paramref name="value"/>.</summary>
    public static DateTimeZone operator +(DateTimeZone left, TimeSpan value) => left.Add(value);

    /// <summary>Moves the instant back by <paramref name="value"/>.</summary>
    public static DateTimeZone operator -(DateTimeZone left, TimeSpan value) => left.Subtract(value);

    /// <summary>Time between two instants.</summary>
    public static TimeSpan operator -(DateTimeZone left, DateTimeZone right) => left.Subtract(right);

    // Calendar arithmetic: changes the date on the clock in the zone, keeps the time on the clock, then finds the instant.

    /// <summary>
    /// The same time on the clock, <paramref name="days"/> days later in the zone. Across a DST night the instant moves
    /// by 23 or 25 hours. If the time does not exist or exists twice on the new date, <paramref name="gap"/> and
    /// <paramref name="overlap"/> decide.
    /// </summary>
    public DateTimeZone AddDays(int days, GapPolicy gap = GapPolicy.ShiftForward, OverlapPolicy overlap = OverlapPolicy.Earlier) =>
        days == 0 ? this : InZone(_value.DateTime.AddDays(days), gap, overlap);

    /// <summary>
    /// The same time on the clock, <paramref name="months"/> months later in the zone. The day is clamped to the end of
    /// the month (31 January + 1 month = 28 February), so <c>AddMonths(1).AddMonths(1)</c> is not always <c>AddMonths(2)</c>.
    /// </summary>
    public DateTimeZone AddMonths(int months, GapPolicy gap = GapPolicy.ShiftForward, OverlapPolicy overlap = OverlapPolicy.Earlier) =>
        months == 0 ? this : InZone(_value.DateTime.AddMonths(months), gap, overlap);

    /// <summary>The same time on the clock, <paramref name="years"/> years later in the zone (29 February → 28 February).</summary>
    public DateTimeZone AddYears(int years, GapPolicy gap = GapPolicy.ShiftForward, OverlapPolicy overlap = OverlapPolicy.Earlier) =>
        years == 0 ? this : InZone(_value.DateTime.AddYears(years), gap, overlap);

    /// <summary>The same date in the zone, at another time on the clock.</summary>
    public DateTimeZone AtTime(TimeOnly time, GapPolicy gap = GapPolicy.ShiftForward, OverlapPolicy overlap = OverlapPolicy.Earlier) =>
        InZone(Date.ToDateTime(time), gap, overlap);

    private DateTimeZone InZone(DateTime wall, GapPolicy gap, OverlapPolicy overlap)
    {
        var zone = Zone;
        return FromResolved(ZoneResolver.Resolve(wall, zone, gap, overlap), zone);
    }

    private DateTimeZone AddScaled(double value, long scale)
    {
        var ticks = value * scale;
        if (double.IsNaN(ticks) || ticks > MaxTicks || ticks < -MaxTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "The result is outside the range of DateTime.");
        }

        return Add(TimeSpan.FromTicks((long)Math.Round(ticks)));
    }
}
