using System.CommandLine;
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using SkillSync.Cli;
using SkillSync.Maintenance;
using SkillSync.Runtime;
using SkillSync.Scheduling;
using SkillSync.Storage;

namespace SkillSync.Features;

public sealed class Maintenance(LocalFiles files, StateStore states, Uninstall uninstall, AutoSyncScheduler scheduler)
{
    private static string Executable()
    {
        var path = Environment.ProcessPath!;
        if (Path.GetFileNameWithoutExtension(path) != "skillsync")
            throw new InvalidOperationException("This command requires an installed standalone skillsync executable.");
        return path;
    }
    private static string Ps(string text) => "'" + text.Replace("'", "''") + "'";
    private void DeferWindows(string body)
    {
        var id = Guid.NewGuid().ToString("N");
        var path = files.At($".skillsync/maintenance/{id}.ps1");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var log = files.At(".skillsync/maintenance.log");
        File.WriteAllText(path, "$ErrorActionPreference='Stop'\ntry {\n" +
            $"Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue\n{body}\n" +
            $"'Completed' | Out-File -LiteralPath {Ps(log)}\n}} catch {{ $_.Exception.Message | Out-File -LiteralPath {Ps(log)} }}\n" +
            $"Remove-Item -LiteralPath {Ps(path)} -Force\n", new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
            { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", path }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Could not start the deferred maintenance process.");
    }
    public Command UpdateCommand(CommandOutput output)
    {
        var version = new Option<string>("--version") { DefaultValueFactory = _ => "latest" };
        var command = new Command("update", "Update the standalone CLI using its embedded, SHA-256 verifying installer.") { version };
        command.SetAction(async (parse, token) => await output.ExecuteAsync(async () =>
        {
            var release = parse.GetValue(version)!;
            if (release != "latest" && !Regex.IsMatch(release, "^v[0-9]+\\.[0-9]+\\.[0-9]+(-[A-Za-z0-9]+([.-][A-Za-z0-9]+)*)?$"))
                throw new ArgumentException("Provide latest or a version tag.");
            var executable = Executable();
            var receipts = PathOwnership.Read(executable);
            if (!OperatingSystem.IsWindows() && receipts.Length == 0)
                throw new InvalidOperationException("Reinstall with the current installer to record the shell profile before updating.");
            using var runLock = files.Lock();
            var extension = OperatingSystem.IsWindows() ? "ps1" : "sh";
            var script = files.At($".skillsync/maintenance/install-{Guid.NewGuid():N}.{extension}");
            Directory.CreateDirectory(Path.GetDirectoryName(script)!);
            using (var input = Assembly.GetExecutingAssembly().GetManifestResourceStream("SkillSync.install." + extension)!)
            using (var target = File.Create(script)) await input.CopyToAsync(target, token);
            if (OperatingSystem.IsWindows())
            {
                DeferWindows($"& {Ps(script)} -Version {Ps(release)} -InstallDir {Ps(Path.GetDirectoryName(executable)!)} -NoPathUpdate\nif ($LASTEXITCODE -ne 0) {{ throw 'Update failed; existing binary was kept.' }}\nRemove-Item -LiteralPath {Ps(script)} -Force");
                output.Writer.WriteLine("Update queued after this process exits. Result: ~/.skillsync/maintenance.log.");
            }
            else
            {
                try
                {
                    var result = await Commands.RunAsync("/bin/bash", [script, "--version", release, "--install-dir", receipts[0].Receipt.Directory,
                        "--profile", receipts[0].Receipt.Profile!], null, token);
                    if (result.ExitCode != 0) throw new IOException("Update failed; existing binary was kept. " + result.Error);
                    output.Writer.WriteLine(result.Output);
                }
                finally { File.Delete(script); }
            }
        }, token));
        return command;
    }

    public Command RemoveCommand(CommandOutput output)
    {
        var command = new Command("remove", "Remove managed skills, auto-sync, owned PATH entries and the CLI; keep backups.");
        command.SetAction(async (_, token) => await output.ExecuteAsync(async () =>
        {
            var executable = Executable();
            PathOwnership.Read(executable);
            if (states.Read() is { Uninstalled: false }) uninstall.Execute();
            using var runLock = files.Lock();
            await scheduler.DisableAsync(token);
            PathOwnership.Remove(executable);
            if (OperatingSystem.IsWindows()) DeferWindows($"Remove-Item -LiteralPath {Ps(executable)} -Force");
            else File.Delete(executable);
            output.Writer.WriteLine("Managed skills, scheduler, owned PATH entries and CLI removed (Windows binary removal is deferred). Backups were kept.");
        }, token));
        return command;
    }
}
