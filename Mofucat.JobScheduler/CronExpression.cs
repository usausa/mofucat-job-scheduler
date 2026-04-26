namespace Mofucat.JobScheduler;

using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
/// Parses and evaluates a cron expression.
/// </summary>
public sealed class CronExpression
{
    private const int MonthMin = 1;
    private const int MonthMax = 12;
    private const int DayOfMonthMin = 1;
    private const int DayOfMonthMax = 31;
    private const byte DayOfMonthRestrictedFlag = 1;
    private const byte DayOfWeekRestrictedFlag = 2;
    private static ReadOnlySpan<byte> DaysInMonthTable => [0, 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
    private static ReadOnlySpan<byte> DayOfWeekTable => [0, 3, 2, 5, 0, 3, 5, 1, 4, 6, 2, 4];

    private readonly ulong secondsMask;
    private readonly ulong minutesMask;
    private readonly uint hoursMask;
    private readonly uint daysOfMonthMask;
    private readonly ushort monthsMask;
    private readonly byte daysOfWeekMask;
    private readonly byte flags;
    private readonly bool includesSeconds;

    private CronExpression(
        string expression,
        ulong secondsMask,
        ulong minutesMask,
        uint hoursMask,
        uint daysOfMonthMask,
        ushort monthsMask,
        byte daysOfWeekMask,
        byte flags,
        bool includesSeconds)
    {
        Expression = expression;
        this.secondsMask = secondsMask;
        this.minutesMask = minutesMask;
        this.hoursMask = hoursMask;
        this.daysOfMonthMask = daysOfMonthMask;
        this.monthsMask = monthsMask;
        this.daysOfWeekMask = daysOfWeekMask;
        this.flags = flags;
        this.includesSeconds = includesSeconds;
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

        var span = expression.AsSpan();
        Span<Range> ranges = stackalloc Range[6];
        var rangeCount = SplitFields(span, ranges);

        var localSecondsMask = 1UL;
        ulong localMinutesMask;
        uint localHoursMask;
        uint localDaysOfMonthMask;
        ushort localMonthsMask;
        byte localDaysOfWeekMask;
        ReadOnlySpan<char> dayOfMonthField;
        ReadOnlySpan<char> dayOfWeekField;

        if (rangeCount == 5)
        {
            localMinutesMask = ParseFieldMask64(span[ranges[0]], 0, 59, "minute");
            localHoursMask = (uint)ParseFieldMask64(span[ranges[1]], 0, 23, "hour");
            localDaysOfMonthMask = (uint)ParseFieldMask64(span[ranges[2]], DayOfMonthMin, DayOfMonthMax, "day-of-month");
            localMonthsMask = (ushort)ParseFieldMask64(span[ranges[3]], MonthMin, MonthMax, "month");
            localDaysOfWeekMask = (byte)ParseFieldMask64(span[ranges[4]], 0, 6, "day-of-week");
            dayOfMonthField = span[ranges[2]];
            dayOfWeekField = span[ranges[4]];
        }
        else if (rangeCount == 6)
        {
            localSecondsMask = ParseFieldMask64(span[ranges[0]], 0, 59, "second");
            localMinutesMask = ParseFieldMask64(span[ranges[1]], 0, 59, "minute");
            localHoursMask = (uint)ParseFieldMask64(span[ranges[2]], 0, 23, "hour");
            localDaysOfMonthMask = (uint)ParseFieldMask64(span[ranges[3]], DayOfMonthMin, DayOfMonthMax, "day-of-month");
            localMonthsMask = (ushort)ParseFieldMask64(span[ranges[4]], MonthMin, MonthMax, "month");
            localDaysOfWeekMask = (byte)ParseFieldMask64(span[ranges[5]], 0, 6, "day-of-week");
            dayOfMonthField = span[ranges[3]];
            dayOfWeekField = span[ranges[5]];
        }
        else
        {
            throw new FormatException($"Cron expression must have 5 or 6 fields: '{expression}'.");
        }

        return new CronExpression(
            expression,
            localSecondsMask,
            localMinutesMask,
            localHoursMask,
            localDaysOfMonthMask,
            localMonthsMask,
            localDaysOfWeekMask,
            GetFlags(dayOfMonthField, dayOfWeekField),
            rangeCount == 6);
    }

    /// <summary>
    /// Gets the next occurrence after the specified time.
    /// </summary>
    /// <param name="from">The base time.</param>
    /// <returns>The next occurrence, if any.</returns>
    public DateTimeOffset? GetNextOccurrence(DateTimeOffset from)
        => includesSeconds ? GetNextOccurrenceWithSeconds(from) : GetNextOccurrenceWithoutSeconds(from);

    private DateTimeOffset? GetNextOccurrenceWithoutSeconds(DateTimeOffset from)
    {
        var offset = from.Offset;
        var year = from.Year;
        var month = from.Month;
        var day = from.Day;
        var hour = from.Hour;
        var minute = from.Minute;
        var second = 0;

        if (from.Second >= 59)
        {
            minute++;
        }

        if (minute > 59)
        {
            minute = 0;
            hour++;
        }

        if (hour > 23)
        {
            hour = 0;
            day++;
            AdjustDay(ref year, ref month, ref day);
        }

        var maxYear = year + 5;

        while (year < maxYear)
        {
            if ((monthsMask & (1 << month)) == 0)
            {
                if (!AdvanceToNextMonth(ref year, ref month, maxYear))
                {
                    break;
                }

                day = 1;
                hour = 0;
                minute = 0;
                second = 0;
                continue;
            }

            var maxDay = DaysInMonth(year, month);
            if (day > maxDay)
            {
                month++;
                if (month > MonthMax)
                {
                    month = MonthMin;
                    year++;
                }

                day = 1;
                hour = 0;
                minute = 0;
                second = 0;
                continue;
            }

            if (!IsDayMatch(year, month, day))
            {
                day++;
                hour = 0;
                minute = 0;
                second = 0;
                if (day > maxDay)
                {
                    month++;
                    if (month > MonthMax)
                    {
                        month = MonthMin;
                        year++;
                    }

                    day = 1;
                }

                continue;
            }

            var nextHour = FindNextBit(hoursMask, hour, 23);
            if (nextHour < 0)
            {
                day++;
                hour = 0;
                minute = 0;
                second = 0;
                if (day > maxDay)
                {
                    month++;
                    if (month > MonthMax)
                    {
                        month = MonthMin;
                        year++;
                    }

                    day = 1;
                }

                continue;
            }

            if (nextHour != hour)
            {
                hour = nextHour;
                minute = 0;
                second = 0;
            }

            var nextMinute = FindNextBit(minutesMask, minute, 59);
            if (nextMinute < 0)
            {
                hour++;
                minute = 0;
                second = 0;
                if (hour > 23)
                {
                    day++;
                    hour = 0;
                    if (day > maxDay)
                    {
                        month++;
                        if (month > MonthMax)
                        {
                            month = MonthMin;
                            year++;
                        }

                        day = 1;
                    }
                }

                continue;
            }

            minute = nextMinute;

            if (year == from.Year
                && month == from.Month
                && day == from.Day
                && hour == from.Hour
                && minute == from.Minute
                && second == from.Second)
            {
                day++;
                hour = 0;
                minute = 0;
                second = 0;
                AdjustDay(ref year, ref month, ref day);

                continue;
            }

            return new DateTimeOffset(year, month, day, hour, minute, second, offset);
        }

        return null;
    }

    private DateTimeOffset? GetNextOccurrenceWithSeconds(DateTimeOffset from)
    {
        var offset = from.Offset;
        var year = from.Year;
        var month = from.Month;
        var day = from.Day;
        var hour = from.Hour;
        var minute = from.Minute;
        var second = from.Second + 1;

        if (second > 59)
        {
            second = 0;
            minute++;
        }

        if (minute > 59)
        {
            minute = 0;
            hour++;
        }

        if (hour > 23)
        {
            hour = 0;
            day++;
            AdjustDay(ref year, ref month, ref day);
        }

        var maxYear = year + 5;

        while (year < maxYear)
        {
            if ((monthsMask & (1 << month)) == 0)
            {
                if (!AdvanceToNextMonth(ref year, ref month, maxYear))
                {
                    break;
                }

                day = 1;
                hour = 0;
                minute = 0;
                second = 0;
                continue;
            }

            var maxDay = DaysInMonth(year, month);
            if (day > maxDay)
            {
                month++;
                if (month > MonthMax)
                {
                    month = MonthMin;
                    year++;
                }

                day = 1;
                hour = 0;
                minute = 0;
                second = 0;
                continue;
            }

            if (!IsDayMatch(year, month, day))
            {
                day++;
                hour = 0;
                minute = 0;
                second = 0;
                if (day > maxDay)
                {
                    month++;
                    if (month > MonthMax)
                    {
                        month = MonthMin;
                        year++;
                    }

                    day = 1;
                }

                continue;
            }

            var nextHour = FindNextBit(hoursMask, hour, 23);
            if (nextHour < 0)
            {
                day++;
                hour = 0;
                minute = 0;
                second = 0;
                if (day > maxDay)
                {
                    month++;
                    if (month > MonthMax)
                    {
                        month = MonthMin;
                        year++;
                    }

                    day = 1;
                }

                continue;
            }

            if (nextHour != hour)
            {
                hour = nextHour;
                minute = 0;
                second = 0;
            }

            var nextMinute = FindNextBit(minutesMask, minute, 59);
            if (nextMinute < 0)
            {
                hour++;
                minute = 0;
                second = 0;
                if (hour > 23)
                {
                    day++;
                    hour = 0;
                    if (day > maxDay)
                    {
                        month++;
                        if (month > MonthMax)
                        {
                            month = MonthMin;
                            year++;
                        }

                        day = 1;
                    }
                }

                continue;
            }

            minute = nextMinute;

            var nextSecond = FindNextBit(secondsMask, second, 59);
            if (nextSecond < 0)
            {
                var resetSecond = FindNextBit(secondsMask, 0, 59);
                if (resetSecond < 0)
                {
                    return null;
                }

                minute++;
                second = resetSecond;
                if (minute > 59)
                {
                    minute = 0;
                    hour++;
                    if (hour > 23)
                    {
                        hour = 0;
                        day++;
                        if (day > maxDay)
                        {
                            month++;
                            if (month > MonthMax)
                            {
                                month = MonthMin;
                                year++;
                            }

                            day = 1;
                        }
                    }
                }

                continue;
            }

            second = nextSecond;

            if (year == from.Year
                && month == from.Month
                && day == from.Day
                && hour == from.Hour
                && minute == from.Minute
                && second == from.Second)
            {
                day++;
                hour = 0;
                minute = 0;
                second = 0;
                AdjustDay(ref year, ref month, ref day);

                continue;
            }

            return new DateTimeOffset(year, month, day, hour, minute, second, offset);
        }

        return null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte GetFlags(ReadOnlySpan<char> dayOfMonthField, ReadOnlySpan<char> dayOfWeekField)
    {
        byte localFlags = 0;
        if (!IsWildcard(dayOfMonthField))
        {
            localFlags |= DayOfMonthRestrictedFlag;
        }

        if (!IsWildcard(dayOfWeekField))
        {
            localFlags |= DayOfWeekRestrictedFlag;
        }

        return localFlags;
    }

    private static int SplitFields(ReadOnlySpan<char> expression, Span<Range> ranges)
    {
        var count = 0;
        var index = 0;
        while (index < expression.Length && count < ranges.Length)
        {
            while (index < expression.Length && expression[index] == ' ')
            {
                index++;
            }

            if (index >= expression.Length)
            {
                break;
            }

            var start = index;
            while (index < expression.Length && expression[index] != ' ')
            {
                index++;
            }

            ranges[count++] = start..index;
        }

        while (index < expression.Length)
        {
            if (expression[index] != ' ')
            {
                return count + 1;
            }

            index++;
        }

        return count;
    }

    private static ulong ParseFieldMask64(ReadOnlySpan<char> field, int min, int max, string name)
    {
        var result = 0UL;
        var tokenStart = 0;

        while (tokenStart <= field.Length)
        {
            var tokenEnd = tokenStart;
            while (tokenEnd < field.Length && field[tokenEnd] != ',')
            {
                tokenEnd++;
            }

            var term = field[tokenStart..tokenEnd];
            if (term.IsEmpty)
            {
                throw new FormatException($"Invalid {name} field: '{field}'.");
            }

            var slashIndex = term.IndexOf('/');
            var rangePart = slashIndex >= 0 ? term[..slashIndex] : term;
            var step = slashIndex >= 0 ? ParsePositiveInt32(term[(slashIndex + 1)..], name, term) : 1;

            int start;
            int end;

            if (IsWildcard(rangePart))
            {
                start = min;
                end = max;
            }
            else
            {
                var dashIndex = rangePart.IndexOf('-');
                if (dashIndex > 0)
                {
                    if (!TryParseUInt32(rangePart[..dashIndex], out var parsedStart)
                        || !TryParseUInt32(rangePart[(dashIndex + 1)..], out var parsedEnd))
                    {
                        throw new FormatException($"Invalid range in {name} field: '{term}'.");
                    }

                    start = (int)parsedStart;
                    end = (int)parsedEnd;
                }
                else
                {
                    if (!TryParseUInt32(rangePart, out var parsedValue))
                    {
                        throw new FormatException($"Invalid value in {name} field: '{term}'.");
                    }

                    start = (int)parsedValue;
                    end = slashIndex >= 0 ? max : start;
                }
            }

            if (start < min || end > max || start > end)
            {
                throw new FormatException($"Value out of range [{min},{max}] in {name} field: '{term}'.");
            }

            for (var value = start; value <= end; value += step)
            {
                result |= 1UL << value;
            }

            if (tokenEnd == field.Length)
            {
                break;
            }

            tokenStart = tokenEnd + 1;
        }

        return result;
    }

    private static int ParsePositiveInt32(ReadOnlySpan<char> value, string name, ReadOnlySpan<char> term)
    {
        if (!TryParseUInt32(value, out var parsedValue) || parsedValue == 0 || parsedValue > int.MaxValue)
        {
            throw new FormatException($"Invalid step in {name} field: '{term}'.");
        }

        return (int)parsedValue;
    }

    private static bool TryParseUInt32(ReadOnlySpan<char> value, out uint result)
    {
        if (value.IsEmpty)
        {
            result = 0;
            return false;
        }

        if (value.Length is >= 1 and <= 4)
        {
            var accumulator = 0u;
            foreach (var c in value)
            {
                var digit = c - '0';
                if ((uint)digit > 9)
                {
                    result = 0;
                    return false;
                }

                accumulator = (accumulator * 10) + (uint)digit;
            }

            result = accumulator;
            return true;
        }

        if (!uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result))
        {
            return false;
        }

        return true;
    }

    private static bool IsWildcard(ReadOnlySpan<char> value) => value.Length == 1 && value[0] == '*';

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int DaysInMonth(int year, int month)
    {
        var days = Unsafe.Add(ref MemoryMarshal.GetReference(DaysInMonthTable), month);
        if (month == 2 && IsLeapYear(year))
        {
            days = 29;
        }

        return days;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsLeapYear(int year)
        => (year & 3) == 0 && ((year % 25) != 0 || (year & 15) == 0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsDayMatch(int year, int month, int day)
    {
        var localFlags = flags;
        if ((localFlags & (DayOfMonthRestrictedFlag | DayOfWeekRestrictedFlag)) == 0)
        {
            return true;
        }

        var dayOfMonthMatch = (daysOfMonthMask & (1U << day)) != 0;
        var dayOfWeekMatch = (daysOfWeekMask & (1 << CalcDayOfWeek(year, month, day))) != 0;

        if ((localFlags & (DayOfMonthRestrictedFlag | DayOfWeekRestrictedFlag)) == (DayOfMonthRestrictedFlag | DayOfWeekRestrictedFlag))
        {
            return dayOfMonthMatch || dayOfWeekMatch;
        }

        return (localFlags & DayOfMonthRestrictedFlag) != 0 ? dayOfMonthMatch : dayOfWeekMatch;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CalcDayOfWeek(int year, int month, int day)
    {
        if (month < 3)
        {
            year--;
        }

        var monthOffset = Unsafe.Add(ref MemoryMarshal.GetReference(DayOfWeekTable), month - 1);
        return (year + (year / 4) - (year / 100) + (year / 400) + monthOffset + day) % 7;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool AdvanceToNextMonth(ref int year, ref int month, int maxYear)
    {
        var nextMonth = FindNextBit(monthsMask, month + 1, MonthMax);
        if (nextMonth >= 0)
        {
            month = nextMonth;
            return true;
        }

        year++;
        if (year >= maxYear)
        {
            return false;
        }

        nextMonth = FindNextBit(monthsMask, MonthMin, MonthMax);
        if (nextMonth < 0)
        {
            return false;
        }

        month = nextMonth;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AdjustDay(ref int year, ref int month, ref int day)
    {
        var maxDay = DaysInMonth(year, month);
        if (day > maxDay)
        {
            day = 1;
            month++;
            if (month > MonthMax)
            {
                month = MonthMin;
                year++;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FindNextBit(ulong mask, int startBit, int maxBit)
    {
        var filteredMask = mask & ~((1UL << startBit) - 1);
        if (filteredMask == 0)
        {
            return -1;
        }

        var position = BitOperations.TrailingZeroCount(filteredMask);
        return position <= maxBit ? position : -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FindNextBit(uint mask, int startBit, int maxBit)
    {
        var filteredMask = mask & ~((1U << startBit) - 1U);
        if (filteredMask == 0)
        {
            return -1;
        }

        var position = BitOperations.TrailingZeroCount(filteredMask);
        return position <= maxBit ? position : -1;
    }

}
