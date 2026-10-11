using static SkillSync.Checks.Assertions;

namespace SkillSync.Checks;

internal static class LifecycleChecks
{
    public static readonly (string Name, Func<Task> Run)[] All =
    [
        ("install, update, backup and uninstall through a real Git registry", async () =>
            {
                using var fixture = new Fixture();
                fixture.Skill("review", "v1");
                fixture.Commit();
                var first = await fixture.Init.ExecuteAsync(fixture.Config);
                Check(first.Skills.Length == 2, "Codex and Cursor should share one installation.");
                Check(fixture.Read(".agents/skills/review/SKILL.md").Contains("v1"), "Shared skill was not installed.");
                Check(fixture.Read(".claude/skills/review/SKILL.md").Contains("v1"), "Claude skill was not installed.");
                fixture.Skill("review", "v2");
                fixture.Commit();
                var second = await fixture.Sync.ExecuteAsync();
                Check(first.Commit != second.Commit, "Revision did not change.");
                Check(fixture.Read(".agents/skills/review/SKILL.md").Contains("v2"), "Update was not applied.");
                Check(fixture.Status.Execute().Lines.All(line => line.EndsWith("synced")), "Installed copies differ from their hashes.");
                Check(fixture.Read($".skillsync/transactions/{second.TransactionId}/backup/.agents/skills/review/SKILL.md").Contains("v1"), "Previous version was not backed up.");
                fixture.Write(".agents/skills/personal/SKILL.md", "personal");
                fixture.Uninstall.Execute();
                Check(!Directory.Exists(fixture.Path(".agents/skills/review")), "Managed skill remains after uninstall.");
                Check(fixture.Read(".agents/skills/personal/SKILL.md") == "personal", "Personal skill was removed.");
                await fixture.Init.ExecuteAsync(fixture.Config);
                Check(fixture.Read(".agents/skills/review/SKILL.md").Contains("v2"), "Reinstallation failed.");
            }),
    ];
}
