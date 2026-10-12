using static SkillSync.Checks.Assertions;

namespace SkillSync.Checks.Installers;

internal static class NativeLifecycle
{
    public static async Task RunAsync(string executable)
    {
        using var registry = new Fixture();
        registry.Skill("review", "native"); registry.Commit();
        await using var install = new InstallerFixture(await File.ReadAllBytesAsync(executable));
        var home = Path.Combine(install.Root, "isolated home"); Directory.CreateDirectory(home);
        var installed = await install.InstallAsync();
        Check(installed.Exit == 0, installed.Output + installed.Error);
        var originalVersion = await install.CliAsync(home, "--version");
        Check(originalVersion.Exit == 0, originalVersion.Output + originalVersion.Error);
        var init = await install.CliAsync(home, "init", registry.Repo);
        Check(init.Exit == 0, init.Output + init.Error);
        if (Environment.GetEnvironmentVariable("SKILLSYNC_TEST_SCHEDULER") == "1")
        {
            try
            {
                var enable = await install.CliAsync(home, "autosync", "enable", "--interval", "1");
                Check(enable.Exit == 0, enable.Output + enable.Error);
                var last = Path.Combine(home, ".skillsync/last-sync.json");
                await WaitAsync(() => File.Exists(last) && File.ReadAllText(last).Contains("\"success\": true"));
                var status = await install.CliAsync(home, "autosync", "status");
                Check(status.Exit == 0 && status.Output.Contains("active"), status.Output + status.Error);
                Console.WriteLine("PASS native scheduler executes a real synchronization job.");
            }
            finally
            {
                var disable = await install.CliAsync(home, "autosync", "disable");
                Check(disable.Exit == 0, disable.Output + disable.Error);
            }
        }
        File.WriteAllText(Path.Combine(home, "personal.txt"), "keep");
        var update = await install.CliAsync(home, "update", "--version", "v9.8.7");
        Check(update.Exit == 0, update.Output + update.Error);
        if (OperatingSystem.IsWindows())
        {
            var log = Path.Combine(home, ".skillsync/maintenance.log");
            await WaitAsync(() => File.Exists(log) && (File.ReadAllText(log).Contains("Completed") || File.ReadAllText(log).Contains("Failed:")));
            Check(File.ReadAllText(log).Contains("Completed"), "Deferred update failed: " + File.ReadAllText(log) + update.Output + update.Error);
        }
        var version = await install.CliAsync(home, "--version");
        Check(version.Exit == 0 && version.Output == originalVersion.Output && install.Requests == 4, "Updated executable does not match the downloaded release.");
        var remove = await install.CliAsync(home, "remove");
        Check(remove.Exit == 0, remove.Output + remove.Error);
        await WaitAsync(() => !File.Exists(install.Binary));
        Check(!Directory.Exists(Path.Combine(home, ".agents/skills/review")), "Complete removal kept managed skills.");
        Check(File.ReadAllText(Path.Combine(home, "personal.txt")) == "keep", "Removal deleted personal files.");
        Check(Directory.Exists(Path.Combine(home, ".skillsync/transactions")), "Removal deleted backups.");
        Check(Directory.GetFiles(install.InstallDir, ".skillsync-path-*.json").Length == 0, "Removal left PATH receipts.");
        if (!OperatingSystem.IsWindows()) Check(!File.ReadAllText(install.Profile).Contains("export PATH="), "Removal left its shell PATH line.");
        Console.WriteLine("PASS native install/update/remove lifecycle with a real standalone binary.");
    }

    private static async Task WaitAsync(Func<bool> ready)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        while (!ready()) await Task.Delay(200, timeout.Token);
    }
}
