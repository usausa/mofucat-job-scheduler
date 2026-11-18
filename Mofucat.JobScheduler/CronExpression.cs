namespace Mofucat.JobScheduler;

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public sealed class CronExpression
{
    // day-of-month is restricted by a non-wildcard value
    private const byte DayOfMonthRestricted = 0x1;
    // day-of-week is restricted by a non-wildcard value
    private const byte DayOfWeekRestricted = 0x2;
    private const byte TimeWildcard = 0x4;

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

    public static CronExpression Parse(string expression)
    {
        var failure = default(ParseFailure);
        return ParseCore(expression, ref failure) ?? throw CreateFormatException(expression, failure);
    }

    public static bool TryParse([NotNullWhen(true)] string? expression, [NotNullWhen(true)] out CronExpression? result)
    {
        var failure = default(ParseFailure);
        result = expression is null ? null : ParseCore(expression, ref failure);
        return result is not null;
    }

    private static CronExpression? ParseCore(string expression, ref ParseFailure failure)
    {
        var span = expression.AsSpan();
        var index = 0;
        while ((index < span.Length) && IsSeparator(span[index]))
        {
            index++;
        }

        if ((index < span.Length) && (span[index] == '@'))
        {
            return ParseMacro(expression, index, ref failure);
        }

        var fieldCount = CountFields(span);
        if ((fieldCount != 5) && (fieldCount != 6))
        {
            failure = new ParseFailure(ParseError.FieldCount, 0, String.Empty, 0, 0);
            return null;
        }

        var includesSeconds = fieldCount == 6;
        var seconds = 1UL;
        if (includesSeconds && !ParseField(span, ref index, 0, 59, "second", FieldNames.None, out seconds, out _, ref failure))
        {
            return null;
        }

        if (!ParseField(span, ref index, 0, 59, "minute", FieldNames.None, out var minutes, out var minuteShape, ref failure) ||
            !ParseField(span, ref index, 0, 23, "hour", FieldNames.None, out var hours, out var hourShape, ref failure) ||
            !ParseField(span, ref index, 1, 31, "day-of-month", FieldNames.None, out var daysOfMonth, out var dayOfMonthShape, ref failure) ||
            !ParseField(span, ref index, 1, 12, "month", FieldNames.Month, out var months, out _, ref failure) ||
            !ParseField(span, ref index, 0, 7, "day-of-week", FieldNames.DayOfWeek, out var daysOfWeek, out var dayOfWeekShape, ref failure))
        {
            return null;
        }

        return new CronExpression(
            expression,
            includesSeconds,
            seconds,
            minutes,
            (uint)hours,
            (uint)daysOfMonth,
            (ushort)months,
            (byte)NormalizeDayOfWeek(daysOfWeek),
            GetFlags(minuteShape, hourShape, dayOfMonthShape, dayOfWeekShape));
    }

    private static CronExpression? ParseMacro(string expression, int start, ref ParseFailure failure)
    {
        var span = expression.AsSpan();
        var end = start + 1;
        while ((end < span.Length) && !IsSeparator(span[end]))
        {
            end++;
        }

        for (var i = end; i < span.Length; i++)
        {
            if (!IsSeparator(span[i]))
            {
                failure = new ParseFailure(ParseError.Macro, start, String.Empty, 0, 0);
                return null;
            }
        }

        var allHours = (uint)BuildMask(0, 23, 1);
        var allDaysOfMonth = (uint)BuildMask(1, 31, 1);
        var allMonths = (ushort)BuildMask(1, 12, 1);
        var allDaysOfWeek = (byte)BuildMask(0, 6, 1);
        var result = span[(start + 1)..end] switch
        {
            "yearly" or "annually" => new CronExpression(expression, false, 1UL, 1UL, 1U, 1U << 1, 1 << 1, allDaysOfWeek, DayOfMonthRestricted),
            "monthly" => new CronExpression(expression, false, 1UL, 1UL, 1U, 1U << 1, allMonths, allDaysOfWeek, DayOfMonthRestricted),
            "weekly" => new CronExpression(expression, false, 1UL, 1UL, 1U, allDaysOfMonth, allMonths, 1, DayOfWeekRestricted),
            "daily" or "midnight" => new CronExpression(expression, false, 1UL, 1UL, 1U, allDaysOfMonth, allMonths, allDaysOfWeek, 0),
            "hourly" => new CronExpression(expression, false, 1UL, 1UL, allHours, allDaysOfMonth, allMonths, allDaysOfWeek, TimeWildcard),
            _ => null
        };

        if (result is null)
        {
            failure = new ParseFailure(ParseError.Macro, start, String.Empty, 0, 0);
        }

        return result;
    }

    private static FormatException CreateFormatException(string expression, ParseFailure failure) =>
        failure.Error switch
        {
            ParseError.FieldCount => new FormatException($"Cron expression must have 5 or 6 fields. expression=[{expression}]"),
            ParseError.Macro => new FormatException($"Unsupported macro. expression=[{expression}]"),
            ParseError.ValueOutOfRange => new FormatException($"Value out of range in field. min=[{failure.Min}], max=[{failure.Max}], name=[{failure.Name}], field=[{GetField(expression, failure.FieldStart)}]"),
            _ => new FormatException($"Invalid field. name=[{failure.Name}], field=[{GetField(expression, failure.FieldStart)}]")
        };

    //--------------------------------------------------------------------------------
    // Next
    //--------------------------------------------------------------------------------

    public DateTimeOffset? GetNextOccurrence(DateTimeOffset from) =>
        includesSeconds ? GetNextOccurrenceWithSeconds(from) : GetNextOccurrenceWithoutSeconds(from);

    public DateTimeOffset? GetNextOccurrence(DateTimeOffset from, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        if (!timeZone.SupportsDaylightSavingTime)
        {
            return GetNextOccurrence(from.ToOffset(timeZone.BaseUtcOffset));
        }

        var local = TimeZoneInfo.ConvertTime(from, timeZone);
        var next = FindNextLocal(local.DateTime, false);
        if (next is null)
        {
            return null;
        }

        var value = next.Value;
        if (!timeZone.IsAmbiguousTime(value))
        {
            var candidate = TimeZoneInfo.ConvertTime(new DateTimeOffset(value, local.Offset), timeZone);
            if (candidate.Offset == local.Offset)
            {
                return candidate;
            }
        }

        return GetNextOccurrenceAroundTransition(local, value, timeZone);
    }

    public IEnumerable<DateTimeOffset> GetOccurrences(DateTimeOffset from, DateTimeOffset to)
    {
        var next = GetNextOccurrence(MoveBack(from));
        while ((next is { } value) && (value < to))
        {
            yield return value;
            next = GetNextOccurrence(value);
        }
    }

    public IEnumerable<DateTimeOffset> GetOccurrences(DateTimeOffset from, DateTimeOffset to, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        return GetOccurrencesInTimeZone(from, to, timeZone);
    }

    private IEnumerable<DateTimeOffset> GetOccurrencesInTimeZone(DateTimeOffset from, DateTimeOffset to, TimeZoneInfo timeZone)
    {
        var next = GetNextOccurrence(MoveBack(from), timeZone);
        while ((next is { } value) && (value < to))
        {
            yield return value;
            next = GetNextOccurrence(value, timeZone);
        }
    }

    private static DateTimeOffset MoveBack(DateTimeOffset from) => from == DateTimeOffset.MinValue ? from : from.AddTicks(-1);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private DateTimeOffset? GetNextOccurrenceAroundTransition(DateTimeOffset local, DateTime value, TimeZoneInfo timeZone)
    {
        var time = local.DateTime;
        if (timeZone.IsAmbiguousTime(time))
        {
            var (earlyOffset, lateOffset) = GetAmbiguousOffsets(timeZone, time);
            var end = GetTransitionEnd(timeZone, time, true);
            var wildcard = (flags & TimeWildcard) != 0;
            if (local.Offset == earlyOffset)
            {
                if (value < end)
                {
                    return new DateTimeOffset(value, earlyOffset);
                }

                if (wildcard)
                {
                    var late = FindNextLocal(end - (earlyOffset - lateOffset), true);
                    if (late < end)
                    {
                        return new DateTimeOffset(late.Value, lateOffset);
                    }
                }
            }
            else if (value < end)
            {
                if (wildcard)
                {
                    return new DateTimeOffset(value, lateOffset);
                }

                var next = FindNextLocal(end, true);
                if (next is null)
                {
                    return null;
                }

                value = next.Value;
            }
        }

        return ResolveLocal(value, timeZone);
    }

    private DateTimeOffset? ResolveLocal(DateTime value, TimeZoneInfo timeZone)
    {
        while (timeZone.IsInvalidTime(value))
        {
            var end = GetTransitionEnd(timeZone, value, false);
            if ((flags & TimeWildcard) == 0)
            {
                return new DateTimeOffset(end, timeZone.GetUtcOffset(end));
            }

            var next = FindNextLocal(end, true);
            if (next is null)
            {
                return null;
            }

            value = next.Value;
        }

        if (timeZone.IsAmbiguousTime(value))
        {
            return new DateTimeOffset(value, GetAmbiguousOffsets(timeZone, value).Early);
        }

        return new DateTimeOffset(value, timeZone.GetUtcOffset(value));
    }

    private DateTime? FindNextLocal(DateTime time, bool inclusive) =>
        GetNextOccurrence(new DateTimeOffset(inclusive ? time.AddTicks(-1) : time, TimeSpan.Zero))?.DateTime;

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

            if (nextMinute != minute)
            {
                minute = nextMinute;
                second = 0;
            }

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

    private static int CountFields(ReadOnlySpan<char> expression)
    {
        var count = 0;
        var separated = true;
        foreach (var c in expression)
        {
            if (IsSeparator(c))
            {
                separated = true;
            }
            else if (separated)
            {
                separated = false;
                count++;
            }
        }

        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsSeparator(char c) =>
        (c == ' ') || ((c >= '\t') && (c <= '\r')) || ((c > '\u007F') && Char.IsWhiteSpace(c));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ParseField(ReadOnlySpan<char> expression, ref int index, int min, int max, string name, FieldNames names, out ulong mask, out FieldShape shape, ref ParseFailure failure)
    {
        while (IsSeparator(expression[index]))
        {
            index++;
        }

        var start = index;
        if (expression[index] == '*')
        {
            if (((index + 1) == expression.Length) || IsSeparator(expression[index + 1]))
            {
                index++;
                shape = FieldShape.Wildcard;
                mask = BuildMask(min, max, 1);
                return true;
            }

            shape = FieldShape.StartsWithWildcard;
        }
        else
        {
            shape = FieldShape.Other;
        }

        mask = 0UL;
        while (true)
        {
            int first;
            int last;
            if (expression[index] == '*')
            {
                index++;
                first = min;
                last = max;
            }
            else
            {
                var error = ParseValue(expression, ref index, min, max, names, out first);
                if (error != ParseError.None)
                {
                    return Fail(out failure, error, start, name, min, max);
                }

                last = first;
                if (index < expression.Length)
                {
                    if (expression[index] == '-')
                    {
                        index++;
                        error = ParseValue(expression, ref index, min, max, names, out last);
                        if (error != ParseError.None)
                        {
                            return Fail(out failure, error, start, name, min, max);
                        }

                        if (last < first)
                        {
                            return Fail(out failure, ParseError.ValueOutOfRange, start, name, min, max);
                        }
                    }
                    else if (expression[index] == '/')
                    {
                        last = max;
                    }
                }
            }

            var step = 1;
            if ((index < expression.Length) && (expression[index] == '/'))
            {
                index++;
                if (!ParseStep(expression, ref index, out step))
                {
                    return Fail(out failure, ParseError.InvalidField, start, name, min, max);
                }
            }

            mask |= BuildMask(first, last, step);

            if ((index == expression.Length) || IsSeparator(expression[index]))
            {
                return true;
            }

            if (expression[index] != ',')
            {
                return Fail(out failure, ParseError.InvalidField, start, name, min, max);
            }

            index++;
            if ((index == expression.Length) || IsSeparator(expression[index]))
            {
                return Fail(out failure, ParseError.InvalidField, start, name, min, max);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ParseError ParseValue(ReadOnlySpan<char> expression, ref int index, int min, int max, FieldNames names, out int value)
    {
        var number = (index < expression.Length) && ((uint)(expression[index] - '0') <= 9) ? ParseNumber(expression, ref index) : ParseName(expression, ref index, names);
        value = (int)number;
        if ((number >= min) && (number <= max))
        {
            return ParseError.None;
        }

        return number < 0 ? ParseError.InvalidField : ParseError.ValueOutOfRange;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ParseStep(ReadOnlySpan<char> expression, ref int index, out int step)
    {
        var value = ParseNumber(expression, ref index);
        step = (int)value;
        return (value > 0) && (value <= Int32.MaxValue);
    }

    private static long ParseName(ReadOnlySpan<char> expression, ref int index, FieldNames names)
    {
        var start = index;
        while ((index < expression.Length) && Char.IsAsciiLetter(expression[index]))
        {
            index++;
        }

        if ((names == FieldNames.None) || ((index - start) != 3))
        {
            return -1;
        }

        var key = ((expression[start] | 0x20) << 16) | ((expression[start + 1] | 0x20) << 8) | (expression[start + 2] | 0x20);
        return names == FieldNames.Month ? GetMonth(key) : GetDayOfWeek(key);
    }

    private static int GetMonth(int key) =>
        key switch
        {
            ('j' << 16) | ('a' << 8) | 'n' => 1,
            ('f' << 16) | ('e' << 8) | 'b' => 2,
            ('m' << 16) | ('a' << 8) | 'r' => 3,
            ('a' << 16) | ('p' << 8) | 'r' => 4,
            ('m' << 16) | ('a' << 8) | 'y' => 5,
            ('j' << 16) | ('u' << 8) | 'n' => 6,
            ('j' << 16) | ('u' << 8) | 'l' => 7,
            ('a' << 16) | ('u' << 8) | 'g' => 8,
            ('s' << 16) | ('e' << 8) | 'p' => 9,
            ('o' << 16) | ('c' << 8) | 't' => 10,
            ('n' << 16) | ('o' << 8) | 'v' => 11,
            ('d' << 16) | ('e' << 8) | 'c' => 12,
            _ => -1
        };

    private static int GetDayOfWeek(int key) =>
        key switch
        {
            ('s' << 16) | ('u' << 8) | 'n' => 0,
            ('m' << 16) | ('o' << 8) | 'n' => 1,
            ('t' << 16) | ('u' << 8) | 'e' => 2,
            ('w' << 16) | ('e' << 8) | 'd' => 3,
            ('t' << 16) | ('h' << 8) | 'u' => 4,
            ('f' << 16) | ('r' << 8) | 'i' => 5,
            ('s' << 16) | ('a' << 8) | 't' => 6,
            _ => -1
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long ParseNumber(ReadOnlySpan<char> expression, ref int index)
    {
        var start = index;
        var value = 0L;
        while (index < expression.Length)
        {
            var digit = expression[index] - '0';
            if ((uint)digit > 9)
            {
                break;
            }

            if (value <= Int32.MaxValue)
            {
                value = (value * 10) + digit;
            }

            index++;
        }

        return index == start ? -1 : value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong BuildMask(int start, int end, int step)
    {
        if (step == 1)
        {
            return (UInt64.MaxValue >> (63 - end)) & (UInt64.MaxValue << start);
        }

        var result = 0UL;
        for (var i = start; i <= end; i += step)
        {
            result |= 1UL << i;

            if (i > end - step)
            {
                break;
            }
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte GetFlags(FieldShape minute, FieldShape hour, FieldShape dayOfMonth, FieldShape dayOfWeek)
    {
        var flags = (byte)0;
        if (dayOfMonth != FieldShape.Wildcard)
        {
            flags |= DayOfMonthRestricted;
        }

        if (dayOfWeek != FieldShape.Wildcard)
        {
            flags |= DayOfWeekRestricted;
        }

        if ((minute != FieldShape.Other) || (hour != FieldShape.Other))
        {
            flags |= TimeWildcard;
        }

        return flags;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool Fail(out ParseFailure failure, ParseError error, int fieldStart, string name, int min, int max)
    {
        failure = new ParseFailure(error, fieldStart, name, min, max);
        return false;
    }

    private static ReadOnlySpan<char> GetField(ReadOnlySpan<char> expression, int fieldStart)
    {
        var end = fieldStart;
        while ((end < expression.Length) && !IsSeparator(expression[end]))
        {
            end++;
        }

        return expression[fieldStart..end];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong NormalizeDayOfWeek(ulong mask)
    {
        if ((mask & (1UL << 7)) != 0)
        {
            mask = (mask & ~(1UL << 7)) | 1UL;
        }

        return mask;
    }

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

    private static (TimeSpan Early, TimeSpan Late) GetAmbiguousOffsets(TimeZoneInfo timeZone, DateTime time)
    {
        var offsets = timeZone.GetAmbiguousTimeOffsets(time);
        return offsets[0] > offsets[1] ? (offsets[0], offsets[1]) : (offsets[1], offsets[0]);
    }

    private static DateTime GetTransitionEnd(TimeZoneInfo timeZone, DateTime time, bool ambiguous)
    {
        var end = new DateTime(time.Year, time.Month, time.Day, time.Hour, time.Minute, 0, DateTimeKind.Unspecified);
        do
        {
            end = end.AddMinutes(1);
        }
        while (ambiguous ? timeZone.IsAmbiguousTime(end) : timeZone.IsInvalidTime(end));

        return end;
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

    private enum FieldShape
    {
        Other,
        StartsWithWildcard,
        Wildcard
    }

    private enum FieldNames
    {
        None,
        Month,
        DayOfWeek
    }

    private enum ParseError
    {
        None,
        FieldCount,
        InvalidField,
        ValueOutOfRange,
        Macro
    }

    private readonly record struct ParseFailure(ParseError Error, int FieldStart, string Name, int Min, int Max);
}
