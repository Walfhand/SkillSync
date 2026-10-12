using System.CommandLine;
using SkillSync.Cli;
using SkillSync.Scheduling;
using SkillSync.Storage;

namespace SkillSync.Features;

public sealed class AutoSync(LocalFiles files, StateStore states, AutoSyncScheduler scheduler)
{
    public Command CreateCommand(CommandOutput output)
    {
        var interval = new Option<int>("--interval") { DefaultValueFactory = _ => 15 };
        var enable = new Command("enable", "Synchronize at login and periodically using the OS scheduler.") { interval };
        enable.SetAction(async (parse, token) => await output.ExecuteAsync(async () =>
        {
            var minutes = parse.GetValue(interval);
            if (minutes is < 1 or > 1440) throw new ArgumentException("Interval must be between 1 and 1440 minutes.");
            states.Require();
            var executable = Environment.ProcessPath!;
            if (Path.GetFileNameWithoutExtension(executable) != "skillsync")
                throw new InvalidOperationException("Install the standalone CLI before enabling auto-sync.");
            using var runLock = files.Lock();
            await scheduler.EnableAsync(executable, minutes, token);
            output.Writer.WriteLine($"Auto-sync enabled every {minutes} minutes.");
        }, token));
        var disable = new Command("disable", "Remove the scheduled synchronization job.");
        disable.SetAction(async (_, token) => await output.ExecuteAsync(async () =>
        {
            using var runLock = files.Lock(); await scheduler.DisableAsync(token);
            output.Writer.WriteLine("Auto-sync disabled.");
        }, token));
        var status = new Command("status", "Inspect the scheduler and the last synchronization attempt.");
        status.SetAction(async (_, token) => await output.ExecuteAsync(async () =>
        {
            output.Writer.WriteLine(await scheduler.StatusAsync(token));
            var attempt = files.ReadJson<SyncAttempt>(files.At(".skillsync/last-sync.json"));
            if (attempt is not null) output.Writer.WriteLine($"{attempt.Time:O}: {(attempt.Success ? "synced" : attempt.Error)}");
        }, token));
        return new Command("autosync", "Manage automatic synchronization.") { enable, disable, status };
    }
}
