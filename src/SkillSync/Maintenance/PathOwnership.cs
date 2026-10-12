using System.Text.Json;
using SkillSync.Storage;

namespace SkillSync.Maintenance;

public sealed record InstallReceipt(string Directory, string? Profile, string? PathLine, bool PathAdded);

public static class PathOwnership
{
    public static (string Path, InstallReceipt Receipt)[] Read(string executable)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(executable))!;
        var comparer = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var files = new LocalFiles(directory);
        return System.IO.Directory.GetFiles(directory, ".skillsync-path-*.json").Select(path =>
        {
            var receipt = files.ReadJson<InstallReceipt>(path) ?? throw new InvalidDataException("Missing installation receipt.");
            if (!string.Equals(directory, receipt.Directory, comparer)) throw new InvalidDataException("Installation receipt belongs to another directory.");
            if (receipt.Profile is not null)
            {
                var quote = "'" + directory.Replace("'", "'\\''") + "'";
                var expected = $"case \":$PATH:\" in *:{quote}:*) ;; *) export PATH={quote}:\"$PATH\" ;; esac";
                if (!Path.IsPathFullyQualified(receipt.Profile) || receipt.Profile.Any(char.IsControl) || receipt.PathLine != expected)
                    throw new InvalidDataException("Invalid shell PATH receipt.");
                if (File.Exists(receipt.Profile) && new FileInfo(receipt.Profile).Length > 5 * 1024 * 1024)
                    throw new InvalidDataException("Shell profile exceeds 5 MiB.");
            }
            else if (!OperatingSystem.IsWindows() || receipt.PathLine is not null)
                throw new InvalidDataException("Invalid Windows PATH receipt.");
            return (path, receipt);
        }).ToArray();
    }

    public static void Remove(string executable)
    {
        var receipts = Read(executable);
        foreach (var (path, receipt) in receipts)
        {
            if (receipt.PathAdded)
            {
                if (receipt.Profile is not null && File.Exists(receipt.Profile))
                {
                    var text = File.ReadAllText(receipt.Profile);
                    File.WriteAllText(receipt.Profile, text.Replace("\n# SkillSync\n" + receipt.PathLine + "\n", "\n", StringComparison.Ordinal));
                }
                else if (OperatingSystem.IsWindows())
                {
                    var current = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? "";
                    Environment.SetEnvironmentVariable("Path", RemoveWindowsEntry(current, receipt.Directory), EnvironmentVariableTarget.User);
                }
            }
            File.Delete(path);
        }
    }

    public static string RemoveWindowsEntry(string value, string directory) => string.Join(';',
        value.Split(';').Where(entry => !entry.TrimEnd('\\').Equals(directory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)));
}
