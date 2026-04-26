using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Mofucat.JobScheduler.DependencyInjection;
using Mofucat.JobScheduler.Sample.Example.Jobs;
using Mofucat.JobScheduler.Sample.Example.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddJobScheduler(static options =>
{
    options.UseJob<SampleJob>("*/1 * * * *", name: "sample");
});

builder.Services.AddSingleton<FeatureService>();

builder.Build().Run();
