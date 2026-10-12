using System.CommandLine;
using SkillSync.Cli;
using SkillSync.Registry;
using SkillSync.Runtime;

namespace SkillSync.Features;

public sealed class Propose
{
    public async Task<string> ExecuteAsync(string directory, string title, string baseBranch, CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Any(char.IsControl)) throw new ArgumentException("Provide a one-line proposal title.");
        directory = Path.GetFullPath(directory);
        WorkingRegistry.Read(directory);
        async Task<string> Git(params string[] args) => System.Text.Encoding.UTF8.GetString(await GitRegistry.RunAsync(
            ["-c", "core.hooksPath=" + Path.Combine(Path.GetTempPath(), "skillsync-no-hooks-" + Guid.NewGuid().ToString("N")),
             "-c", "core.fsmonitor=false", "-c", "protocol.allow=never", "-c", "protocol.https.allow=always",
             "-c", "protocol.ssh.allow=always", .. args], cancellation, directory));
        var pending = (await Git("status", "--porcelain=v1", "-z", "--untracked-files=all")).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        if (pending.Length == 0) throw new InvalidOperationException("No local changes to propose.");
        // Renames have a second NUL-separated path; refuse them rather than risk including unrelated files.
        if (pending.Any(entry => entry.Length < 4 || entry[0] is 'R' or 'C' || entry[1] is 'R' or 'C' ||
            !(entry[3..].StartsWith("skills/", StringComparison.Ordinal) || entry[3..] == "skillsync.json")))
            throw new InvalidOperationException("Pending files outside skills/ (or renames) must be committed or moved before proposing.");
        await Git("check-ref-format", "refs/heads/" + baseBranch);
        if ((await Git("branch", "--show-current")).Trim() != baseBranch)
            throw new InvalidOperationException("Start proposals from the requested base branch.");
        var source = (await Git("remote", "get-url", "origin")).Trim();
        RegistryConfig.ValidateSource(source);
        var auth = await Commands.RunAsync("gh", ["auth", "status"], directory, cancellation);
        if (auth.ExitCode != 0) throw new InvalidOperationException("Authenticate GitHub CLI with gh auth login first.");
        await Git("fetch", "--quiet", "--no-tags", "--no-recurse-submodules", "origin", baseBranch);
        if ((await Git("rev-parse", "HEAD")).Trim() != (await Git("rev-parse", "refs/remotes/origin/" + baseBranch)).Trim())
            throw new InvalidOperationException("Base branch must match origin before proposing; synchronize it first.");
        var branch = "skillsync/propose-" + Guid.NewGuid().ToString("N");
        await Git("switch", "-c", branch);
        await Git("add", "--all", "--", "skills");
        if (File.Exists(Path.Combine(directory, "skillsync.json"))) await Git("add", "--", "skillsync.json");
        await Git("commit", "-m", title);
        await Git("push", "--set-upstream", "origin", branch);
        var body = Path.Combine(Path.GetTempPath(), "skillsync-proposal-" + Guid.NewGuid().ToString("N") + ".md");
        try
        {
            await File.WriteAllTextAsync(body, "Update the shared skill registry.\n\nValidation: skillsync validate passed before creating this proposal.\n", cancellation);
            var result = await Commands.RunAsync("gh", ["pr", "create", "--draft", "--base", baseBranch, "--head", branch,
                "--title", title, "--body-file", body], directory, cancellation);
            if (result.ExitCode != 0) throw new IOException($"Branch {branch} was pushed. PR creation failed; retry with gh pr create --head {branch}.");
            return result.Output.Trim();
        }
        finally { File.Delete(body); }
    }

    public Command CreateCommand(CommandOutput output)
    {
        var repo = new Option<string>("--repo") { DefaultValueFactory = _ => Environment.CurrentDirectory };
        var title = new Option<string>("--title") { Required = true };
        var baseBranch = new Option<string>("--base") { DefaultValueFactory = _ => "main" };
        var command = new Command("propose", "Commit skill changes, push a new branch and open a draft GitHub PR.") { repo, title, baseBranch };
        command.SetAction(async (parse, token) => await output.ExecuteAsync(async () =>
            output.Writer.WriteLine(await ExecuteAsync(parse.GetValue(repo)!, parse.GetValue(title)!, parse.GetValue(baseBranch)!, token)), token));
        return command;
    }
}
