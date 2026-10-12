using SkillSync.Cli;
using static SkillSync.Checks.Assertions;

namespace SkillSync.Checks.Features;

internal static class MaintenanceChecks
{
    public static readonly (string Name, Func<Task> Run)[] All =
    [
        ("maintenance commands are registered and reject unsafe update versions", async () =>
        {
            using var f = new Fixture(); using var output = new StringWriter(); using var error = new StringWriter();
            Check(await CliApp.RunAsync(["update", "--help"], f.Home, output, error) == 0 && output.ToString().Contains("--version"), "Update command missing.");
            output.GetStringBuilder().Clear();
            Check(await CliApp.RunAsync(["remove", "--help"], f.Home, output, error) == 0 && output.ToString().Contains("PATH"), "Complete removal command missing.");
            Check(await CliApp.RunAsync(["update", "--version", "../bad"], f.Home, output, error) != 0, "Unsafe update version accepted.");
            Check(!Directory.Exists(f.Path(".skillsync")), "Rejected update changed local state.");
        }),
        ("PATH removal preserves unrelated shell content and pre-existing Windows entries", () =>
        {
            Check(SkillSync.Maintenance.PathOwnership.RemoveWindowsEntry("C:\\Windows;C:\\Apps;C:\\Tools", "c:\\apps\\") == "C:\\Windows;C:\\Tools", "Windows PATH removal loses unrelated entries.");
            if (OperatingSystem.IsWindows()) return Task.CompletedTask;
            using var f = new Fixture();
            var directory = f.Path("bin with 'quotes'");
            Directory.CreateDirectory(directory);
            var quote = "'" + directory.Replace("'", "'\\''") + "'";
            var line = $"case \":$PATH:\" in *:{quote}:*) ;; *) export PATH={quote}:\"$PATH\" ;; esac";
            var profile = f.Path("shell-profile");
            File.WriteAllText(profile, "before\n\n# SkillSync\n" + line + "\nafter\n");
            var receipt = new SkillSync.Maintenance.InstallReceipt(directory, profile, line, true);
            File.WriteAllText(Path.Combine(directory, ".skillsync-path-test.json"), System.Text.Json.JsonSerializer.Serialize(receipt, SkillSync.Storage.LocalFiles.Json));
            SkillSync.Maintenance.PathOwnership.Remove(Path.Combine(directory, "skillsync"));
            Check(File.ReadAllText(profile) == "before\n\nafter\n", "Shell PATH cleanup changed unrelated content.");
            return Task.CompletedTask;
        })
    ];
}
