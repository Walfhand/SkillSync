using System.Xml.Linq;
using SkillSync.Cli;
using SkillSync.Scheduling;
using static SkillSync.Checks.Assertions;

namespace SkillSync.Checks.Features;

internal static class SchedulingChecks
{
    public static readonly (string Name, Func<Task> Run)[] All =
    [
        ("scheduler definitions run at login and periodically without a shell interpolation vulnerability", () =>
        {
            var linux = SchedulerDefinitions.Build("linux", "/tmp/a \"quote\"%$dollar/skillsync", "/tmp/user", 7, "user", "/usr/bin:/bin");
            Check(linux.Count == 2 && linux.Values.Any(text => text.Contains("OnUnitActiveSec=7min")), "Linux timer missing.");
            Check(linux.Values.Any(text => text.Contains("%%") && text.Contains("\\\"") && text.Contains("sync --scheduled")), "Linux executable not escaped.");
            Check(linux.Values.Any(text => text.Contains("$$dollar")), "Systemd expands dollar signs in the executable path.");
            var mac = SchedulerDefinitions.Build("osx", "/tmp/a & quotes/skillsync", "/tmp/user", 7, "user", "/usr/bin:/bin");
            var plist = XDocument.Parse(mac.Single().Value);
            Check(plist.Descendants("integer").Any(node => node.Value == "420") && plist.Descendants("string").Any(node => node.Value == "/tmp/a & quotes/skillsync"), "macOS plist lost interval or executable.");
            var windows = SchedulerDefinitions.Build("win", "C:\\Apps 'quotes' & space\\skillsync.exe", "C:\\Users\\Test", 7, "DOMAIN\\User", "C:\\Windows");
            var xml = XDocument.Parse(windows.Single().Value);
            Check(xml.Descendants().Any(node => node.Name.LocalName == "LogonTrigger") && xml.Descendants().Any(node => node.Name.LocalName == "Interval" && node.Value == "PT7M"), "Windows logon repetition missing.");
            Check(xml.Descendants().Any(node => node.Name.LocalName == "RunLevel" && node.Value == "LeastPrivilege"), "Scheduler requests elevated privileges.");
            Check(xml.Descendants().Any(node => node.Name.LocalName == "TimeTrigger"), "Schedule does not repeat until the next login.");
            return Task.CompletedTask;
        }),
        ("scheduler status is read-only and invalid intervals do not install jobs", async () =>
        {
            using var f = new Fixture(); using var output = new StringWriter(); using var error = new StringWriter();
            Check(await CliApp.RunAsync(["autosync", "status"], f.Home, output, error) == 0, error.ToString());
            Check(output.ToString().Contains("disabled"), "Fresh scheduler not disabled.");
            Check(await CliApp.RunAsync(["autosync", "enable", "--interval", "0"], f.Home, output, error) != 0, "Invalid schedule accepted.");
            Check(!Directory.Exists(f.Path(".skillsync")), "Read-only or rejected scheduler created metadata.");
        }),
        ("scheduled synchronization records failures while preserving installed skills", async () =>
        {
            using var f = new Fixture(); f.Skill("review", "old"); f.Commit(); await f.Init.ExecuteAsync(f.Config);
            Directory.Move(f.Repo, f.Repo + "-offline");
            using var output = new StringWriter(); using var error = new StringWriter();
            Check(await CliApp.RunAsync(["sync", "--scheduled"], f.Home, output, error) != 0, "Offline scheduled sync succeeded.");
            Check(File.Exists(f.Path(".skillsync/last-sync.json")) && f.Read(".skillsync/last-sync.json").Contains("false"), "Scheduled failure was not recorded.");
            Check(f.Read(".agents/skills/review/SKILL.md").Contains("old"), "Scheduled failure damaged skills.");
        }),
        ("scheduler enable and disable persist state and clean up generated jobs through native command arguments", async () =>
        {
            using var f = new Fixture();
            var calls = new List<(string Command, string[] Arguments)>();
            var scheduler = new AutoSyncScheduler(f.Files, (command, arguments, _, _) =>
            {
                calls.Add((command, arguments));
                return Task.FromResult(new SkillSync.Runtime.CommandResult(0, command == "id" ? "501" : "active", ""));
            });
            var executable = f.Path("bin/skillsync"); f.Write("bin/skillsync", "fixture");
            await scheduler.EnableAsync(executable, 7, CancellationToken.None);
            Check(scheduler.Read()?.Minutes == 7 && calls.Count > 0, "Scheduler not persisted or not invoked.");
            Check((await scheduler.StatusAsync(CancellationToken.None)).Contains("active"), "Native scheduler status ignored.");
            await scheduler.DisableAsync(CancellationToken.None);
            Check(scheduler.Read() is null, "Disable left enabled metadata.");
            Check(!File.Exists(f.Path(".config/systemd/user/skillsync.timer")) && !File.Exists(f.Path("Library/LaunchAgents/io.skillsync.sync.plist")), "Disable left job files.");
        }),
        ("Linux scheduler units pass the native parser with quotes, percent and dollar signs in the executable path", async () =>
        {
            if (!OperatingSystem.IsLinux()) return;
            using var f = new Fixture();
            var executable = f.Path("bin \"quote\"%$value/skillsync");
            f.Write("bin \"quote\"%$value/skillsync", "#!/bin/sh\nexit 0\n");
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var definitions = SchedulerDefinitions.Build("linux", executable, f.Home, 7, "user", "/usr/bin:/bin");
            foreach (var (relative, text) in definitions) f.Write(relative, text);
            var result = await SkillSync.Runtime.Commands.RunAsync("systemd-analyze",
                ["verify", f.Path(".config/systemd/user/skillsync.service"), f.Path(".config/systemd/user/skillsync.timer")],
                null, CancellationToken.None);
            Check(result.ExitCode == 0, result.Error);
        })
    ];
}
