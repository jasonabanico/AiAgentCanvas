using System.Globalization;

namespace AiAgentCanvas.Abstractions;

/// <summary>
/// Minimal 5-field cron evaluator (minute hour day-of-month month day-of-week).
/// Supports <c>*</c>, <c>*/n</c>, <c>a-b</c>, <c>a-b/n</c>, comma lists and literal
/// numbers. Day-of-week accepts 0-7 with both 0 and 7 meaning Sunday.
/// Used by both the scheduler and the event-trigger loop so a schedule written in
/// one place means the same thing in the other.
/// </summary>
public sealed class CronSchedule
{
    private readonly bool[] _minutes = new bool[60];
    private readonly bool[] _hours = new bool[24];
    private readonly bool[] _daysOfMonth = new bool[32];
    private readonly bool[] _months = new bool[13];
    private readonly bool[] _daysOfWeek = new bool[7];

    public string Expression { get; }

    private CronSchedule(string expression) => Expression = expression;

    public static bool TryParse(string? expression, out CronSchedule? schedule)
    {
        schedule = null;
        if (string.IsNullOrWhiteSpace(expression))
            return false;

        var fields = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5)
            return false;

        var parsed = new CronSchedule(expression.Trim());
        if (!FillField(fields[0], 0, 59, parsed._minutes)) return false;
        if (!FillField(fields[1], 0, 23, parsed._hours)) return false;
        if (!FillField(fields[2], 1, 31, parsed._daysOfMonth)) return false;
        if (!FillField(fields[3], 1, 12, parsed._months)) return false;
        if (!FillDayOfWeek(fields[4], parsed._daysOfWeek)) return false;

        schedule = parsed;
        return true;
    }

    public static CronSchedule Parse(string expression) =>
        TryParse(expression, out var schedule) && schedule is not null
            ? schedule
            : throw new FormatException($"'{expression}' is not a supported 5-field cron expression.");

    /// <summary>True when <paramref name="instant"/> falls on a minute this schedule matches.</summary>
    public bool Matches(DateTimeOffset instant)
    {
        var t = instant.UtcDateTime;
        return _minutes[t.Minute]
            && _hours[t.Hour]
            && _months[t.Month]
            && MatchesDay(t);
    }

    /// <summary>
    /// True when at least one matching minute falls in the half-open window
    /// (<paramref name="after"/>, <paramref name="now"/>]. A null <paramref name="after"/>
    /// means the schedule has never run, so the next matching minute at or before now is due.
    /// </summary>
    public bool IsDue(DateTimeOffset? after, DateTimeOffset now)
    {
        var next = GetNextOccurrence(after ?? now.AddMinutes(-1));
        return next is not null && next <= now;
    }

    /// <summary>
    /// First matching instant strictly after <paramref name="after"/>, or null if none
    /// falls within the next four years (which only happens for impossible dates such as
    /// <c>0 0 30 2 *</c>).
    /// </summary>
    public DateTimeOffset? GetNextOccurrence(DateTimeOffset after)
    {
        // Everything below is UTC DateTime arithmetic on purpose. Mixing in
        // DateTimeOffset invites the implicit DateTime conversion, which stamps the
        // machine's local offset and can walk the candidate backwards.
        var start = after.UtcDateTime;
        var candidate = new DateTime(
            start.Year, start.Month, start.Day, start.Hour, start.Minute, 0, DateTimeKind.Utc)
            .AddMinutes(1);

        var limit = candidate.AddYears(4);

        // Belt and braces. Every branch below advances the candidate, but a scheduler
        // that spins forever takes the host with it, so the search is bounded twice.
        var steps = 0;
        const int maxSteps = 4 * 366 * 24 * 60;

        while (candidate < limit && steps++ < maxSteps)
        {
            if (!_months[candidate.Month])
            {
                candidate = new DateTime(candidate.Year, candidate.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
                continue;
            }

            if (!MatchesDay(candidate))
            {
                candidate = candidate.Date.AddDays(1);
                continue;
            }

            if (!_hours[candidate.Hour])
            {
                candidate = candidate.Date.AddHours(candidate.Hour + 1);
                continue;
            }

            if (_minutes[candidate.Minute])
                return new DateTimeOffset(candidate, TimeSpan.Zero);

            candidate = candidate.AddMinutes(1);
        }

        return null;
    }

    private bool MatchesDay(DateTime t)
    {
        // Standard cron semantics: when both day-of-month and day-of-week are restricted
        // the day matches if either does; when only one is restricted, only that one counts.
        var domRestricted = !AllSet(_daysOfMonth, 1, 31);
        var dowRestricted = !AllSet(_daysOfWeek, 0, 6);

        var domMatch = _daysOfMonth[t.Day];
        var dowMatch = _daysOfWeek[(int)t.DayOfWeek];

        if (domRestricted && dowRestricted) return domMatch || dowMatch;
        if (domRestricted) return domMatch;
        if (dowRestricted) return dowMatch;
        return true;
    }

    private static bool AllSet(bool[] flags, int min, int max)
    {
        for (var i = min; i <= max; i++)
            if (!flags[i]) return false;
        return true;
    }

    private static bool FillDayOfWeek(string field, bool[] flags)
    {
        // Accept 0-7 on input, then fold 7 onto 0 so lookups by DayOfWeek stay in range.
        var wide = new bool[8];
        if (!FillField(field, 0, 7, wide)) return false;
        for (var i = 0; i <= 7; i++)
            if (wide[i]) flags[i % 7] = true;
        return true;
    }

    private static bool FillField(string field, int min, int max, bool[] flags)
    {
        foreach (var part in field.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var step = 1;
            var range = part;

            var slash = part.IndexOf('/');
            if (slash >= 0)
            {
                range = part[..slash];
                if (!int.TryParse(part[(slash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out step) || step < 1)
                    return false;
            }

            int from, to;
            if (range == "*")
            {
                from = min;
                to = max;
            }
            else
            {
                var dash = range.IndexOf('-');
                if (dash >= 0)
                {
                    if (!int.TryParse(range[..dash], NumberStyles.None, CultureInfo.InvariantCulture, out from)) return false;
                    if (!int.TryParse(range[(dash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out to)) return false;
                }
                else
                {
                    if (!int.TryParse(range, NumberStyles.None, CultureInfo.InvariantCulture, out from)) return false;
                    to = slash >= 0 ? max : from;
                }
            }

            if (from < min || to > max || from > to)
                return false;

            for (var value = from; value <= to; value += step)
                flags[value] = true;
        }

        return true;
    }
}
