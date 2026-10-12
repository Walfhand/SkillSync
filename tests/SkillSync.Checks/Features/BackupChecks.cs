using SkillSync.Cli;
using static SkillSync.Checks.Assertions;

namespace SkillSync.Checks.Features;

internal static class BackupChecks
{
    public static readonly (string Name, Func<Task> Run)[] All =
    [
        ("rollback restores changed and removed skills, removes additions and pins the restored revision", async () =>
        {
            using var f = new Fixture();
            f.Skill("review", "old"); f.Skill("removed", "old"); f.Commit();
            var before = await f.Init.ExecuteAsync(f.Config);
            Directory.Delete(Path.Combine(f.Repo, "skills/removed"), true);
            f.Skill("review", "new"); f.Skill("added", "new"); f.Commit();
            await f.Sync.ExecuteAsync();
            await f.Sync.ExecuteAsync();
            using var output = new StringWriter(); using var error = new StringWriter();
            Check(await CliApp.RunAsync(["rollback"], f.Home, output, error) == 0, error.ToString());
            Check(f.Read(".agents/skills/review/SKILL.md").Contains("old") && Directory.Exists(f.Path(".agents/skills/removed")), "Previous content not restored.");
            Check(!Directory.Exists(f.Path(".agents/skills/added")), "New skill not removed.");
            Check(f.Status.Execute().State.Config.Revision == before.Commit, "Rollback did not pin restored revision.");
            await f.Sync.ExecuteAsync();
            Check(f.Read(".agents/skills/review/SKILL.md").Contains("old"), "Sync immediately undid rollback.");
        }),
        ("rollback refuses local edits and corrupted backups without partial restoration", async () =>
        {
            using var f = new Fixture(); f.Skill("review", "old"); f.Commit(); await f.Init.ExecuteAsync(f.Config);
            f.Skill("review", "new"); f.Commit(); var next = await f.Sync.ExecuteAsync();
            var backup = f.Path($".skillsync/transactions/{next.TransactionId}/backup/.agents/skills/review/SKILL.md");
            File.WriteAllText(backup, "corrupt");
            using var output = new StringWriter(); using var error = new StringWriter();
            Check(await CliApp.RunAsync(["rollback"], f.Home, output, error) != 0, "Corrupted backup accepted.");
            Check(f.Read(".claude/skills/review/SKILL.md").Contains("new"), "Rollback partially applied.");
        }),
        ("backup pruning requires explicit confirmation and preserves rollback and recovered files", async () =>
        {
            using var f = new Fixture(); f.Skill("review", "one"); f.Commit(); await f.Init.ExecuteAsync(f.Config);
            f.Skill("review", "two"); f.Commit(); var old = await f.Sync.ExecuteAsync();
            f.Skill("review", "three"); f.Commit(); var prunable = await f.Sync.ExecuteAsync();
            f.Skill("review", "four"); f.Commit(); var current = await f.Sync.ExecuteAsync();
            var recovered = f.Path($".skillsync/transactions/{old.TransactionId}/recovered/notes.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(recovered)!); File.WriteAllText(recovered, "personal");
            using var output = new StringWriter(); using var error = new StringWriter();
            Check(await CliApp.RunAsync(["backups", "list"], f.Home, output, error) == 0, error.ToString());
            Check(output.ToString().Contains(current.TransactionId), "Backup list missing current transaction.");
            Check(await CliApp.RunAsync(["backups", "prune", "--keep", "0"], f.Home, output, error) != 0, "Prune ran without confirmation.");
            Check(await CliApp.RunAsync(["backups", "prune", "--keep", "0", "--yes"], f.Home, output, error) == 0, error.ToString());
            Check(File.Exists(recovered) && Directory.Exists(f.Path($".skillsync/transactions/{current.TransactionId}/backup")), "Prune destroyed recovery data.");
            Check(!Directory.Exists(f.Path($".skillsync/transactions/{prunable.TransactionId}")), "Old backup was not actually pruned.");
        })
    ];
}
