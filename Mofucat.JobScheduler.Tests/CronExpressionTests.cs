namespace Mofucat.JobScheduler.Tests;

public sealed class CronExpressionTests
{
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
    public void GetNextOccurrenceWhenUsingSixFieldsThenReturnsNextSecond()
    {
        var expression = CronExpression.Parse("*/10 * * * * *");
        var from = new DateTimeOffset(2026, 4, 26, 10, 7, 5, TimeSpan.Zero);

        var next = expression.GetNextOccurrence(from);

        Assert.Equal(new DateTimeOffset(2026, 4, 26, 10, 7, 10, TimeSpan.Zero), next);
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
    public void GetNextOccurrenceWhenUsingSixFieldsAcrossMinuteBoundaryThenReturnsNextMatchingSecond()
    {
        var expression = CronExpression.Parse("*/10 * * * * *");
        var from = new DateTimeOffset(2026, 4, 26, 10, 7, 59, TimeSpan.Zero);

        var next = expression.GetNextOccurrence(from);

        Assert.Equal(new DateTimeOffset(2026, 4, 26, 10, 8, 0, TimeSpan.Zero), next);
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
