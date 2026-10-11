using static SkillSync.Checks.Assertions;

namespace SkillSync.Checks.Features;

internal static class SyncChecks
{
    public static readonly (string Name, Func<Task> Run)[] All =
    [
        ("moving a skill into a category keeps its installation and ownership", async () =>
            {
                using var fixture = new Fixture();
                fixture.Skill("review", "v1");
                fixture.Commit();
                var first = await fixture.Init.ExecuteAsync(fixture.Config);
                Directory.CreateDirectory(System.IO.Path.Combine(fixture.Repo, "skills/pull-request"));
                fixture.Git("mv", "skills/review", "skills/pull-request/review");
                fixture.Commit();
                var second = await fixture.Sync.ExecuteAsync();
                Check(second.Commit != first.Commit, "Moved registry revision was not recorded.");
                Check(second.Skills.SequenceEqual(first.Skills), "Moving a category changed skill identity or content.");
                Check(fixture.Read(".agents/skills/review/SKILL.md").Contains("v1"), "Moved skill disappeared.");
                Check(!Directory.Exists(fixture.Path($".skillsync/transactions/{second.TransactionId}/backup")), "Unchanged skill was unnecessarily replaced.");
            }),
        ("duplicate names across categories reject the update before changing installations", async () =>
            {
                using var fixture = new Fixture();
                fixture.Skill("review", "original", "architecture");
                fixture.Commit();
                var first = await fixture.Init.ExecuteAsync(fixture.Config);
                fixture.Skill("review", "duplicate", "pull-request");
                fixture.Commit();
                await Reject(() => fixture.Sync.ExecuteAsync(), "must be unique");
                Check(fixture.Status.Execute().State.Commit == first.Commit, "Duplicate names changed state.");
                Check(fixture.Read(".agents/skills/review/SKILL.md").Contains("original"), "Duplicate name overwrote an installed skill.");
            }),
        ("a skill missing its SKILL.md cannot silently remove installed content", async () =>
            {
                using var fixture = new Fixture();
                fixture.Skill("review", "v1", "architecture");
                File.WriteAllText(System.IO.Path.Combine(fixture.Repo, "skills/architecture/review/rules.md"), "Rules");
                fixture.Commit();
                var first = await fixture.Init.ExecuteAsync(fixture.Config);
                File.Delete(System.IO.Path.Combine(fixture.Repo, "skills/architecture/review/SKILL.md"));
                fixture.Commit();
                await Reject(() => fixture.Sync.ExecuteAsync(), "containing SKILL.md");
                Check(fixture.Status.Execute().State.Commit == first.Commit, "Malformed registry changed state.");
                Check(fixture.Read(".agents/skills/review/SKILL.md").Contains("v1"), "Malformed registry removed the installed skill.");
            }),
        ("local changes block all updates and uninstall", async () =>
            {
                using var fixture = new Fixture();
                fixture.Skill("review", "v1");
                fixture.Commit();
                var first = await fixture.Init.ExecuteAsync(fixture.Config);
                fixture.Write(".agents/skills/review/SKILL.md", "local edits");
                fixture.Skill("review", "v2");
                fixture.Commit();
                await Reject(() => fixture.Sync.ExecuteAsync(), "Local edits");
                Check(fixture.Read(".agents/skills/review/SKILL.md") == "local edits", "Local edits were lost.");
                Check(fixture.Read(".claude/skills/review/SKILL.md").Contains("v1"), "Another target was partially updated.");
                Check(fixture.Status.Execute().State.Commit == first.Commit, "Failed sync changed the recorded revision.");
                await Reject(() => { fixture.Uninstall.Execute(); return Task.CompletedTask; }, "Local edits");
                Check(Directory.Exists(fixture.Path(".claude/skills/review")), "Uninstall partially removed skills.");
                Check(!Directory.EnumerateDirectories(fixture.Path(".skillsync/transactions"), "stage", SearchOption.AllDirectories).Any(), "Rejected operations left staging data behind.");
            }),
        ("deleted registry skills are removed without touching neighbors", async () =>
            {
                using var fixture = new Fixture();
                fixture.Skill("review", "v1");
                fixture.Skill("commit", "v1");
                fixture.Commit();
                await fixture.Init.ExecuteAsync(fixture.Config);
                fixture.Write(".agents/skills/personal/notes.txt", "keep");
                Directory.Delete(System.IO.Path.Combine(fixture.Repo, "skills/review"), true);
                fixture.Commit();
                await fixture.Sync.ExecuteAsync();
                Check(!Directory.Exists(fixture.Path(".agents/skills/review")), "Removed registry skill remains installed.");
                Check(Directory.Exists(fixture.Path(".agents/skills/commit")), "Unrelated managed skill was removed.");
                Check(fixture.Read(".agents/skills/personal/notes.txt") == "keep", "Personal neighbor changed.");
            }),
        ("unreachable registry keeps installed content and revision", async () =>
            {
                using var fixture = new Fixture();
                fixture.Skill("review", "v1");
                fixture.Commit();
                var first = await fixture.Init.ExecuteAsync(fixture.Config);
                Directory.Move(fixture.Repo, fixture.Repo + "-offline");
                await Reject(() => fixture.Sync.ExecuteAsync(), "Git failed");
                Check(fixture.Status.Execute().State.Commit == first.Commit, "Offline sync changed state.");
                Check(fixture.Read(".agents/skills/review/SKILL.md").Contains("v1"), "Offline sync changed content.");
            }),
        ("missing managed copies are restored", async () =>
            {
                using var fixture = new Fixture();
                fixture.Skill("review", "v1");
                fixture.Commit();
                await fixture.Init.ExecuteAsync(fixture.Config);
                Directory.Delete(fixture.Path(".agents/skills/review"), true);
                Check(fixture.Status.Execute().Lines.Any(line => line.EndsWith("missing")), "Missing copy was not detected.");
                await fixture.Sync.ExecuteAsync();
                Check(fixture.Read(".agents/skills/review/SKILL.md").Contains("v1"), "Missing copy was not restored.");
            }),
    ];
}
