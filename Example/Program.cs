using Example.Jobs;
using Example.Services;

using Mofucat.JobScheduler.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddJobScheduler(static options =>
{
    options.UseJob<SampleJob>("*/1 * * * *", name: "sample");
});

builder.Services.AddSingleton<FeatureService>();

builder.Build().Run();
