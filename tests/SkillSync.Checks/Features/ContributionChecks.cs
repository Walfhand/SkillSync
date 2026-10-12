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
        }),
        ("propose includes policy deletion, uses a draft PR and passes its exact body through a file", async () =>
        {
            using var f = new Fixture(); f.Skill("review", "one");
            var calls = new List<string[]>();
            var proposal = new SkillSync.Features.Propose((command, args, _, _) =>
            {
                calls.Add([command, .. args]);
                var value = command == "gh" ? "https://github.com/example/skills/pull/1" :
                    args[0] switch
                    {
                        "status" => " M skills/review/SKILL.md\0 D skillsync.json\0",
                        "branch" => "main",
                        "remote" => "https://github.com/example/skills.git",
                        "rev-parse" => new string('a', 40),
                        _ => ""
                    };
                if (command == "gh" && args[0] == "pr")
                {
                    Check(args.Contains("--draft"), "Proposal was published as a ready PR.");
                    var body = File.ReadAllText(args[Array.IndexOf(args, "--body-file") + 1]);
                    Check(body.Contains("\n\nValidation:"), "PR body lost actual newlines.");
                }
                return Task.FromResult(new SkillSync.Runtime.CommandResult(0, value, ""));
            });
            var url = await proposal.ExecuteAsync(f.Repo, "Update skills", "main", CancellationToken.None);
            Check(url.EndsWith("/pull/1"), "PR URL was lost.");
            Check(calls.Any(args => args.SequenceEqual(["git", "add", "--all", "--", "skillsync.json"])), "Policy deletion not staged.");
            Check(calls.Any(args => args[0] == "git" && args[1] == "push"), "Proposal was not pushed.");
        })
    ];
}
