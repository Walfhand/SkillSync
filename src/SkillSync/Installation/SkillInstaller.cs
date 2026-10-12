using SkillSync.Agents;
using SkillSync.Registry;
using SkillSync.Storage;

namespace SkillSync.Installation;

public sealed class SkillInstaller(LocalFiles files, StateStore states)
{
    public SyncState Apply(RegistryConfig config, SyncState? previous, Snapshot snapshot, bool uninstall = false)
    {
        var id = Guid.NewGuid().ToString("N");
        try
        {
            var changes = new List<Change>();
            var installed = new List<InstalledSkill>();
            var old = (previous?.Skills ?? []).ToDictionary(s => s.Target + "/" + s.Name, StringComparer.Ordinal);
            var desired = new HashSet<string>(StringComparer.Ordinal);
            foreach (var target in config.Agents.Select(AgentTargets.Target).Distinct(StringComparer.Ordinal))
                foreach (var (name, contents) in snapshot.Skills.OrderBy(s => s.Key, StringComparer.Ordinal))
                {
                    AgentTargets.ValidateTarget(target);
                    SkillValidator.ValidateName(name);
                    var key = target + "/" + name;
                    desired.Add(key);
                    var stage = TransactionPath(id, "stage", target, name);
                    Directory.CreateDirectory(stage);
                    foreach (var content in contents)
                    {
                        LocalFiles.ValidateRelative(content.Path);
                        var path = Path.Combine(stage, content.Path.Replace('/', Path.DirectorySeparatorChar));
                        files.Guard(path);
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        File.WriteAllBytes(path, content.Content);
                        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, content.Executable
                            ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute
                            : UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
                    }
                    var hash = files.HashDirectory(stage);
                    installed.Add(new InstalledSkill(target, name, hash));
                    var destination = files.At(key);
                    var exists = Directory.Exists(destination);
                    if (File.Exists(destination) || (exists && !old.ContainsKey(key)))
                        throw new IOException($"Personal skill already exists at {key}. Rename it before synchronizing.");
                    if (exists && files.HashDirectory(destination) != old[key].Hash)
                        throw new IOException($"Local edits found at {key}. Commit or move them before synchronizing; no managed skill was changed.");
                    if (!exists || old[key].Hash != hash) changes.Add(new Change(target, name, exists, Install: true));
                }
            foreach (var (key, skill) in old)
            {
                if (desired.Contains(key)) continue;
                var path = files.At(key);
                if (File.Exists(path) || (Directory.Exists(path) && files.HashDirectory(path) != skill.Hash))
                    throw new IOException($"Local edits found at {key}. Commit or move them before removing this skill.");
                if (Directory.Exists(path)) changes.Add(new Change(skill.Target, skill.Name, HadOriginal: true, Install: false));
            }
            if (changes.Count == 0 && previous is not null)
            {
                var unchanged = previous with { Config = config, Commit = snapshot.Commit, SyncedAt = DateTimeOffset.UtcNow, Uninstalled = uninstall };
                states.Write(unchanged);
                return unchanged;
            }
            if (previous is not null) files.WriteJson(files.At($".skillsync/transactions/{id}/before.json"), previous);
            var next = new SyncState(id, config, snapshot.Commit, DateTimeOffset.UtcNow, installed.ToArray(), uninstall);
            files.WriteJson(files.JournalPath, new Journal(id, changes.ToArray()));
            try
            {
                foreach (var change in changes)
                {
                    var target = files.At(change.Target + "/" + change.Name);
                    if (change.HadOriginal)
                    {
                        var backup = TransactionPath(id, "backup", change.Target, change.Name);
                        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                        Directory.Move(target, backup);
                    }
                    if (change.Install)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        Directory.Move(TransactionPath(id, "stage", change.Target, change.Name), target);
                    }
                }
                states.Write(next);
                File.Delete(files.JournalPath);
            }
            catch
            {
                Recover();
                throw;
            }
            return next;
        }
        finally
        {
            // An unfinished recovery still needs its staging directories to identify completed moves.
            if (!File.Exists(files.JournalPath))
            {
                var unusedStages = files.At($".skillsync/transactions/{id}/stage");
                if (Directory.Exists(unusedStages)) Directory.Delete(unusedStages, recursive: true);
            }
        }
    }

    private string TransactionPath(string id, string area, string target, string name)
        => files.At($".skillsync/transactions/{id}/{area}/{target}/{name}");

    public void Recover()
    {
        var journal = files.ReadJson<Journal>(files.JournalPath);
        if (journal is null) return;
        LocalFiles.ValidateId(journal.Id);
        if (journal.Changes is null) throw new InvalidDataException("Invalid recovery journal.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var change in journal.Changes)
        {
            if (change is null) throw new InvalidDataException("Invalid recovery entry.");
            AgentTargets.ValidateTarget(change.Target);
            SkillValidator.ValidateName(change.Name);
            if (!keys.Add(change.Target + "/" + change.Name)) throw new InvalidDataException("Duplicate recovery entry.");
        }
        if (states.Read()?.TransactionId != journal.Id)
        {
            foreach (var change in journal.Changes.Reverse())
            {
                var target = files.At(change.Target + "/" + change.Name);
                var backup = TransactionPath(journal.Id, "backup", change.Target, change.Name);
                var stage = TransactionPath(journal.Id, "stage", change.Target, change.Name);
                var hasBackup = Directory.Exists(backup);
                var newInstall = change.Install && !change.HadOriginal && !Directory.Exists(stage);
                if (!hasBackup && !newInstall) continue;
                if (File.Exists(target)) throw new IOException("Recovery found a file where a skill directory was expected. Backups were kept; move that file and retry.");
                if (Directory.Exists(target))
                {
                    var recovered = TransactionPath(journal.Id, "recovered", change.Target, change.Name);
                    Directory.CreateDirectory(Path.GetDirectoryName(recovered)!);
                    Directory.Move(target, recovered);
                }
                if (hasBackup)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    Directory.Move(backup, target);
                }
            }
        }
        File.Delete(files.JournalPath);
    }
}
