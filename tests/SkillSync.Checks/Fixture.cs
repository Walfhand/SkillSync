using System.Diagnostics;
using SkillSync.Agents;
using SkillSync.Features;
using SkillSync.Installation;
using SkillSync.Registry;
using SkillSync.Storage;

namespace SkillSync.Checks;

internal sealed class Fixture : IDisposable
{
    public string Root { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "skillsync-checks-" + Guid.NewGuid().ToString("N"));
    public string Repo => System.IO.Path.Combine(Root, "registry with spaces");
    public string Home => System.IO.Path.Combine(Root, "home");
    public LocalFiles Files { get; }
    public Init Init { get; }
    public Sync Sync { get; }
    public Status Status { get; }
    public Uninstall Uninstall { get; }
    public RegistryConfig Config => new(Repo, "main", AgentTargets.Names);

    public Fixture()
    {
        Directory.CreateDirectory(Repo);
        Directory.CreateDirectory(Home);
        Files = new LocalFiles(Home);
        var states = new StateStore(Files);
        var installer = new SkillInstaller(Files, states);
        Init = new Init(Files, states, installer);
        Sync = new Sync(Files, states, installer);
        Status = new Status(Files, states, installer);
        Uninstall = new Uninstall(Files, states, installer);
        Git("init", "--quiet", "--initial-branch=main");
    }

    public string Path(string relative) => System.IO.Path.Combine(Home, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
    public string Read(string relative) => File.ReadAllText(Path(relative));
    public void Write(string relative, string value)
    {
        var path = Path(relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, value);
    }

    public void Skill(string name, string body, string category = "")
    {
        var directory = System.IO.Path.Combine(Repo, "skills", category, name);
        Directory.CreateDirectory(directory);
        File.WriteAllText(System.IO.Path.Combine(directory, "SKILL.md"), $"---\nname: {name}\ndescription: Review changes for the team.\n---\n{body}\n");
    }

    public void Commit()
    {
        Git("add", "--all");
        Git("commit", "--quiet", "-m", "Update registry");
    }

    public string Git(params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = Repo, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = System.IO.Path.Combine(Root, "empty.gitconfig");
        foreach (var argument in new[] { "-c", "user.name=SkillSync Checks", "-c", "user.email=checks@example.invalid", "-c", "commit.gpgsign=false", "-c", "core.autocrlf=false", "-c", "core.hooksPath=" + System.IO.Path.Combine(Root, "empty-hooks") }.Concat(arguments))
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WhenAll(output, error).GetAwaiter().GetResult();
        if (process.ExitCode != 0) throw new InvalidOperationException("Fixture Git failed: " + error.Result);
        return output.Result;
    }

    public void Dispose()
    {
        // Git objects are read-only on Windows; clear that attribute before removing the fixture.
        foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(Root, recursive: true);
    }
}
