using System.CommandLine;
using SkillSync.Cli;
using SkillSync.Installation;
using SkillSync.Registry;
using SkillSync.Storage;

namespace SkillSync.Features;

public sealed class Uninstall(LocalFiles files, StateStore states, SkillInstaller installer)
{
    public void Execute()
    {
        using var runLock = files.Lock();
        installer.Recover();
        var previous = states.Require();
        installer.Apply(previous.Config, previous, new Snapshot(previous.Commit, []), uninstall: true);
    }

    public Command CreateCommand(CommandOutput output)
    {
        var command = new Command("uninstall", "Remove unchanged managed skills; preserve personal files and backups.");
        command.SetAction(async (_, token) => await output.ExecuteAsync(() =>
        {
            Execute();
            output.Writer.WriteLine("Managed skills removed. Backups remain in ~/.skillsync/transactions/.");
            return Task.CompletedTask;
        }, token));
        return command;
    }
}
