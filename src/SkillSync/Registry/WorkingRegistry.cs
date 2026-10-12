using SkillSync.Storage;

namespace SkillSync.Registry;

public static class WorkingRegistry
{
    public static Snapshot Read(string directory)
    {
        var files = new LocalFiles(directory);
        if (!Directory.Exists(files.Home)) throw new DirectoryNotFoundException("Registry directory does not exist.");
        var root = files.At("skills");
        if (File.Exists(root)) throw new InvalidDataException("skills must be a directory.");
        var entries = new List<(string Path, string FullPath)>();
        var pending = new Stack<string>();
        if (Directory.Exists(root)) pending.Push(root);
        while (pending.TryPop(out var dir))
            foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
            {
                files.Guard(entry);
                var relative = Path.GetRelativePath(files.Home, entry).Replace('\\', '/');
                LocalFiles.ValidateRelative(relative);
                if (Directory.Exists(entry)) pending.Push(entry);
                else
                {
                    if (entries.Count >= 1000) throw new InvalidDataException("Registry exceeds 1000 files.");
                    entries.Add((relative, entry));
                }
            }
        var casing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var exact = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var parts = entry.Path.Split('/');
            for (var i = 1; i <= parts.Length; i++)
            {
                var prefix = string.Join('/', parts.Take(i));
                if (exact.Add(prefix) && !casing.Add(prefix)) throw new InvalidDataException("Registry paths collide on case-insensitive filesystems.");
            }
        }
        var roots = SkillCatalog.FindRoots(entries.Select(entry => entry.Path));
        var skills = roots.Values.ToDictionary(name => name, _ => new List<SkillFile>(), StringComparer.Ordinal);
        long total = 0;
        foreach (var entry in entries)
        {
            var size = new FileInfo(entry.FullPath).Length; total += size;
            if (size > LocalFiles.MaxFileBytes || total > LocalFiles.MaxTotalBytes) throw new InvalidDataException("Registry exceeds the size limit.");
            var owner = roots.Keys.FirstOrDefault(path => entry.Path.StartsWith(path + "/", StringComparison.Ordinal));
            if (owner is null)
            {
                if (!entry.Path.EndsWith("/README.md", StringComparison.Ordinal)) throw new InvalidDataException("Files must belong to a skill containing SKILL.md.");
                continue;
            }
            skills[roots[owner]].Add(new SkillFile(entry.Path[(owner.Length + 1)..], File.ReadAllBytes(entry.FullPath),
                !OperatingSystem.IsWindows() && (File.GetUnixFileMode(entry.FullPath) & UnixFileMode.UserExecute) != 0));
        }
        foreach (var (name, contents) in skills) SkillValidator.ValidateDocument(contents.Single(file => file.Path == "SKILL.md").Content, name);
        var policyPath = files.At("skillsync.json");
        RegistryPolicy? policy = null;
        if (File.Exists(policyPath))
        {
            if (new FileInfo(policyPath).Length > 1024 * 1024) throw new InvalidDataException("Registry policy exceeds 1 MiB.");
            policy = RegistryPolicy.Parse(File.ReadAllBytes(policyPath), skills.Keys);
        }
        return new Snapshot("working-tree", skills, roots.ToDictionary(pair => pair.Value, pair => pair.Key[7..]), policy);
    }
}
