using SkillSync.Cli;
using static SkillSync.Checks.Assertions;

namespace SkillSync.Checks.Features;

internal static class SelectionChecks
{
    public static readonly (string Name, Func<Task> Run)[] All =
    [
        ("selection lists categories and installs the union of named skills and categories", async () =>
        {
            using var f = new Fixture();
            f.Skill("review", "one", "architecture/backend");
            f.Skill("commit", "two", "delivery");
            f.Skill("other", "three", "other");
            f.Commit();
            using var output = new StringWriter();
            using var error = new StringWriter();
            Check(await CliApp.RunAsync(["init", f.Repo, "--category", "architecture", "--skill", "commit"], f.Home, output, error) == 0, error.ToString());
            Check(Directory.Exists(f.Path(".agents/skills/review")) && Directory.Exists(f.Path(".agents/skills/commit")), "Selection did not include its union.");
            Check(!Directory.Exists(f.Path(".agents/skills/other")), "Unselected skill installed.");
            Check(await CliApp.RunAsync(["list"], f.Home, output, error) == 0, error.ToString());
            Check(output.ToString().Contains("architecture/backend/review") && output.ToString().Contains("other/other"), "List hides unselected skills or categories.");
            Check(await CliApp.RunAsync(["select", "--skill", "other"], f.Home, output, error) == 0, error.ToString());
            Check(!Directory.Exists(f.Path(".agents/skills/review")) && Directory.Exists(f.Path(".agents/skills/other")), "Changing selection did not reconcile installed copies.");
            f.Skill("other", "updated", "other"); f.Commit();
            await f.Sync.ExecuteAsync();
            Check(f.Read(".agents/skills/other/SKILL.md").Contains("updated"), "Sync forgot persisted selection.");
            Check(await CliApp.RunAsync(["select", "--all"], f.Home, output, error) == 0, error.ToString());
            Check(f.Status.Execute().State.Skills.Length == 6, "Select all did not restore the full library.");
        }),
        ("unknown selections fail without removing installed skills", async () =>
        {
            using var f = new Fixture(); f.Skill("review", "one"); f.Commit();
            await f.Init.ExecuteAsync(f.Config);
            var before = File.ReadAllText(f.Files.StatePath);
            using var output = new StringWriter(); using var error = new StringWriter();
            Check(await CliApp.RunAsync(["select", "--category", "missing"], f.Home, output, error) != 0, "Unknown category accepted.");
            Check(File.ReadAllText(f.Files.StatePath) == before && Directory.Exists(f.Path(".agents/skills/review")), "Rejected selection changed state.");
        }),
        ("team policy includes required skills, labels recommendations and excludes blocked skills", async () =>
        {
            using var f = new Fixture();
            foreach (var name in new[] { "required", "suggested", "blocked", "chosen" }) f.Skill(name, name);
            File.WriteAllText(Path.Combine(f.Repo, "skillsync.json"), """{"teams":{"backend":{"required":["required"],"recommended":["suggested"],"blocked":["blocked"]}}}""");
            f.Commit();
            using var output = new StringWriter(); using var error = new StringWriter();
            Check(await CliApp.RunAsync(["init", f.Repo, "--team", "backend", "--skill", "chosen"], f.Home, output, error) == 0, error.ToString());
            Check(Directory.Exists(f.Path(".agents/skills/required")) && !Directory.Exists(f.Path(".agents/skills/suggested")), "Required or recommended policy was ignored.");
            Check(await CliApp.RunAsync(["list"], f.Home, output, error) == 0 && output.ToString().Contains("recommended"), "Recommendation not visible.");
            Check(await CliApp.RunAsync(["select", "--skill", "blocked"], f.Home, output, error) != 0, "Explicit blocked selection accepted.");
            Check(await CliApp.RunAsync(["select", "--all"], f.Home, output, error) == 0, error.ToString());
            Check(!Directory.Exists(f.Path(".agents/skills/blocked")), "Blocked skill installed by select all.");
        }),
        ("invalid team policy blocks the whole update before changing copies", async () =>
        {
            using var f = new Fixture(); f.Skill("review", "one"); f.Commit();
            await f.Init.ExecuteAsync(f.Config);
            File.WriteAllText(Path.Combine(f.Repo, "skillsync.json"), """{"teams":{"backend":{"required":["review"],"blocked":["review"]}}}""");
            f.Skill("review", "two"); f.Commit();
            await Reject(() => f.Sync.ExecuteAsync(), "policy");
            Check(f.Read(".agents/skills/review/SKILL.md").Contains("one"), "Invalid policy partially applied.");
        }),
        ("approved revision stays pinned when the registry branch advances", async () =>
        {
            using var f = new Fixture(); f.Skill("review", "approved"); f.Commit();
            var revision = f.Git("rev-parse", "HEAD").Trim();
            f.Skill("review", "unapproved"); f.Commit();
            using var output = new StringWriter(); using var error = new StringWriter();
            Check(await CliApp.RunAsync(["init", f.Repo, "--revision", revision], f.Home, output, error) == 0, error.ToString());
            await f.Sync.ExecuteAsync();
            Check(f.Read(".agents/skills/review/SKILL.md").Contains("approved") && !f.Read(".agents/skills/review/SKILL.md").Contains("unapproved"), "Pinned revision drifted.");
            Check(f.Status.Execute().State.Commit == revision, "Pinned revision not recorded.");
        })
    ];
}
