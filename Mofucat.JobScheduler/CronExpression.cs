namespace Mofucat.JobScheduler;

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public sealed class CronExpression
{
    private const int MonthMin = 1;
    private const int MonthMax = 12;
    private const int DayOfMonthMin = 1;
    private const int DayOfMonthMax = 31;

    // day-of-month がワイルドカード以外で制約されていることを示すフラグ。
    private const byte DayOfMonthRestrictedFlag = 0x1;
    // day-of-week がワイルドカード以外で制約されていることを示すフラグ。
    private const byte DayOfWeekRestrictedFlag = 0x2;

    // 月ごとの日数テーブル。0 番目は未使用。
    private static ReadOnlySpan<byte> DaysInMonthTable => [0, 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
    // ツェラー系計算で曜日算出に用いる補助テーブル。
    private static ReadOnlySpan<byte> DayOfWeekTable => [0, 3, 2, 5, 0, 3, 5, 1, 4, 6, 2, 4];

    // 秒付き形式
    private readonly bool includesSeconds;

    // 一致候補ビットマスク
    private readonly ulong secondsMask;
    private readonly ulong minutesMask;
    private readonly uint hoursMask;
    private readonly uint daysOfMonthMask;
    private readonly ushort monthsMask;
    private readonly byte daysOfWeekMask;

    // 制約フラグ
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
                (uint)ParseFieldMask64(span[ranges[2]], DayOfMonthMin, DayOfMonthMax, "day-of-month"),
                (ushort)ParseFieldMask64(span[ranges[3]], MonthMin, MonthMax, "month"),
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
                (uint)ParseFieldMask64(span[ranges[3]], DayOfMonthMin, DayOfMonthMax, "day-of-month"),
                (ushort)ParseFieldMask64(span[ranges[4]], MonthMin, MonthMax, "month"),
                (byte)ParseFieldMask64(span[ranges[5]], 0, 6, "day-of-week"),
                GetFlags(span[ranges[3]], span[ranges[5]]));
        }

        throw new FormatException($"Cron expression must have 5 or 6 fields. expression=[{expression}]");
    }

    //--------------------------------------------------------------------------------
    // GetNextOccurrence
    //--------------------------------------------------------------------------------

    /// <summary>
    /// 指定日時より後で最初に一致する実行日時を取得します。
    /// </summary>
    /// <param name="from">基準となる日時です。</param>
    /// <returns>次回実行日時。見つからない場合は <see langword="null"/> です。</returns>
    public DateTimeOffset? GetNextOccurrence(DateTimeOffset from)
        => includesSeconds ? GetNextOccurrenceWithSeconds(from) : GetNextOccurrenceWithoutSeconds(from);

    private DateTimeOffset? GetNextOccurrenceWithoutSeconds(DateTimeOffset from)
    {
        // 秒を含まない 5 フィールド形式では、分単位で次回候補を探索する。
        // 入力の UTC オフセットはそのまま維持し、ローカルな各要素だけを進める。
        var offset = from.Offset;
        var year = from.Year;
        var month = from.Month;
        var day = from.Day;
        var hour = from.Hour;
        var minute = from.Minute;
        var second = 0;

        if (from.Second >= 59)
        {
            // 59 秒台をまたぐ場合は、次の分から探索を開始する。
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

        // 探索範囲を無制限にせず、将来 5 年以内で打ち切る。
        while (year < maxYear)
        {
            // 月が条件に一致しない場合は、次の一致月まで日付と時刻を初期化して進める。
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
                // 月末を超えた場合は翌月の先頭へ進める。
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
                // 日条件が一致しない場合は次の日を試す。
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
                // 当日に一致する時が無い場合は翌日に進める。
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
                // 時が進んだ場合は下位要素を最小値へ戻して再探索する。
                hour = nextHour;
                minute = 0;
                second = 0;
            }

            var nextMinute = FindNextBit(minutesMask, minute, 59);
            if (nextMinute < 0)
            {
                // 当該時刻内で一致する分が無ければ次の時へ進める。
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
                // 基準時刻ちょうどは「次回」ではないため、翌候補へ進める。
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
        // 秒を含む 6 フィールド形式では、基準時刻の次の秒から探索する。
        // 同一秒の再ヒットを避けるため、必ず +1 秒した状態から始める。
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
                // 条件に一致する月までスキップする。
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
                // 日条件は day-of-month と day-of-week の組み合わせ規則に従って判定する。
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
                // 時が変わったら分・秒は最初の候補からやり直す。
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
                // 秒が見つからない場合は次の分へ繰り上げる。
                var resetSecond = FindNextBit(secondsMask, 0, 59);
                if (resetSecond < 0)
                {
                    // 秒フィールドが空になることは通常無いが、防御的に null を返す。
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
                // 現在時刻そのものは返さず、以後の候補を探す。
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

    //--------------------------------------------------------------------------------
    // Helper
    //--------------------------------------------------------------------------------

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
        if (field.Length == 1 && field[0] == '*')
        {
            // `*` は許容範囲全体をそのまま有効化する。
            return BuildMask(min, max, 1);
        }

        if (field.Length > 2
            && field[0] == '*'
            && field[1] == '/'
            && field.IndexOf(',') < 0
            && field.IndexOf('-') < 0)
        {
            // `*/n` の最適化パス。単純な刻み指定は直接マスク化する。
            return BuildMask(min, max, ParsePositiveInt32(field[2..], name, field));
        }

        if (field.IndexOf(',') < 0 && field.IndexOf('-') < 0 && field.IndexOf('/') < 0)
        {
            // 最も単純な単一値指定は、個別パースして 1 ビットだけ立てる。
            if (!TryParseUInt32(field, out var parsedValue))
            {
                throw new FormatException($"Invalid value in {name} field: '{field}'.");
            }

            var value = (int)parsedValue;
            if (value < min || value > max)
            {
                throw new FormatException($"Value out of range [{min},{max}] in {name} field: '{field}'.");
            }

            return 1UL << value;
        }

        var result = 0UL;
        var tokenStart = 0;

        // 複合指定ではカンマ区切りの各トークンを順番にマスクへ折り畳む。
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
                // `1,,2` のような空トークンは不正として扱う。
                throw new FormatException($"Invalid {name} field: '{field}'.");
            }

            var slashIndex = term.IndexOf('/');
            var rangePart = slashIndex >= 0 ? term[..slashIndex] : term;
            var step = slashIndex >= 0 ? ParsePositiveInt32(term[(slashIndex + 1)..], name, term) : 1;

            int start;
            int end;

            if (IsWildcard(rangePart))
            {
                // `*/n` や `*` のような全域指定。
                start = min;
                end = max;
            }
            else
            {
                var dashIndex = rangePart.IndexOf('-');
                if (dashIndex > 0)
                {
                    // `a-b` 形式の範囲指定。
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
                    // 単一値または `a/n` 形式。`a/n` は a から最大値まで刻む。
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
                // 正規化後に範囲外となる指定はここで一括して拒否する。
                throw new FormatException($"Value out of range [{min},{max}] in {name} field: '{term}'.");
            }

            for (var value = start; value <= end; value += step)
            {
                // 範囲と刻みに従って対象ビットを立てる。
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
        for (var value = start; value <= end; value += step)
        {
            result |= 1UL << value;
        }

        return result;
    }

    private static int ParsePositiveInt32(ReadOnlySpan<char> value, string name, ReadOnlySpan<char> term)
    {
        // step は 1 以上の整数のみ許容する。
        if (!TryParseUInt32(value, out var parsedValue) || parsedValue == 0 || parsedValue > int.MaxValue)
        {
            throw new FormatException($"Invalid step in {name} field: '{term}'.");
        }

        return (int)parsedValue;
    }

    private static bool TryParseUInt32(ReadOnlySpan<char> value, out uint result)
    {
        // ReadOnlySpan から割り当てなしで非負整数をパースする。
        if (value.IsEmpty)
        {
            result = 0;
            return false;
        }

        var accumulator = 0u;
        foreach (var c in value)
        {
            // 数字以外の文字を含む場合は失敗とする。
            var digit = c - '0';
            if ((uint)digit > 9)
            {
                result = 0;
                return false;
            }

            var next = (accumulator * 10) + (uint)digit;
            if (next < accumulator)
            {
                // uint のオーバーフローを検出する。
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

    private static bool IsWildcard(ReadOnlySpan<char> value) => value.Length == 1 && value[0] == '*';

    //--------------------------------------------------------------------------------
    // Date helpers
    //--------------------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int DaysInMonth(int year, int month)
    {
        // 通常月はテーブル参照、2 月だけうるう年補正を加える。
        var days = Unsafe.Add(ref MemoryMarshal.GetReference(DaysInMonthTable), month);
        if (month == 2 && IsLeapYear(year))
        {
            days = 29;
        }

        return days;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsLeapYear(int year)
        // DateTime.IsLeapYear より軽量なビット演算ベースのうるう年判定。
        => (year & 3) == 0 && ((year % 25) != 0 || (year & 15) == 0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsDayMatch(int year, int month, int day)
    {
        // 日付条件なしなら、すべての日を一致とみなす。
        var localFlags = flags;
        if ((localFlags & (DayOfMonthRestrictedFlag | DayOfWeekRestrictedFlag)) == 0)
        {
            return true;
        }

        // 月内の日付一致と曜日一致をそれぞれ独立に評価する。
        var dayOfMonthMatch = (daysOfMonthMask & (1U << day)) != 0;
        var dayOfWeekMatch = (daysOfWeekMask & (1 << CalcDayOfWeek(year, month, day))) != 0;

        if ((localFlags & (DayOfMonthRestrictedFlag | DayOfWeekRestrictedFlag)) == (DayOfMonthRestrictedFlag | DayOfWeekRestrictedFlag))
        {
            // 両方に制約がある場合は cron の仕様に従って OR 条件で判定する。
            return dayOfMonthMatch || dayOfWeekMatch;
        }

        // 片方だけ制約がある場合は、その条件のみで判定する。
        return (localFlags & DayOfMonthRestrictedFlag) != 0 ? dayOfMonthMatch : dayOfWeekMatch;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CalcDayOfWeek(int year, int month, int day)
    {
        // 1 月と 2 月を前年の 13,14 月相当として扱う簡易曜日計算を行う。
        if (month < 3)
        {
            year--;
        }

        // 返値は 0=日曜 ～ 6=土曜 の想定。
        var monthOffset = Unsafe.Add(ref MemoryMarshal.GetReference(DayOfWeekTable), month - 1);
        return (year + (year / 4) - (year / 100) + (year / 400) + monthOffset + day) % 7;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool AdvanceToNextMonth(ref int year, ref int month, int maxYear)
    {
        // 同一年内で次に一致する月を優先して探す。
        var nextMonth = FindNextBit(monthsMask, month + 1, MonthMax);
        if (nextMonth >= 0)
        {
            month = nextMonth;
            return true;
        }

        // 同年に候補が無ければ翌年へ進み、最初の一致月を探す。
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
        // day を 1 日進めた後、月末超過なら年月を繰り上げる。
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

    //--------------------------------------------------------------------------------
    // Bit helpers
    //--------------------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FindNextBit(ulong mask, int startBit, int maxBit)
    {
        // startBit 未満をマスクしてから、最下位の 1 ビット位置を調べる。
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
        // 32 ビット版。hour/day-of-month の探索に使う。
        var filteredMask = mask & ~((1U << startBit) - 1U);
        if (filteredMask == 0)
        {
            return -1;
        }

        var position = BitOperations.TrailingZeroCount(filteredMask);
        return position <= maxBit ? position : -1;
    }
}
