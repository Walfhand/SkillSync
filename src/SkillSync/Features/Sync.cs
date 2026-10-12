using System.CommandLine;
using SkillSync.Cli;
using SkillSync.Installation;
using SkillSync.Registry;
using SkillSync.Storage;

namespace SkillSync.Features;

public sealed class Sync(LocalFiles files, StateStore states, SkillInstaller installer)
{
    public async Task<SyncState> ExecuteAsync(CancellationToken cancellation = default)
    {
        using var runLock = files.Lock();
        installer.Recover();
        var previous = states.Require();
        var snapshot = await GitRegistry.ReadAsync(previous.Config, files, cancellation);
        return installer.Apply(previous.Config, previous, SkillSelection.Apply(snapshot, previous.Config));
    }

    public Command CreateCommand(CommandOutput output)
    {
        var sync = new Command("sync", "Fetch and apply the latest registry revision.");
        var scheduled = new Option<bool>("--scheduled") { Hidden = true };
        sync.Options.Add(scheduled);
        sync.SetAction(async (parse, token) =>
        {
            using var errors = new StringWriter();
            var console = parse.GetValue(scheduled) ? new CommandOutput(output.Writer, errors) : output;
            var exit = await console.ExecuteAsync(async () => console.Print(await ExecuteAsync(token)), token);
            if (parse.GetValue(scheduled))
                files.WriteJson(files.At(".skillsync/last-sync.json"), new Scheduling.SyncAttempt(DateTimeOffset.UtcNow, exit == 0, exit == 0 ? null : errors.ToString().Trim()));
            return exit;
        });
        return sync;
    }
}
