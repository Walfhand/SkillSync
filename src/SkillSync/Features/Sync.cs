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
        return installer.Apply(previous.Config, previous, snapshot);
    }

    public Command CreateCommand(CommandOutput output)
    {
        var sync = new Command("sync", "Fetch and apply the latest registry revision.");
        sync.SetAction(async (_, token) => await output.ExecuteAsync(async () => output.Print(await ExecuteAsync(token)), token));
        return sync;
    }
}
