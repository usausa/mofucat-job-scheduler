namespace Mofucat.JobScheduler;

using System.Globalization;

/// <summary>
/// Parses and evaluates a cron expression.
/// </summary>
public sealed class CronExpression
{
    private readonly HashSet<int> seconds;
    private readonly HashSet<int> minutes;
    private readonly HashSet<int> hours;
    private readonly HashSet<int> daysOfMonth;
    private readonly HashSet<int> months;
    private readonly HashSet<int> daysOfWeek;
    private readonly bool dayOfMonthRestricted;
    private readonly bool dayOfWeekRestricted;

    private CronExpression(
        string expression,
        HashSet<int> seconds,
        HashSet<int> minutes,
        HashSet<int> hours,
        HashSet<int> daysOfMonth,
        HashSet<int> months,
        HashSet<int> daysOfWeek,
        bool dayOfMonthRestricted,
        bool dayOfWeekRestricted)
    {
        Expression = expression;
        this.seconds = seconds;
        this.minutes = minutes;
        this.hours = hours;
        this.daysOfMonth = daysOfMonth;
        this.months = months;
        this.daysOfWeek = daysOfWeek;
        this.dayOfMonthRestricted = dayOfMonthRestricted;
        this.dayOfWeekRestricted = dayOfWeekRestricted;
    }

    /// <summary>
    /// Gets the original cron expression.
    /// </summary>
    public string Expression { get; }

    /// <summary>
    /// Parses a cron expression.
    /// </summary>
    /// <param name="expression">The cron expression.</param>
    /// <returns>The parsed expression.</returns>
    public static CronExpression Parse(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);

        var parts = expression.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

        HashSet<int> localSeconds;
        HashSet<int> localMinutes;
        HashSet<int> localHours;
        HashSet<int> localDaysOfMonth;
        HashSet<int> localMonths;
        HashSet<int> localDaysOfWeek;
        string dayOfMonthField;
        string dayOfWeekField;

        if (parts.Length == 5)
        {
            localSeconds = [0];
            localMinutes = ParseField(parts[0], 0, 59, "minute");
            localHours = ParseField(parts[1], 0, 23, "hour");
            localDaysOfMonth = ParseField(parts[2], 1, 31, "day-of-month");
            localMonths = ParseField(parts[3], 1, 12, "month");
            localDaysOfWeek = ParseField(parts[4], 0, 6, "day-of-week");
            dayOfMonthField = parts[2];
            dayOfWeekField = parts[4];
        }
        else if (parts.Length == 6)
        {
            localSeconds = ParseField(parts[0], 0, 59, "second");
            localMinutes = ParseField(parts[1], 0, 59, "minute");
            localHours = ParseField(parts[2], 0, 23, "hour");
            localDaysOfMonth = ParseField(parts[3], 1, 31, "day-of-month");
            localMonths = ParseField(parts[4], 1, 12, "month");
            localDaysOfWeek = ParseField(parts[5], 0, 6, "day-of-week");
            dayOfMonthField = parts[3];
            dayOfWeekField = parts[5];
        }
        else
        {
            throw new FormatException($"Cron expression must have 5 or 6 fields: '{expression}'.");
        }

        return new CronExpression(
            expression,
            localSeconds,
            localMinutes,
            localHours,
            localDaysOfMonth,
            localMonths,
            localDaysOfWeek,
            dayOfMonthField != "*",
            dayOfWeekField != "*");
    }

    /// <summary>
    /// Gets the next occurrence after the specified time.
    /// </summary>
    /// <param name="from">The base time.</param>
    /// <returns>The next occurrence, if any.</returns>
    public DateTimeOffset? GetNextOccurrence(DateTimeOffset from)
    {
        var candidate = new DateTimeOffset(
            from.Year,
            from.Month,
            from.Day,
            from.Hour,
            from.Minute,
            from.Second,
            from.Offset).AddSeconds(1);

        var limit = candidate.AddYears(4);
        while (candidate < limit)
        {
            if (!months.Contains(candidate.Month))
            {
                candidate = new DateTimeOffset(candidate.Year, candidate.Month, 1, 0, 0, 0, candidate.Offset).AddMonths(1);
                continue;
            }

            var dayOfMonthMatch = daysOfMonth.Contains(candidate.Day);
            var dayOfWeekMatch = daysOfWeek.Contains((int)candidate.DayOfWeek);
            var dayMatches = CalculateDayMatches(dayOfMonthMatch, dayOfWeekMatch);

            if (!dayMatches)
            {
                candidate = new DateTimeOffset(candidate.Year, candidate.Month, candidate.Day, 0, 0, 0, candidate.Offset).AddDays(1);
                continue;
            }

            if (!hours.Contains(candidate.Hour))
            {
                candidate = new DateTimeOffset(candidate.Year, candidate.Month, candidate.Day, candidate.Hour, 0, 0, candidate.Offset).AddHours(1);
                continue;
            }

            if (!minutes.Contains(candidate.Minute))
            {
                candidate = new DateTimeOffset(candidate.Year, candidate.Month, candidate.Day, candidate.Hour, candidate.Minute, 0, candidate.Offset).AddMinutes(1);
                continue;
            }

            if (!seconds.Contains(candidate.Second))
            {
                candidate = candidate.AddSeconds(1);
                continue;
            }

            return candidate;
        }

        return null;
    }

    private static HashSet<int> ParseField(string field, int min, int max, string name)
    {
        var result = new HashSet<int>();
        foreach (var term in field.Split(','))
        {
            if (term.Length == 0)
            {
                throw new FormatException($"Invalid {name} field: '{field}'.");
            }

            var step = 1;
            var rangePart = term;
            var slashIndex = term.IndexOf('/');
            if (slashIndex >= 0)
            {
                rangePart = term[..slashIndex];
                var stepPart = term[(slashIndex + 1)..];
                if (!int.TryParse(stepPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out step) || step <= 0)
                {
                    throw new FormatException($"Invalid step in {name} field: '{term}'.");
                }
            }

            int start;
            int end;
            if (rangePart == "*")
            {
                start = min;
                end = max;
            }
            else
            {
                var dashIndex = rangePart.IndexOf('-');
                if (dashIndex > 0)
                {
                    var startPart = rangePart[..dashIndex];
                    var endPart = rangePart[(dashIndex + 1)..];
                    if (!int.TryParse(startPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out start)
                        || !int.TryParse(endPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out end))
                    {
                        throw new FormatException($"Invalid range in {name} field: '{term}'.");
                    }
                }
                else
                {
                    if (!int.TryParse(rangePart, NumberStyles.Integer, CultureInfo.InvariantCulture, out start))
                    {
                        throw new FormatException($"Invalid value in {name} field: '{term}'.");
                    }

                    end = slashIndex >= 0 ? max : start;
                }
            }

            if (start < min || end > max || start > end)
            {
                throw new FormatException($"Value out of range [{min},{max}] in {name} field: '{term}'.");
            }

            for (var value = start; value <= end; value += step)
            {
                result.Add(value);
            }
        }

        return result;
    }

    private bool CalculateDayMatches(bool dayOfMonthMatch, bool dayOfWeekMatch)
    {
        if (dayOfMonthRestricted && dayOfWeekRestricted)
        {
            return dayOfMonthMatch || dayOfWeekMatch;
        }

        if (dayOfMonthRestricted)
        {
            return dayOfMonthMatch;
        }

        return dayOfWeekRestricted ? dayOfWeekMatch : true;
    }
}
