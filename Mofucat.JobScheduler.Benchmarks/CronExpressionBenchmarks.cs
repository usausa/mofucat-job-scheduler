namespace Mofucat.JobScheduler.Benchmarks;

using BenchmarkDotNet.Attributes;

[MemoryDiagnoser]
#pragma warning disable CA1812
public class CronExpressionBenchmarks
{
    private readonly string expression = "*/10 * * * * *";

    [Benchmark]
    public Mofucat.JobScheduler.CronExpression ParseCustom() => Mofucat.JobScheduler.CronExpression.Parse(expression);
}
#pragma warning restore CA1812
