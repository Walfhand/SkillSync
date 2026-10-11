using SkillSync.Installation;
using static SkillSync.Checks.Assertions;

namespace SkillSync.Checks.Installation;

internal static class RecoveryChecks
{
    public static readonly (string Name, Func<Task> Run)[] All =
    [
        ("interrupted update restores old content and preserves new local edits", async () =>
            {
                using var fixture = new Fixture();
                fixture.Skill("review", "v1");
                fixture.Commit();
                var first = await fixture.Init.ExecuteAsync(fixture.Config);
                var id = Guid.NewGuid().ToString("N");
                var backup = fixture.Path($".skillsync/transactions/{id}/backup/.agents/skills/review");
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(backup)!);
                Directory.Move(fixture.Path(".agents/skills/review"), backup);
                fixture.Write(".agents/skills/review/SKILL.md", "new content edited after interruption");
                fixture.Files.WriteJson(fixture.Files.JournalPath, new Journal(id, [new Change(".agents/skills", "review", true, true)]));
                Check(fixture.Status.Execute().State.Commit == first.Commit, "Recovery changed the previous revision.");
                Check(fixture.Read(".agents/skills/review/SKILL.md").Contains("v1"), "Old content was not restored.");
                Check(fixture.Read($".skillsync/transactions/{id}/recovered/.agents/skills/review/SKILL.md").Contains("edited after interruption"), "New local edits were lost during recovery.");
                Check(!File.Exists(fixture.Files.JournalPath), "Recovery journal remains after successful recovery.");
                fixture.Status.Execute();
            }),
        ("committed transaction is not rolled back after interrupted cleanup", async () =>
            {
                using var fixture = new Fixture();
                fixture.Skill("review", "v1");
                fixture.Commit();
                await fixture.Init.ExecuteAsync(fixture.Config);
                fixture.Skill("review", "v2");
                fixture.Commit();
                var second = await fixture.Sync.ExecuteAsync();
                fixture.Files.WriteJson(fixture.Files.JournalPath, new Journal(second.TransactionId, [new Change(".agents/skills", "review", true, true)]));
                fixture.Status.Execute();
                Check(fixture.Read(".agents/skills/review/SKILL.md").Contains("v2"), "Committed update was rolled back.");
            }),
        ("untrusted recovery metadata cannot escape managed targets", async () =>
            {
                using var fixture = new Fixture();
                fixture.Files.WriteJson(fixture.Files.JournalPath, new Journal(Guid.NewGuid().ToString("N"), [new Change("../../outside", "review", false, true)]));
                await Reject(() => { fixture.Status.Execute(); return Task.CompletedTask; }, "Invalid installation target");
                Check(File.Exists(fixture.Files.JournalPath), "Unsafe journal should be preserved for inspection.");
            }),
    ];
}
