using System.CommandLine;
using SkillSync.Cli;
using SkillSync.Installation;
using SkillSync.Registry;
using SkillSync.Storage;

namespace SkillSync.Features;

public sealed class Backups(LocalFiles files, StateStore states, SkillInstaller installer)
{
    public SyncState Rollback()
    {
        using var runLock = files.Lock();
        installer.Recover();
        var current = states.Require();
        var previous = states.Read(files.At($".skillsync/transactions/{current.TransactionId}/before.json"))
            ?? throw new InvalidOperationException("No previous installation is available for rollback.");
        if (previous.Uninstalled) throw new InvalidOperationException("Previous installation was uninstalled.");
        var contents = new Dictionary<string, List<SkillFile>>(StringComparer.Ordinal);
        foreach (var skill in previous.Skills)
        {
            var backup = files.At($".skillsync/transactions/{current.TransactionId}/backup/{skill.Target}/{skill.Name}");
            var source = Directory.Exists(backup) ? backup : files.At($"{skill.Target}/{skill.Name}");
            if (!Directory.Exists(source) || files.HashDirectory(source) != skill.Hash)
                throw new InvalidDataException($"Missing or corrupted backup for {skill.Name}.");
            if (contents.ContainsKey(skill.Name)) continue;
            var entries = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
            contents.Add(skill.Name, entries.Select(path => new SkillFile(
                Path.GetRelativePath(source, path).Replace('\\', '/'), File.ReadAllBytes(path),
                !OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) & UnixFileMode.UserExecute) != 0)).ToList());
        }
        var config = previous.Config with { Revision = previous.Commit };
        RegistryConfig.Validate(config);
        return installer.Apply(config, current, new Snapshot(previous.Commit, contents));
    }

    private (string Id, SyncState Previous)[] History()
    {
        var root = files.At(".skillsync/transactions");
        if (!Directory.Exists(root)) return [];
        return Directory.EnumerateDirectories(root).Select(path =>
        {
            files.Guard(path);
            var id = Path.GetFileName(path); LocalFiles.ValidateId(id);
            return (Id: id, Previous: states.Read(Path.Combine(path, "before.json")));
        }).Where(item => item.Previous is not null).Select(item => (item.Id, item.Previous!))
            .OrderByDescending(item => item.Item2.SyncedAt).ToArray();
    }

    public Command RollbackCommand(CommandOutput output)
    {
        var command = new Command("rollback", "Restore the previous installation and pin its revision.");
        command.SetAction(async (_, token) => await output.ExecuteAsync(() =>
        {
            output.Print(Rollback()); return Task.CompletedTask;
        }, token));
        return command;
    }

    public Command CreateCommand(CommandOutput output)
    {
        var list = new Command("list", "List retained installation backups.");
        list.SetAction(async (_, token) => await output.ExecuteAsync(() =>
        {
            using var runLock = files.Lock(); installer.Recover();
            foreach (var (id, previous) in History()) output.Writer.WriteLine($"{id}: {previous.Commit}, {previous.SyncedAt:O}");
            return Task.CompletedTask;
        }, token));
        var keep = new Option<int>("--keep") { DefaultValueFactory = _ => 10 };
        var yes = new Option<bool>("--yes");
        var prune = new Command("prune", "Delete old backups, preserving current rollback and recovered files.") { keep, yes };
        prune.SetAction(async (parse, token) => await output.ExecuteAsync(() =>
        {
            var count = parse.GetValue(keep);
            if (!parse.GetValue(yes) || count < 0) throw new ArgumentException("Pruning requires --yes and --keep >= 0.");
            using var runLock = files.Lock(); installer.Recover();
            var current = states.Read()?.TransactionId;
            var candidates = History().Where(item => item.Id != current).Skip(count)
                .Where(item => !Directory.Exists(files.At($".skillsync/transactions/{item.Id}/recovered"))).ToArray();
            foreach (var (id, _) in candidates)
            {
                var root = files.At($".skillsync/transactions/{id}");
                var pending = new Stack<string>(); pending.Push(root);
                while (pending.TryPop(out var dir))
                    foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
                    {
                        files.Guard(entry);
                        if (Directory.Exists(entry)) pending.Push(entry);
                    }
            }
            foreach (var (id, _) in candidates) Directory.Delete(files.At($".skillsync/transactions/{id}"), recursive: true);
            output.Writer.WriteLine($"Deleted {candidates.Length} old backup transactions.");
            return Task.CompletedTask;
        }, token));
        return new Command("backups", "Inspect and prune retained backups.") { list, prune };
    }
}
