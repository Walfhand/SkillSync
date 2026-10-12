using SkillSync.Runtime;
using SkillSync.Storage;

namespace SkillSync.Scheduling;

public sealed record ScheduleState(int Minutes, string Executable, string Platform);
public sealed record SyncAttempt(DateTimeOffset Time, bool Success, string? Error);

public sealed class AutoSyncScheduler(LocalFiles files,
    Func<string, string[], string?, CancellationToken, Task<CommandResult>>? runner = null)
{
    private string StatePath => files.At(".skillsync/schedule.json");
    private static string Platform => OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
    private async Task<CommandResult> Run(string command, string[] args, CancellationToken cancellation)
        => await (runner ?? Commands.RunAsync)(command, args, null, cancellation);
    private async Task Require(string command, string[] args, CancellationToken cancellation)
    {
        var result = await Run(command, args, cancellation);
        if (result.ExitCode != 0) throw new IOException($"{command} failed: {result.Error}");
    }
    private async Task<string> Domain(CancellationToken cancellation)
    {
        var result = await Run("id", ["-u"], cancellation);
        if (result.ExitCode != 0 || !uint.TryParse(result.Output.Trim(), out _)) throw new IOException("Could not determine the launchd user.");
        return "gui/" + result.Output.Trim();
    }
    private string TaskName => "SkillSync-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(files.Home)))[..12];

    public ScheduleState? Read() => files.ReadJson<ScheduleState>(StatePath);
    public async Task EnableAsync(string executable, int minutes, CancellationToken cancellation)
    {
        var definitions = SchedulerDefinitions.Build(Platform, executable, files.Home, minutes,
            Environment.UserDomainName + "\\" + Environment.UserName, Environment.GetEnvironmentVariable("PATH") ?? "");
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable)) throw new ArgumentException("Use an installed standalone skillsync executable.");
        foreach (var (relative, _) in definitions)
        {
            var path = files.At(relative);
            if (File.Exists(path) && !File.ReadAllText(path).Contains("Managed by SkillSync", StringComparison.Ordinal))
                throw new IOException("Scheduler file already exists and is not managed by SkillSync.");
        }
        var previous = Read();
        if (previous is not null) await DisableAsync(cancellation);
        foreach (var (relative, content) in definitions)
        {
            var path = files.At(relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, Platform == "win" ? System.Text.Encoding.Unicode : new System.Text.UTF8Encoding(false));
        }
        try
        {
            if (Platform == "linux")
            {
                await Require("systemctl", ["--user", "link", files.At(".config/systemd/user/skillsync.service")], cancellation);
                await Require("systemctl", ["--user", "daemon-reload"], cancellation);
                await Require("systemctl", ["--user", "enable", "--now", files.At(".config/systemd/user/skillsync.timer")], cancellation);
            }
            else if (Platform == "osx")
                await Require("launchctl", ["bootstrap", await Domain(cancellation), files.At(definitions.Single().Key)], cancellation);
            else
            {
                await Require("schtasks.exe", ["/Create", "/TN", TaskName, "/XML", files.At(".skillsync/schedule.xml"), "/F"], cancellation);
                await Require("schtasks.exe", ["/Run", "/TN", TaskName], cancellation);
            }
            files.WriteJson(StatePath, new ScheduleState(minutes, executable, Platform));
        }
        catch
        {
            // Registration may have succeeded before a later command failed.
            try
            {
                if (Platform == "linux") await Run("systemctl", ["--user", "disable", "--now", "skillsync.timer", "skillsync.service"], CancellationToken.None);
                else if (Platform == "osx") await Run("launchctl", ["bootout", (await Domain(CancellationToken.None)) + "/io.skillsync.sync"], CancellationToken.None);
                else await Run("schtasks.exe", ["/Delete", "/TN", TaskName, "/F"], CancellationToken.None);
            }
            catch (Exception cleanup) when (cleanup is IOException or System.ComponentModel.Win32Exception or OperationCanceledException) { }
            foreach (var relative in definitions.Keys) File.Delete(files.At(relative));
            throw;
        }
    }

    public async Task DisableAsync(CancellationToken cancellation)
    {
        var state = Read();
        if (state is null) return;
        if (state.Platform != Platform) throw new InvalidDataException("Scheduler state belongs to another platform.");
        if (Platform == "linux")
            await Require("systemctl", ["--user", "disable", "--now", "skillsync.timer", "skillsync.service"], cancellation);
        else if (Platform == "osx")
            await Require("launchctl", ["bootout", (await Domain(cancellation)) + "/io.skillsync.sync"], cancellation);
        else await Require("schtasks.exe", ["/Delete", "/TN", TaskName, "/F"], cancellation);
        var definitions = SchedulerDefinitions.Build(Platform, state.Executable, files.Home, state.Minutes, "", "");
        foreach (var relative in definitions.Keys) File.Delete(files.At(relative));
        File.Delete(StatePath);
        if (Platform == "linux") await Require("systemctl", ["--user", "daemon-reload"], cancellation);
    }

    public async Task<string> StatusAsync(CancellationToken cancellation)
    {
        var state = Read();
        if (state is null) return "Auto-sync disabled.";
        var result = Platform == "linux" ? await Run("systemctl", ["--user", "is-active", "skillsync.timer"], cancellation) :
            Platform == "osx" ? await Run("launchctl", ["print", (await Domain(cancellation)) + "/io.skillsync.sync"], cancellation) :
            await Run("schtasks.exe", ["/Query", "/TN", TaskName], cancellation);
        return $"Auto-sync configured every {state.Minutes} minutes; scheduler {(result.ExitCode == 0 ? "active" : "unavailable")}.";
    }
}
