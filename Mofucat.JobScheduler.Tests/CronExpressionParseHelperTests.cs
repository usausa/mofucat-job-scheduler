namespace Mofucat.JobScheduler.Tests;

#pragma warning disable CA1812
public sealed class CronExpressionParseHelperTests
{
    [Theory]
    [InlineData("*/5 * * * * *")]
    [InlineData("0 0 1 1 *")]
    public void ParseWhenExpressionIsValidThenStoresOriginalExpression(string value)
    {
        var expression = CronExpression.Parse(value);

        Assert.Equal(value, expression.Expression);
    }
}
#pragma warning restore CA1812
