using System.CommandLine;
using SkillSync.Cli;

namespace SkillSync.Features;

public sealed class Watch(Sync sync, Status status)
{
    public Command CreateCommand(CommandOutput output)
    {
        var interval = new Option<int>("--interval") { DefaultValueFactory = _ => 15, Description = "Minutes between synchronizations (1–1440)." };
        var watch = new Command("watch", "Synchronize periodically while this process is running. Stop with Ctrl+C.") { interval };
        watch.SetAction(async (parse, token) => await output.ExecuteAsync(async () =>
        {
            var minutes = parse.GetValue(interval);
            if (minutes is < 1 or > 1440) throw new ArgumentException("Interval must be between 1 and 1440 minutes.");
            status.Execute();
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(minutes));
            do
            {
                await output.ExecuteAsync(async () => output.Print(await sync.ExecuteAsync(token)), token);
            } while (await timer.WaitForNextTickAsync(token));
        }, token));
        return watch;
    }
}
