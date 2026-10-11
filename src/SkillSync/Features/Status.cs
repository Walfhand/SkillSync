using System.CommandLine;
using SkillSync.Cli;
using SkillSync.Installation;
using SkillSync.Storage;

namespace SkillSync.Features;

public sealed class Status(LocalFiles files, StateStore states, SkillInstaller installer)
{
    public (SyncState State, string[] Lines) Execute()
    {
        using var runLock = files.Lock();
        installer.Recover();
        var state = states.Require();
        var lines = state.Skills.Select(skill =>
        {
            var path = files.At(skill.Target + "/" + skill.Name);
            var status = !Directory.Exists(path) ? "missing" : files.HashDirectory(path) == skill.Hash ? "synced" : "modified";
            return $"{skill.Target}/{skill.Name}: {status}";
        }).ToArray();
        return (state, lines);
    }

    public Command CreateCommand(CommandOutput output)
    {
        var command = new Command("status", "Show installed versions and detect local changes.");
        command.SetAction(async (_, token) => await output.ExecuteAsync(() =>
        {
            var (state, lines) = Execute();
            output.Print(state);
            foreach (var line in lines) output.Writer.WriteLine(line);
            return Task.CompletedTask;
        }, token));
        return command;
    }
}
