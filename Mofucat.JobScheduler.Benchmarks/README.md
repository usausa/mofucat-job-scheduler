# Benchmarks

Run:

```powershell
dotnet run -c Release --project Mofucat.JobScheduler.Benchmarks
```

This compares:
- `Mofucat.JobScheduler.CronExpression`
- `Cronos.CronExpression`

Benchmarks included:
- parse cost
- next occurrence cost
