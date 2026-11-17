namespace Mofucat.JobScheduler.Tests;

using Mofucat.JobScheduler.Tests.Mock;

public sealed class CronExpressionTests
{
    [Theory]
    [InlineData("*/5 * * * * *")]
    [InlineData("0 0 1 1 *")]
    public void ParseWhenExpressionIsValidThenStoresOriginalExpression(string value)
    {
        var expression = CronExpression.Parse(value);

        Assert.Equal(value, expression.Expression);
    }

    [Fact]
    public void ParseWhenExpressionHasInvalidFieldCountThenThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => CronExpression.Parse("* * * *"));
    }

    [Fact]
    public void ParseWhenExpressionHasOutOfRangeValueThenThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => CronExpression.Parse("60 * * * * *"));
    }

    [Fact]
    public void GetNextOccurrenceWhenUsingFiveFieldsThenReturnsNextMinute()
    {
        var expression = CronExpression.Parse("*/15 * * * *");
        var from = new DateTimeOffset(2026, 4, 26, 10, 7, 30, TimeSpan.Zero);

        var next = expression.GetNextOccurrence(from);

        Assert.Equal(new DateTimeOffset(2026, 4, 26, 10, 15, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void GetNextOccurrenceWhenUsingFiveFieldsAndCurrentTimeHasSubMinutePartThenSkipsPastMinute()
    {
        var expression = CronExpression.Parse("*/1 * * * *");
        var from = new DateTimeOffset(2026, 4, 26, 10, 7, 30, TimeSpan.Zero);

        var next = expression.GetNextOccurrence(from);

        Assert.Equal(new DateTimeOffset(2026, 4, 26, 10, 8, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void GetNextOccurrenceWhenUsingSixFieldsThenReturnsNextSecond()
    {
        var expression = CronExpression.Parse("*/10 * * * * *");
        var from = new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero);

        var next = expression.GetNextOccurrence(from);

        Assert.Equal(new DateTimeOffset(2026, 4, 26, 10, 7, 10, TimeSpan.Zero), next);
    }

    [Fact]
    public void GetNextOccurrenceWhenUsingSixFieldsAndTimeMatchesExactlyThenReturnsStrictlyLaterSecond()
    {
        var expression = CronExpression.Parse("*/1 * * * * *");
        var from = new DateTimeOffset(2026, 4, 26, 2, 43, 0, TimeSpan.Zero);

        var next = expression.GetNextOccurrence(from);

        Assert.Equal(new DateTimeOffset(2026, 4, 26, 2, 43, 1, TimeSpan.Zero), next);
    }

    [Fact]
    public void GetNextOccurrenceWhenUsingSixFieldsAndTimeHasFractionThenReturnsStrictlyLaterSecond()
    {
        var expression = CronExpression.Parse("*/1 * * * * *");
        var from = new DateTimeOffset(2026, 4, 26, 2, 43, 0, 1, TimeSpan.Zero);

        var next = expression.GetNextOccurrence(from);

        Assert.Equal(new DateTimeOffset(2026, 4, 26, 2, 43, 1, TimeSpan.Zero), next);
    }

    [Fact]
    public void GetNextOccurrenceWhenDayOfMonthAndDayOfWeekRestrictedThenMatchesEither()
    {
        var expression = CronExpression.Parse("0 12 15 * 1");
        var from = new DateTimeOffset(2026, 4, 13, 12, 0, 0, TimeSpan.Zero);

        var next = expression.GetNextOccurrence(from);

        Assert.Equal(new DateTimeOffset(2026, 4, 15, 12, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void GetNextOccurrenceWhenUsingFiveFieldsAtEndOfMinuteThenAdvancesToNextMinute()
    {
        var expression = CronExpression.Parse("*/15 * * * *");
        var from = new DateTimeOffset(2026, 4, 26, 10, 14, 59, TimeSpan.Zero);

        var next = expression.GetNextOccurrence(from);

        Assert.Equal(new DateTimeOffset(2026, 4, 26, 10, 15, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void GetNextOccurrenceWhenUsingFiveFieldsThenPreservesOffset()
    {
        var expression = CronExpression.Parse("0 9 * * *");
        var from = new DateTimeOffset(2026, 4, 26, 8, 10, 0, TimeSpan.FromHours(9));

        var next = expression.GetNextOccurrence(from);

        Assert.Equal(new DateTimeOffset(2026, 4, 26, 9, 0, 0, TimeSpan.FromHours(9)), next);
    }

    [Fact]
    public void GetNextOccurrenceWhenUsingFiveFieldsAndCurrentTimeMatchesThenReturnsNextMatchingMinute()
    {
        var expression = CronExpression.Parse("*/1 * * * *");
        var from = new DateTimeOffset(2026, 4, 26, 10, 7, 0, TimeSpan.Zero);

        var next = expression.GetNextOccurrence(from);

        Assert.Equal(new DateTimeOffset(2026, 4, 26, 10, 8, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void GetNextOccurrenceWhenUsingFiveFieldsAndTimeMatchesExactlyThenReturnsStrictlyLaterMinute()
    {
        var expression = CronExpression.Parse("*/1 * * * *");
        var from = new DateTimeOffset(2026, 4, 26, 2, 43, 0, TimeSpan.Zero);

        var next = expression.GetNextOccurrence(from);

        Assert.Equal(new DateTimeOffset(2026, 4, 26, 2, 44, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void GetNextOccurrenceWhenUsingFiveFieldsAndTimeHasFractionThenReturnsStrictlyLaterMinute()
    {
        var expression = CronExpression.Parse("*/1 * * * *");
        var from = new DateTimeOffset(2026, 4, 26, 2, 43, 0, 1, TimeSpan.Zero);

        var next = expression.GetNextOccurrence(from);

        Assert.Equal(new DateTimeOffset(2026, 4, 26, 2, 44, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void GetNextOccurrenceWhenUsingFiveFieldsAndCurrentTimeMatchesAtHourBoundaryThenReturnsNextMatchingHour()
    {
        var expression = CronExpression.Parse("0 */1 * * *");
        var from = new DateTimeOffset(2026, 4, 26, 10, 0, 0, TimeSpan.Zero);

        var next = expression.GetNextOccurrence(from);

        Assert.Equal(new DateTimeOffset(2026, 4, 26, 11, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void GetNextOccurrenceWhenUsingFiveFieldsAndCurrentTimeMatchesAtDayBoundaryThenReturnsNextMatchingDay()
    {
        var expression = CronExpression.Parse("0 0 * * *");
        var from = new DateTimeOffset(2026, 4, 26, 0, 0, 0, TimeSpan.Zero);

        var next = expression.GetNextOccurrence(from);

        Assert.Equal(new DateTimeOffset(2026, 4, 27, 0, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void GetNextOccurrenceWhenUsingFiveFieldsAndCurrentTimeMatchesAtMonthBoundaryThenReturnsNextMatchingMonth()
    {
        var expression = CronExpression.Parse("0 0 1 * *");
        var from = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);

        var next = expression.GetNextOccurrence(from);

        Assert.Equal(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void GetNextOccurrenceWhenUsingFiveFieldsAndCurrentTimeMatchesAtYearBoundaryThenReturnsNextMatchingYear()
    {
        var expression = CronExpression.Parse("0 0 1 1 *");
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var next = expression.GetNextOccurrence(from);

        Assert.Equal(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void GetNextOccurrenceWhenUsingSixFieldsAcrossMinuteBoundaryThenReturnsNextMatchingSecond()
    {
        var expression = CronExpression.Parse("*/10 * * * * *");
        var from = new DateTimeOffset(2026, 4, 26, 10, 7, 59, TimeSpan.Zero);

        var next = expression.GetNextOccurrence(from);

        Assert.Equal(new DateTimeOffset(2026, 4, 26, 10, 8, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void GetNextOccurrenceWhenUsingSixFieldsAndCurrentTimeMatchesThenReturnsNextMatchingSecond()
    {
        var expression = CronExpression.Parse("*/1 * * * * *");
        var from = new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero);

        var next = expression.GetNextOccurrence(from);

        Assert.Equal(new DateTimeOffset(2026, 4, 26, 10, 7, 6, TimeSpan.Zero), next);
    }

    [Fact]
    public void GetNextOccurrenceWhenUsingSixFieldsAndCurrentTimeMatchesAtMinuteBoundaryThenReturnsNextMatchingMinute()
    {
        var expression = CronExpression.Parse("0 */1 * * * *");
        var from = new DateTimeOffset(2026, 4, 26, 10, 7, 0, TimeSpan.Zero);

        var next = expression.GetNextOccurrence(from);

        Assert.Equal(new DateTimeOffset(2026, 4, 26, 10, 8, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void GetNextOccurrenceWhenUsingSixFieldsAndCurrentTimeMatchesAtHourBoundaryThenReturnsNextMatchingHour()
    {
        var expression = CronExpression.Parse("0 0 */1 * * *");
        var from = new DateTimeOffset(2026, 4, 26, 10, 0, 0, TimeSpan.Zero);

        var next = expression.GetNextOccurrence(from);

        Assert.Equal(new DateTimeOffset(2026, 4, 26, 11, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void GetNextOccurrenceWhenUsingSixFieldsAndCurrentTimeMatchesAtDayBoundaryThenReturnsNextMatchingDay()
    {
        var expression = CronExpression.Parse("0 0 0 * * *");
        var from = new DateTimeOffset(2026, 4, 26, 0, 0, 0, TimeSpan.Zero);

        var next = expression.GetNextOccurrence(from);

        Assert.Equal(new DateTimeOffset(2026, 4, 27, 0, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void GetNextOccurrenceWhenUsingSixFieldsAndCurrentTimeMatchesAtMonthBoundaryThenReturnsNextMatchingMonth()
    {
        var expression = CronExpression.Parse("0 0 0 1 * *");
        var from = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);

        var next = expression.GetNextOccurrence(from);

        Assert.Equal(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void GetNextOccurrenceWhenUsingSixFieldsAndCurrentTimeMatchesAtYearBoundaryThenReturnsNextMatchingYear()
    {
        var expression = CronExpression.Parse("0 0 0 1 1 *");
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var next = expression.GetNextOccurrence(from);

        Assert.Equal(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void ParseWhenExpressionContainsExtraSpacesThenParsesSuccessfully()
    {
        var expression = CronExpression.Parse("  */10   *   *   *   *   * ");
        var from = new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero);

        var next = expression.GetNextOccurrence(from);

        Assert.Equal(new DateTimeOffset(2026, 4, 26, 10, 7, 10, TimeSpan.Zero), next);
    }

    [Fact]
    public void ParseWhenSecondStepExceedsRangeThenMatchesOnlyStartWithoutHanging()
    {
        var expression = CronExpression.Parse("58/2147483647 * * * * *");
        var from = new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero);

        var next = expression.GetNextOccurrence(from);

        Assert.Equal(new DateTimeOffset(2026, 4, 26, 10, 7, 58, TimeSpan.Zero), next);
    }

    [Fact]
    public void ParseWhenSecondWildcardStepExceedsRangeThenMatchesOnlyMinimumWithoutHanging()
    {
        var expression = CronExpression.Parse("*/2147483647 * * * * *");
        var from = new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero);

        var next = expression.GetNextOccurrence(from);

        Assert.Equal(new DateTimeOffset(2026, 4, 26, 10, 8, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void ParseWhenMinuteStepExceedsRangeThenMatchesOnlyStartWithoutHanging()
    {
        var expression = CronExpression.Parse("59/2147483647 * * * *");
        var from = new DateTimeOffset(2026, 4, 26, 10, 7, 30, TimeSpan.Zero);

        var next = expression.GetNextOccurrence(from);

        Assert.Equal(new DateTimeOffset(2026, 4, 26, 10, 59, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void ParseWhenExpressionUsesTabAndNewlineSeparatorsThenParsesSuccessfully()
    {
        var expression = CronExpression.Parse("*/10\t*\t*\r\n* *\t*");
        var from = new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero);

        var next = expression.GetNextOccurrence(from);

        Assert.Equal(new DateTimeOffset(2026, 4, 26, 10, 7, 10, TimeSpan.Zero), next);
    }

    [Fact]
    public void GetNextOccurrenceWhenDayOfWeekIsSevenThenTreatedAsSunday()
    {
        var expressionSeven = CronExpression.Parse("0 12 * * 7");
        var expressionZero = CronExpression.Parse("0 12 * * 0");
        var from = new DateTimeOffset(2026, 4, 13, 12, 0, 0, TimeSpan.Zero);

        var nextSeven = expressionSeven.GetNextOccurrence(from);
        var nextZero = expressionZero.GetNextOccurrence(from);

        Assert.Equal(nextZero, nextSeven);
        Assert.Equal(new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero), nextSeven);
    }

    [Fact]
    public void GetNextOccurrenceWithTimeZoneThenReturnsLocalTimeOfTimeZone()
    {
        AssertOccurrences(
            TestTimeZones.Tokyo,
            "0 3 * * *",
            "2026-11-17T17:00:00+00:00",
            "2026-11-18T03:00:00+09:00",
            "2026-11-19T03:00:00+09:00");
    }

    [Fact]
    public void GetNextOccurrenceWithTimeZoneWhenDaylightSavingTimeStartsThenKeepsLocalTime()
    {
        AssertOccurrences(
            TestTimeZones.Eastern,
            "0 3 * * *",
            "2026-03-07T03:00:00-05:00",
            "2026-03-08T03:00:00-04:00",
            "2026-03-09T03:00:00-04:00");
    }

    [Fact]
    public void GetNextOccurrenceWithTimeZoneWhenDaylightSavingTimeEndsThenKeepsLocalTime()
    {
        AssertOccurrences(
            TestTimeZones.Eastern,
            "0 3 * * *",
            "2026-10-31T03:00:00-04:00",
            "2026-11-01T03:00:00-05:00",
            "2026-11-02T03:00:00-05:00");
    }

    [Fact]
    public void GetNextOccurrenceWithTimeZoneWhenTimeIsSkippedThenOccursWhenClockJumps()
    {
        AssertOccurrences(
            TestTimeZones.Eastern,
            "30 2 * * *",
            "2026-03-07T03:00:00-05:00",
            "2026-03-08T03:00:00-04:00",
            "2026-03-09T02:30:00-04:00");
    }

    [Fact]
    public void GetNextOccurrenceWithTimeZoneWhenSkippedTimeHasSeveralOccurrencesThenOccursOnce()
    {
        AssertOccurrences(
            TestTimeZones.Eastern,
            "*/15 2 * * *",
            "2026-03-08T01:50:00-05:00",
            "2026-03-08T03:00:00-04:00",
            "2026-03-09T02:00:00-04:00");
    }

    [Theory]
    [InlineData("30 1 * * *")]
    [InlineData("0 30 1 * * *")]
    public void GetNextOccurrenceWithTimeZoneWhenFixedTimeIsRepeatedThenOccursOnlyInFirstPass(string expression)
    {
        AssertOccurrences(
            TestTimeZones.Eastern,
            expression,
            "2026-11-01T00:00:00-04:00",
            "2026-11-01T01:30:00-04:00",
            "2026-11-02T01:30:00-05:00");
    }

    [Fact]
    public void GetNextOccurrenceWithTimeZoneWhenListTimeIsRepeatedThenOccursOnlyInFirstPass()
    {
        AssertOccurrences(
            TestTimeZones.Eastern,
            "0,30 1 * * *",
            "2026-11-01T00:00:00-04:00",
            "2026-11-01T01:00:00-04:00",
            "2026-11-01T01:30:00-04:00",
            "2026-11-02T01:00:00-05:00");
    }

    [Fact]
    public void GetNextOccurrenceWithTimeZoneWhenMinuteStepIsRepeatedThenOccursInBothPasses()
    {
        AssertOccurrences(
            TestTimeZones.Eastern,
            "*/30 * * * *",
            "2026-11-01T00:45:00-04:00",
            "2026-11-01T01:00:00-04:00",
            "2026-11-01T01:30:00-04:00",
            "2026-11-01T01:00:00-05:00",
            "2026-11-01T01:30:00-05:00",
            "2026-11-01T02:00:00-05:00");
    }

    [Fact]
    public void GetNextOccurrenceWithTimeZoneWhenHourRangeIsRepeatedThenOccursInBothPasses()
    {
        AssertOccurrences(
            TestTimeZones.Eastern,
            "0 1-2 * * *",
            "2026-11-01T00:30:00-04:00",
            "2026-11-01T01:00:00-04:00",
            "2026-11-01T01:00:00-05:00",
            "2026-11-01T02:00:00-05:00");
    }

    [Fact]
    public void GetNextOccurrenceWithTimeZoneWhenSecondStepIsRepeatedThenOccursInBothPasses()
    {
        AssertOccurrences(
            TestTimeZones.Eastern,
            "*/30 30 1 * * *",
            "2026-11-01T01:29:59-04:00",
            "2026-11-01T01:30:00-04:00",
            "2026-11-01T01:30:30-04:00",
            "2026-11-01T01:30:00-05:00",
            "2026-11-01T01:30:30-05:00",
            "2026-11-02T01:30:00-05:00");
    }

    [Theory]
    [InlineData("30 1 * * *", "2026-11-02T01:30:00-05:00")]
    [InlineData("*/30 * * * *", "2026-11-01T01:30:00-05:00")]
    public void GetNextOccurrenceWithTimeZoneWhenStartingInSecondPassThenDoesNotReturnToFirstPass(string expression, string expected)
    {
        AssertOccurrences(TestTimeZones.Eastern, expression, "2026-11-01T01:15:00-05:00", expected);
    }

    [Fact]
    public void GetNextOccurrenceWithTimeZoneWhenTimeZoneIsNullThenThrowsArgumentNullException()
    {
        var expression = CronExpression.Parse("0 3 * * *");

        Assert.Throws<ArgumentNullException>(() => expression.GetNextOccurrence(DateTimeOffset.UnixEpoch, null!));
    }

    private static void AssertOccurrences(TimeZoneInfo timeZone, string expression, string from, params string[] expected)
    {
        var cron = CronExpression.Parse(expression);
        var current = DateTimeOffset.Parse(from, CultureInfo.InvariantCulture);
        var actual = new List<string>();
        for (var i = 0; i < expected.Length; i++)
        {
            var next = cron.GetNextOccurrence(current, timeZone);
            Assert.NotNull(next);
            current = next.Value;
            actual.Add(current.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture));
        }

        Assert.Equal(expected, actual);
    }
}
