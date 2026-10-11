using SkillSync.Cli;
using static SkillSync.Checks.Assertions;

namespace SkillSync.Checks;

internal static class CliChecks
{
    public static readonly (string Name, Func<Task> Run)[] All =
    [
        ("CLI commands complete the install, sync, status and uninstall workflow", async () =>
        {
            using var fixture = new Fixture();
            fixture.Skill("review", "v1", "architecture");
            fixture.Commit();
            using var output = new StringWriter();
            using var error = new StringWriter();
            Check(await CliApp.RunAsync(["init", fixture.Repo], fixture.Home, output, error) == 0, error.ToString());
            Check(fixture.Status.Execute().State.Skills.Length == 2, "Default agents were not registered.");
            fixture.Skill("review", "v2", "architecture");
            fixture.Commit();
            Check(await CliApp.RunAsync(["sync"], fixture.Home, output, error) == 0, error.ToString());
            Check(fixture.Read(".agents/skills/review/SKILL.md").Contains("v2"), "CLI sync did not apply the update.");
            Check(await CliApp.RunAsync(["status"], fixture.Home, output, error) == 0, error.ToString());
            Check(output.ToString().Contains(".agents/skills/review: synced"), "CLI status lost its output.");
            Check(await CliApp.RunAsync(["uninstall"], fixture.Home, output, error) == 0, error.ToString());
            Check(!Directory.Exists(fixture.Path(".agents/skills/review")), "CLI uninstall did not remove its skills.");
            Check(error.ToString() == "", "Successful commands wrote to stderr.");
        }),
        ("CLI help, parsing errors and invalid watch intervals do not create local state", async () =>
        {
            using var fixture = new Fixture();
            using var output = new StringWriter();
            using var error = new StringWriter();
            Check(await CliApp.RunAsync(["--help"], fixture.Home, output, error) == 0, "Help failed.");
            Check(output.ToString().Contains("init") && output.ToString().Contains("watch"), "Command registration is incomplete.");
            Check(await CliApp.RunAsync(["--unknown"], fixture.Home, output, error) == 1, "Unknown option was accepted.");
            error.GetStringBuilder().Clear();
            Check(await CliApp.RunAsync(["watch", "--interval", "0"], fixture.Home, output, error) == 1, "Invalid interval was accepted.");
            Check(error.ToString().Contains("between 1 and 1440"), "Watch validation message changed.");
            Check(!Directory.Exists(fixture.Path(".skillsync")), "Read-only or rejected CLI invocations created local state.");
        }),
        ("CLI init binds the registry branch and selected agents", async () =>
        {
            using var fixture = new Fixture();
            fixture.Skill("review", "v1");
            fixture.Commit();
            fixture.Git("branch", "approved");
            using var output = new StringWriter();
            using var error = new StringWriter();
            Check(await CliApp.RunAsync(["init", fixture.Repo, "--branch", "approved", "--agent", "codex", "cursor"], fixture.Home, output, error) == 0, error.ToString());
            var state = fixture.Status.Execute().State;
            Check(state.Config.Branch == "approved", "Branch option was lost.");
            Check(state.Config.Agents.SequenceEqual(["codex", "cursor"]), "Agent options were lost.");
            Check(state.Skills.Length == 1, "Shared destination should be installed only once.");
            Check(!Directory.Exists(fixture.Path(".claude")), "An unselected agent was modified.");
        }),
        ("CLI init rejects invalid configuration before filesystem changes", async () =>
        {
            using var fixture = new Fixture();
            using var output = new StringWriter();
            using var error = new StringWriter();
            Check(await CliApp.RunAsync(["init", fixture.Repo, "--agent", "unknown"], fixture.Home, output, error) == 1, "Unknown agent was accepted.");
            Check(error.ToString().Contains("Unknown agent"), "Configuration rejection was not reported.");
            Check(!Directory.Exists(fixture.Path(".skillsync")), "Invalid configuration created local state.");
        }),
        ("CLI watch cancels gracefully and preserves valid installations", async () =>
        {
            using var fixture = new Fixture();
            fixture.Skill("review", "v1");
            fixture.Commit();
            await fixture.Init.ExecuteAsync(fixture.Config);
            using var output = new StringWriter();
            using var error = new StringWriter();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            var exit = await CliApp.RunAsync(["watch", "--interval", "1"], fixture.Home, output, error, cancellation.Token);
            Check(exit == 130, "Cancelled watch did not return the interruption exit code.");
            Check(fixture.Status.Execute().Lines.All(line => line.EndsWith("synced")), "Cancelled watch damaged installed skills.");
            Check(!File.Exists(fixture.Files.JournalPath), "Cancelled watch left an unfinished transaction.");
        }),
        ("CLI errors preserve exit codes and strip registry-controlled terminal escapes", async () =>
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            var console = new CommandOutput(output, error);
            var exit = await console.ExecuteAsync(() => throw new IOException("unsafe\u001b[31m\ntext"), CancellationToken.None);
            Check(exit == 1, "I/O failure did not return an error exit code.");
            Check(!error.ToString().TrimEnd('\r', '\n').Any(char.IsControl), "Untrusted control characters reached stderr.");
            Check(output.ToString() == "", "Error was sent to stdout.");
        })
    ];
}
