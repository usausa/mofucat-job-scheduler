namespace Mofucat.JobScheduler;

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public sealed class CronExpression
{
    // day-of-month is restricted by a non-wildcard value
    private const byte DayOfMonthRestricted = 0x1;
    // day-of-week is restricted by a non-wildcard value
    private const byte DayOfWeekRestricted = 0x2;

    // Days per month table
    private static ReadOnlySpan<byte> DaysInMonthTable => [0, 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
    // Helper table used by the day-of-week calculation
    private static ReadOnlySpan<byte> DayOfWeekTable => [0, 3, 2, 5, 0, 3, 5, 1, 4, 6, 2, 4];

    // Includes seconds
    private readonly bool includesSeconds;

    // Bitmasks for matching candidates
    private readonly ulong secondsMask;
    private readonly ulong minutesMask;
    private readonly uint hoursMask;
    private readonly uint daysOfMonthMask;
    private readonly ushort monthsMask;
    private readonly byte daysOfWeekMask;

    // Flags
    private readonly byte flags;

    public string Expression { get; }

    //--------------------------------------------------------------------------------
    // Constructor
    //--------------------------------------------------------------------------------

    private CronExpression(
        string expression,
        bool includesSeconds,
        ulong secondsMask,
        ulong minutesMask,
        uint hoursMask,
        uint daysOfMonthMask,
        ushort monthsMask,
        byte daysOfWeekMask,
        byte flags)
    {
        Expression = expression;
        this.includesSeconds = includesSeconds;
        this.secondsMask = secondsMask;
        this.minutesMask = minutesMask;
        this.hoursMask = hoursMask;
        this.daysOfMonthMask = daysOfMonthMask;
        this.monthsMask = monthsMask;
        this.daysOfWeekMask = daysOfWeekMask;
        this.flags = flags;
    }

    //--------------------------------------------------------------------------------
    // Parse
    //--------------------------------------------------------------------------------

    [SkipLocalsInit]
    public static CronExpression Parse(string expression)
    {
        var span = expression.AsSpan();
        Span<Range> ranges = stackalloc Range[6];
        var rangeCount = SplitFields(span, ranges);

        if (rangeCount == 5)
        {
            return new CronExpression(
                expression,
                false,
                1UL,
                ParseFieldMask64(span[ranges[0]], 0, 59, "minute"),
                (uint)ParseFieldMask64(span[ranges[1]], 0, 23, "hour"),
                (uint)ParseFieldMask64(span[ranges[2]], 1, 31, "day-of-month"),
                (ushort)ParseFieldMask64(span[ranges[3]], 1, 12, "month"),
                (byte)ParseFieldMask64(span[ranges[4]], 0, 6, "day-of-week"),
                GetFlags(span[ranges[2]], span[ranges[4]]));
        }
        if (rangeCount == 6)
        {
            return new CronExpression(
                expression,
                true,
                ParseFieldMask64(span[ranges[0]], 0, 59, "second"),
                ParseFieldMask64(span[ranges[1]], 0, 59, "minute"),
                (uint)ParseFieldMask64(span[ranges[2]], 0, 23, "hour"),
                (uint)ParseFieldMask64(span[ranges[3]], 1, 31, "day-of-month"),
                (ushort)ParseFieldMask64(span[ranges[4]], 1, 12, "month"),
                (byte)ParseFieldMask64(span[ranges[5]], 0, 6, "day-of-week"),
                GetFlags(span[ranges[3]], span[ranges[5]]));
        }

        throw new FormatException($"Cron expression must have 5 or 6 fields. expression=[{expression}]");
    }

    //--------------------------------------------------------------------------------
    // Next
    //--------------------------------------------------------------------------------

    public DateTimeOffset? GetNextOccurrence(DateTimeOffset from) =>
        includesSeconds ? GetNextOccurrenceWithSeconds(from) : GetNextOccurrenceWithoutSeconds(from);

    private DateTimeOffset? GetNextOccurrenceWithoutSeconds(DateTimeOffset from)
    {
        var year = from.Year;
        var month = from.Month;
        var day = from.Day;
        var hour = from.Hour;
        var minute = from.Minute + 1;
        var offset = from.Offset;

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
            // Reset the lower components and advance to the next matching month
            if ((monthsMask & (1 << month)) == 0)
            {
                if (!AdvanceToNextMonth(ref year, ref month, maxYear))
                {
                    break;
                }

                day = 1;
                hour = 0;
                minute = 0;
                continue;
            }

            var maxDay = DaysInMonth(year, month);
            if (day > maxDay)
            {
                // If the current day exceeds the end of the month, move to the first day of the next month
                month++;
                if (month > 12)
                {
                    month = 1;
                    year++;
                }

                day = 1;
                hour = 0;
                minute = 0;
                continue;
            }

            if (!IsDayMatch(year, month, day))
            {
                // If the day does not match, try the next day
                day++;
                hour = 0;
                minute = 0;
                if (day > maxDay)
                {
                    month++;
                    if (month > 12)
                    {
                        month = 1;
                        year++;
                    }

                    day = 1;
                }

                continue;
            }

            var nextHour = FindNextBit(hoursMask, hour, 23);
            if (nextHour < 0)
            {
                // If no matching hour exists on this day, advance to the next day
                day++;
                hour = 0;
                minute = 0;
                if (day > maxDay)
                {
                    month++;
                    if (month > 12)
                    {
                        month = 1;
                        year++;
                    }

                    day = 1;
                }

                continue;
            }

            if (nextHour != hour)
            {
                // When the hour changes, reset lower-order components and search again
                hour = nextHour;
                minute = 0;
            }

            var nextMinute = FindNextBit(minutesMask, minute, 59);
            if (nextMinute < 0)
            {
                // If no matching minute exists within the current hour, advance to the next hour
                hour++;
                minute = 0;
                if (hour > 23)
                {
                    day++;
                    hour = 0;
                    if (day > maxDay)
                    {
                        month++;
                        if (month > 12)
                        {
                            month = 1;
                            year++;
                        }

                        day = 1;
                    }
                }

                continue;
            }

            minute = nextMinute;

            return new DateTimeOffset(year, month, day, hour, minute, 0, offset);
        }

        return null;
    }

    private DateTimeOffset? GetNextOccurrenceWithSeconds(DateTimeOffset from)
    {
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

        var offset = from.Offset;
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
                if (month > 12)
                {
                    month = 1;
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
                    if (month > 12)
                    {
                        month = 1;
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
                    if (month > 12)
                    {
                        month = 1;
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
                        if (month > 12)
                        {
                            month = 1;
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
                minute++;
                second = 0;
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
                            if (month > 12)
                            {
                                month = 1;
                                year++;
                            }

                            day = 1;
                        }
                    }
                }

                continue;
            }

            second = nextSecond;

            return new DateTimeOffset(year, month, day, hour, minute, second, offset);
        }

        return null;
    }

    //--------------------------------------------------------------------------------
    // Parse helper
    //--------------------------------------------------------------------------------

    private static int SplitFields(ReadOnlySpan<char> expression, Span<Range> ranges)
    {
        var count = 0;
        var index = 0;
        while ((index < expression.Length) && (count < ranges.Length))
        {
            while ((index < expression.Length) && (expression[index] == ' '))
            {
                index++;
            }

            if (index >= expression.Length)
            {
                break;
            }

            var start = index;
            while ((index < expression.Length) && (expression[index] != ' '))
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
        // *
        if ((field.Length == 1) && (field[0] == '*'))
        {
            return BuildMask(min, max, 1);
        }

        // */n
        if ((field.Length > 2) && (field[0] == '*') && (field[1] == '/') && (field.IndexOf(',') < 0) && (field.IndexOf('-') < 0))
        {
            return BuildMask(min, max, ParsePositiveInt32(field[2..], name, field));
        }

        if ((field.IndexOf(',') < 0) && (field.IndexOf('-') < 0) && (field.IndexOf('/') < 0))
        {
            if (!TryParseUInt32(field, out var parsedValue))
            {
                throw new FormatException($"Invalid value in field. name=[{name}], field=[{field}]");
            }

            var value = (int)parsedValue;
            if ((value < min) || (value > max))
            {
                throw new FormatException($"Value out of range in field. min=[{min}], max=[{max}], name=[{name}], field=[{field}]");
            }

            return 1UL << value;
        }

        var result = 0UL;

        // Mixed
        var tokenStart = 0;
        while (tokenStart <= field.Length)
        {
            var tokenEnd = tokenStart;
            while ((tokenEnd < field.Length) && (field[tokenEnd] != ','))
            {
                tokenEnd++;
            }

            var term = field[tokenStart..tokenEnd];
            if (term.IsEmpty)
            {
                throw new FormatException($"Invalid field. name=[{name}], field=[{field}]");
            }

            var slashIndex = term.IndexOf('/');
            var rangePart = slashIndex >= 0 ? term[..slashIndex] : term;
            var step = slashIndex >= 0 ? ParsePositiveInt32(term[(slashIndex + 1)..], name, term) : 1;

            int start;
            int end;

            if (IsWildcard(rangePart))
            {
                // */n, *
                start = min;
                end = max;
            }
            else
            {
                var dashIndex = rangePart.IndexOf('-');
                if (dashIndex > 0)
                {
                    // a-b
                    if (!TryParseUInt32(rangePart[..dashIndex], out var parsedStart)
                        || !TryParseUInt32(rangePart[(dashIndex + 1)..], out var parsedEnd))
                    {
                        throw new FormatException($"Invalid range in field. name=[{name}], term=[{term}]");
                    }

                    start = (int)parsedStart;
                    end = (int)parsedEnd;
                }
                else
                {
                    // Single value or a/n
                    if (!TryParseUInt32(rangePart, out var parsedValue))
                    {
                        throw new FormatException($"Invalid value in field. name=[{name}], term=[{term}]");
                    }

                    start = (int)parsedValue;
                    end = slashIndex >= 0 ? max : start;
                }
            }

            if ((start < min) || (end > max) || (start > end))
            {
                throw new FormatException($"Value out of range in field. min=[{min}], max=[{max}], name=[{name}], term=[{term}]");
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong BuildMask(int start, int end, int step)
    {
        var result = 0UL;
        for (var i = start; i <= end; i += step)
        {
            result |= 1UL << i;
        }

        return result;
    }

    private static int ParsePositiveInt32(ReadOnlySpan<char> value, string name, ReadOnlySpan<char> term)
    {
        if ((!TryParseUInt32(value, out var parsedValue)) || (parsedValue == 0) || (parsedValue > int.MaxValue))
        {
            throw new FormatException($"Invalid step in field. name=[{name}], term=[{term}]");
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

        var accumulator = 0u;
        foreach (var c in value)
        {
            var digit = c - '0';
            if ((uint)digit > 9)
            {
                result = 0;
                return false;
            }

            var next = (accumulator * 10) + (uint)digit;
            if (next < accumulator)
            {
                // Overflow
                result = 0;
                return false;
            }

            accumulator = next;
        }

        result = accumulator;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte GetFlags(ReadOnlySpan<char> dayOfMonthField, ReadOnlySpan<char> dayOfWeekField)
    {
        var flags = (byte)0;
        if (!IsWildcard(dayOfMonthField))
        {
            flags |= DayOfMonthRestricted;
        }

        if (!IsWildcard(dayOfWeekField))
        {
            flags |= DayOfWeekRestricted;
        }

        return flags;
    }

    private static bool IsWildcard(ReadOnlySpan<char> value) => (value.Length == 1) && (value[0] == '*');

    //--------------------------------------------------------------------------------
    // Date helper
    //--------------------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int DaysInMonth(int year, int month)
    {
        // Use the lookup table for regular months and apply leap-year correction for February
        var days = Unsafe.Add(ref MemoryMarshal.GetReference(DaysInMonthTable), month);
        if ((month == 2) && IsLeapYear(year))
        {
            days = 29;
        }

        return days;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsLeapYear(int year) =>
        ((year & 3) == 0) && (((year % 25) != 0) || ((year & 15) == 0));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsDayMatch(int year, int month, int day)
    {
        // If there are no day restrictions, every day matches
        var localFlags = flags;
        if ((localFlags & (DayOfMonthRestricted | DayOfWeekRestricted)) == 0)
        {
            return true;
        }

        if ((localFlags & (DayOfMonthRestricted | DayOfWeekRestricted)) == (DayOfMonthRestricted | DayOfWeekRestricted))
        {
            // When both are restricted, cron semantics treat them as an OR condition.
            return ((daysOfMonthMask & (1U << day)) != 0) ||
                   ((daysOfWeekMask & (1 << CalcDayOfWeek(year, month, day))) != 0);
        }

        // When only one side is restricted, only that condition is used
        if ((localFlags & DayOfMonthRestricted) != 0)
        {
            return (daysOfMonthMask & (1U << day)) != 0;
        }

        return (daysOfWeekMask & (1 << CalcDayOfWeek(year, month, day))) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CalcDayOfWeek(int year, int month, int day)
    {
        // Simple day-of-week calculation that treats January and February as months 13 and 14 of the previous year
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
        // Prefer the next matching month within the same year
        var nextMonth = FindNextBit(monthsMask, month + 1, 12);
        if (nextMonth >= 0)
        {
            month = nextMonth;
            return true;
        }

        // If no candidate exists in the current year, move to the next year and find the first matching month
        year++;
        if (year >= maxYear)
        {
            return false;
        }

        nextMonth = FindNextBit(monthsMask, 1, 12);
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
        // After incrementing the day, carry into month and year if the end of the month is exceeded
        var maxDay = DaysInMonth(year, month);
        if (day > maxDay)
        {
            day = 1;
            month++;
            if (month > 12)
            {
                month = 1;
                year++;
            }
        }
    }

    //--------------------------------------------------------------------------------
    // Bit helper
    //--------------------------------------------------------------------------------

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
