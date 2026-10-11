using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SkillSync.Storage;

public sealed class LocalFiles(string home)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32
    };
    public const int MaxFileBytes = 5 * 1024 * 1024;
    public const int MaxTotalBytes = 25 * 1024 * 1024;
    public string Home { get; } = Path.GetFullPath(home);
    public string Root => At(".skillsync");
    public string StatePath => At(".skillsync/state.json");
    public string JournalPath => At(".skillsync/journal.json");

    public static void ValidateRelative(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 1024 || path.Any(char.IsControl) ||
            path.IndexOfAny(['\\', ':', '<', '>', '"', '|', '?', '*']) >= 0)
            throw new InvalidDataException("Registry contains a non-portable or unsafe path.");
        foreach (var part in path.Split('/'))
        {
            var stem = part.Split('.')[0];
            if (part is "" or "." or ".." || part.Length > 255 ||
                part.EndsWith('.') || part != part.Trim() ||
                part.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                Regex.IsMatch(stem, "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                throw new InvalidDataException("Registry contains a non-portable or unsafe path.");
        }
    }

    public string At(string relative)
    {
        ValidateRelative(relative);
        var path = Path.Combine(Home, relative.Replace('/', Path.DirectorySeparatorChar));
        Guard(path);
        return path;
    }

    public void Guard(string path)
    {
        var relative = Path.GetRelativePath(Home, Path.GetFullPath(path));
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar) || Path.IsPathRooted(relative))
            throw new InvalidDataException("Path escapes the installation home.");
        var cursor = Home;
        CheckLink(cursor);
        foreach (var part in relative.Split(Path.DirectorySeparatorChar))
        {
            cursor = Path.Combine(cursor, part);
            CheckLink(cursor);
        }
    }

    private static void CheckLink(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Symbolic links and reparse points are refused in managed paths.");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }

    public FileStream Lock()
    {
        Directory.CreateDirectory(Root);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try { return new FileStream(At(".skillsync/run.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) { throw new IOException("Another SkillSync command may be running. Retry when it finishes.", ex); }
    }

    public T? ReadJson<T>(string path)
    {
        Guard(path);
        if (!File.Exists(path)) return default;
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("Local metadata exceeds 1 MiB.");
        return JsonSerializer.Deserialize<T>(File.ReadAllBytes(path), Json)
            ?? throw new InvalidDataException("Local metadata is empty.");
    }

    public void WriteJson<T>(string path, T value)
    {
        Guard(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                JsonSerializer.Serialize(stream, value, Json);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public string HashDirectory(string path)
    {
        Guard(path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var entries = new List<string>();
        var pending = new Stack<string>();
        pending.Push(path);
        while (pending.TryPop(out var directory))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                Guard(entry);
                if (entries.Count >= 2000) throw new InvalidDataException("Skill has too many local entries.");
                entries.Add(entry);
                if (Directory.Exists(entry)) pending.Push(entry);
            }
        }
        long total = 0;
        foreach (var entry in entries.OrderBy(p => Path.GetRelativePath(path, p).Replace('\\', '/'), StringComparer.Ordinal))
        {
            var directory = Directory.Exists(entry);
            var relative = Path.GetRelativePath(path, entry).Replace('\\', '/');
            hash.AppendData(Encoding.UTF8.GetBytes(relative + (directory ? "/\0" : "\0")));
            if (directory) continue;
            var length = new FileInfo(entry).Length;
            total += length;
            if (length > MaxFileBytes || total > MaxTotalBytes) throw new InvalidDataException("Skill exceeds the size limit.");
            hash.AppendData(BitConverter.GetBytes(length));
            hash.AppendData(File.ReadAllBytes(entry));
            if (!OperatingSystem.IsWindows()) hash.AppendData(BitConverter.GetBytes((int)File.GetUnixFileMode(entry)));
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    public static void ValidateId(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Invalid transaction identifier.");
    }
}
