using SkillSync.Cli;
using static SkillSync.Checks.Assertions;

namespace SkillSync.Checks.Features;

internal static class ContributionChecks
{
    public static readonly (string Name, Func<Task> Run)[] All =
    [
        ("new creates a categorized skill and validate checks the uncommitted working tree", async () =>
        {
            using var f = new Fixture();
            using var output = new StringWriter(); using var error = new StringWriter();
            Check(await CliApp.RunAsync(["new", "review", "--repo", f.Repo, "--category", "architecture/backend", "--description", "Review: safe \"quotes\"."], f.Home, output, error) == 0, error.ToString());
            var document = Path.Combine(f.Repo, "skills/architecture/backend/review/SKILL.md");
            Check(File.Exists(document), "Scaffold missing.");
            Check(await CliApp.RunAsync(["validate", "--repo", f.Repo], f.Home, output, error) == 0, error.ToString());
            var original = File.ReadAllText(document);
            Check(await CliApp.RunAsync(["new", "review", "--repo", f.Repo], f.Home, output, error) != 0, "Duplicate creation accepted.");
            Check(File.ReadAllText(document) == original, "Scaffold overwrote an existing skill.");
            File.WriteAllText(document, "invalid");
            Check(await CliApp.RunAsync(["validate", "--repo", f.Repo], f.Home, output, error) != 0, "Uncommitted invalid frontmatter accepted.");
        }),
        ("scaffold refuses traversal and nesting inside an existing skill", async () =>
        {
            using var f = new Fixture(); f.Skill("parent", "one");
            using var output = new StringWriter(); using var error = new StringWriter();
            Check(await CliApp.RunAsync(["new", "child", "--repo", f.Repo, "--category", "../escape"], f.Home, output, error) != 0, "Traversal accepted.");
            Check(await CliApp.RunAsync(["new", "child", "--repo", f.Repo, "--category", "parent"], f.Home, output, error) != 0, "Nested skill accepted.");
            Check(!Directory.Exists(Path.Combine(f.Repo, "skills/parent/child")), "Rejected scaffold wrote files.");
        }),
        ("propose refuses unrelated pending files before creating or pushing a branch", async () =>
        {
            using var f = new Fixture(); f.Skill("review", "one"); f.Commit();
            f.Skill("review", "two"); File.WriteAllText(Path.Combine(f.Repo, "secret.txt"), "keep private");
            using var output = new StringWriter(); using var error = new StringWriter();
            Check(await CliApp.RunAsync(["propose", "--repo", f.Repo, "--title", "Update review"], f.Home, output, error) != 0, "Unrelated pending files accepted.");
            Check(error.ToString().Contains("outside skills"), "Unrelated changes were not explained.");
            Check(f.Git("branch", "--show-current").Trim() == "main", "Rejected proposal switched branches.");
        })
    ];
}
