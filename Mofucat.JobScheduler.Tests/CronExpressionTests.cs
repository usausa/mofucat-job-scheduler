namespace Mofucat.JobScheduler.Tests;

public sealed class CronExpressionTest
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
}
