using Example.Jobs;
using Example.Services;

using Mofucat.JobScheduler.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddJobSchedulerService(static options =>
{
    options.UseScopedJob<SampleJob>("*/1 * * * *", name: "sample");
});

builder.Services.AddSingleton<FeatureService>();

builder.Build().Run();
