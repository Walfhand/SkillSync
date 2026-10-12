using static SkillSync.Checks.Assertions;

namespace SkillSync.Checks.Installers;

internal static class InstallerChecks
{
    public static readonly (string Name, Func<Task> Run)[] All =
    [
        ("installer downloads the matching archive and installs its verified binary", async () =>
        {
            await using var fixture = new InstallerFixture();
            var result = await fixture.InstallAsync();
            Check(result.Exit == 0, result.Output + result.Error);
            Check(File.ReadAllBytes(fixture.Binary).SequenceEqual(fixture.Payload), "Installed bytes differ from the release.");
            Check(fixture.Requests == 2, "Archive and checksum were not both fetched.");
            if (!OperatingSystem.IsWindows())
                Check((File.GetUnixFileMode(fixture.Binary) & UnixFileMode.UserExecute) != 0, "Installed binary is not executable.");
        }),
        ("installer keeps the existing binary and profile when checksum verification fails", async () =>
        {
            await using var fixture = new InstallerFixture { BadChecksum = true };
            fixture.ExistingInstallation();
            var result = await fixture.InstallAsync();
            Check(result.Exit != 0 && (result.Error + result.Output).Contains("SHA", StringComparison.OrdinalIgnoreCase), "Corrupt download was accepted.");
            fixture.AssertPreviousKept();
        }),
        ("installer keeps the existing installation when the release download fails", async () =>
        {
            await using var fixture = new InstallerFixture { MissingAsset = true };
            fixture.ExistingInstallation();
            var result = await fixture.InstallAsync();
            Check(result.Exit != 0, "Missing download was accepted.");
            fixture.AssertPreviousKept();
        }),
        ("installer does not extract paths outside its staging directory", async () =>
        {
            await using var fixture = new InstallerFixture { MissingBinary = true };
            fixture.ExistingInstallation();
            var result = await fixture.InstallAsync();
            Check(result.Exit != 0, "Archive without the expected binary was accepted.");
            fixture.AssertPreviousKept();
            Check(!File.Exists(Path.Combine(fixture.Root, "escaped")), "Archive escaped staging.");
        }),
        ("installer rejects an unsafe version before any network request", async () =>
        {
            await using var fixture = new InstallerFixture();
            fixture.ExistingInstallation();
            var result = await fixture.InstallAsync("../other");
            Check(result.Exit != 0 && fixture.Requests == 0, "Unsafe version reached the network.");
            fixture.AssertPreviousKept();
        }),
        ("installer checks Git before downloading or changing an installation", async () =>
        {
            await using var fixture = new InstallerFixture();
            fixture.ExistingInstallation();
            var result = await fixture.InstallAsync(noGit: true);
            Check(result.Exit != 0 && fixture.Requests == 0, "Missing Git was not rejected before download.");
            Check((result.Error + result.Output).Contains("Git", StringComparison.OrdinalIgnoreCase), "Missing prerequisite was not explained.");
            fixture.AssertPreviousKept();
        }),
        ("installer PATH setup is idempotent and supports quoted install directories", async () =>
        {
            await using var fixture = new InstallerFixture();
            if (OperatingSystem.IsWindows())
            {
                Check(File.Exists(InstallerFixture.Script), "Installer script is missing: install.ps1");
                var checkScript = Path.Combine(fixture.Root, "check-path.ps1");
                var script = InstallerFixture.Script.Replace("'", "''");
                File.WriteAllText(checkScript,
                    ". '" + script + "'\n" +
                    "$existing = 'C:\\Windows;C:\\Tools'\n" +
                    "$updated = Get-SkillSyncPath $existing \"C:\\Apps With Spaces\\SkillSync\"\n" +
                    "if ($updated -ne 'C:\\Windows;C:\\Tools;C:\\Apps With Spaces\\SkillSync') { throw 'PATH entries were lost' }\n" +
                    "if ((Get-SkillSyncPath $updated 'c:\\apps with spaces\\skillsync\\') -ne $updated) { throw 'Duplicate PATH entry' }\n");
                var result = await fixture.RunAsync(["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", checkScript]);
                Check(result.Exit == 0, result.Output + result.Error);
            }
            else
            {
                File.WriteAllText(fixture.Profile, "export USER_VALUE='kept'\n");
                Check((await fixture.InstallAsync()).Exit == 0, "First installation failed.");
                var profile = File.ReadAllText(fixture.Profile);
                Check((await fixture.InstallAsync()).Exit == 0, "Repeated installation failed.");
                Check(File.ReadAllText(fixture.Profile) == profile, "Repeated installation duplicated PATH configuration.");
                Check(profile.StartsWith("export USER_VALUE='kept'\n", StringComparison.Ordinal), "Existing profile content was lost.");
                var result = await fixture.RunAsync(["--noprofile", "--norc", "-c", ". \"$1\"; skillsync --version", "check", fixture.Profile]);
                Check(result.Exit == 0 && result.Output.Trim() == "9.8.7", "Installed CLI cannot be called through the profile's PATH.");
            }
        }),
        ("installer supports the latest release URL", async () =>
        {
            await using var fixture = new InstallerFixture();
            var result = await fixture.InstallAsync("latest");
            Check(result.Exit == 0, result.Output + result.Error);
            Check(File.Exists(fixture.Binary), "Latest release was not installed.");
        }),
        ("installer records PATH ownership for complete removal and preserves it on repeated installs", async () =>
        {
            await using var fixture = new InstallerFixture();
            Check((await fixture.InstallAsync()).Exit == 0, "Installation failed.");
            var receipts = Directory.GetFiles(fixture.InstallDir, ".skillsync-path-*.json");
            Check(receipts.Length == 1, "Installer did not record PATH ownership.");
            var first = File.ReadAllText(receipts[0]);
            using var receipt = System.Text.Json.JsonDocument.Parse(first);
            Check(receipt.RootElement.GetProperty("directory").GetString() == fixture.InstallDir, "Receipt points at another installation.");
            if (!OperatingSystem.IsWindows())
            {
                Check(receipt.RootElement.GetProperty("profile").GetString() == fixture.Profile, "Receipt lost shell profile.");
                Check(receipt.RootElement.GetProperty("pathAdded").GetBoolean(), "Installer did not own its new PATH entry.");
            }
            var repeated = await fixture.InstallAsync();
            Check(repeated.Exit == 0, repeated.Output + repeated.Error);
            Check(File.ReadAllText(receipts[0]) == first, "Repeated install lost PATH ownership.");
        }),
        ("Unix installer recognizes its PATH marker from releases predating receipts", async () =>
        {
            if (OperatingSystem.IsWindows()) return;
            await using var fixture = new InstallerFixture();
            var quote = "'" + fixture.InstallDir.Replace("'", "'\\''") + "'";
            var line = $"case \":$PATH:\" in *:{quote}:*) ;; *) export PATH={quote}:\"$PATH\" ;; esac";
            File.WriteAllText(fixture.Profile, "\n# SkillSync\n" + line + "\n");
            Check((await fixture.InstallAsync()).Exit == 0, "Migration installation failed.");
            using var receipt = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Directory.GetFiles(fixture.InstallDir, ".skillsync-path-*.json").Single()));
            Check(receipt.RootElement.GetProperty("pathAdded").GetBoolean(), "Previous SkillSync PATH marker was not adopted.");
        })
    ];
}
