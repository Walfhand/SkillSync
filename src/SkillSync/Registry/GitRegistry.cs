using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using SkillSync.Storage;

namespace SkillSync.Registry;

public static class GitRegistry
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static async Task<Snapshot> ReadAsync(RegistryConfig config, LocalFiles files, CancellationToken cancellation)
    {
        RegistryConfig.Validate(config);
        if (string.IsNullOrEmpty(config.Branch) || config.Branch.Any(char.IsControl))
            throw new InvalidDataException("Provide a valid Git branch.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        var cache = files.At(".skillsync/git");
        var hooks = files.At(".skillsync/empty-hooks");
        Directory.CreateDirectory(hooks);
        async Task<byte[]> Git(params string[] arguments) => await RunAsync(
            ["-c", $"core.hooksPath={hooks}", "-c", "core.fsmonitor=false", "-c", "protocol.allow=never",
             "-c", "protocol.https.allow=always", "-c", "protocol.ssh.allow=always", "-c", "protocol.file.allow=always",
             "-c", "credential.interactive=false", .. arguments], token);

        await Git("check-ref-format", "refs/heads/" + config.Branch);
        if (!Directory.Exists(cache)) await Git("init", "--bare", "--quiet", cache);
        var commit = config.Revision;
        if (commit is null)
        {
        var expectedRef = "refs/heads/" + config.Branch;
        var refs = Utf8.GetString(await Git("ls-remote", "--exit-code", "--refs", "--", config.Source, expectedRef))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (refs.Length != 1) throw new InvalidDataException("The registry branch did not resolve uniquely.");
        var parts = refs[0].TrimEnd('\r').Split('\t');
        if (parts.Length != 2 || parts[1] != expectedRef || !Regex.IsMatch(parts[0], "^(?:[a-f0-9]{40}|[a-f0-9]{64})$"))
            throw new InvalidDataException("Git returned an unexpected registry reference.");
        commit = parts[0];
        }
        await Git("--git-dir=" + cache, "fetch", "--quiet", "--no-tags", "--no-recurse-submodules", "--depth=1", "--", config.Source, commit);
        var tree = Utf8.GetString(await Git("--git-dir=" + cache, "ls-tree", "-r", "-z", commit));
        var skills = new Dictionary<string, List<SkillFile>>(StringComparer.Ordinal);
        var entries = new List<(string Path, string ObjectId, bool Executable)>();
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in tree.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = entry.IndexOf('\t');
            if (tab < 0) throw new InvalidDataException("Git returned an invalid tree entry.");
            var path = entry[(tab + 1)..];
            if (path == "skills") throw new InvalidDataException("Symlinks and submodules are refused at the skills root.");
            if (!path.StartsWith("skills/", StringComparison.Ordinal)) continue;
            LocalFiles.ValidateRelative(path);
            var segments = path.Split('/');
            var metadata = entry[..tab].Split(' ');
            if (metadata.Length != 3 || metadata[1] != "blob" || metadata[0] is not ("100644" or "100755"))
                throw new InvalidDataException("Symlinks and submodules are refused inside skills.");
            for (var n = 1; n <= segments.Length; n++)
            {
                var prefix = string.Join('/', segments.Take(n));
                if (paths.TryGetValue(prefix, out var existing) && existing != prefix)
                    throw new InvalidDataException("Registry paths collide on case-insensitive filesystems.");
                paths[prefix] = prefix;
            }
            if (entries.Count >= 1000) throw new InvalidDataException("Registry exceeds 1000 files under skills/.");
            entries.Add((path, metadata[2], metadata[0] == "100755"));
        }
        var roots = SkillCatalog.FindRoots(entries.Select(entry => entry.Path));
        foreach (var name in roots.Values) skills.Add(name, []);
        long total = 0;
        foreach (var entry in entries)
        {
            var owner = roots.Keys.FirstOrDefault(root => entry.Path.StartsWith(root + "/", StringComparison.Ordinal));
            if (owner is null && !entry.Path.EndsWith("/README.md", StringComparison.Ordinal))
                throw new InvalidDataException("Files must belong to a skill folder containing SKILL.md; categories may also contain README.md.");
            var sizeText = Utf8.GetString(await Git("--git-dir=" + cache, "cat-file", "-s", entry.ObjectId)).Trim();
            if (!long.TryParse(sizeText, out var size) || size < 0 || size > LocalFiles.MaxFileBytes || total + size > LocalFiles.MaxTotalBytes)
                throw new InvalidDataException("Registry exceeds the file or total size limit.");
            total += size;
            if (owner is null) continue;
            var content = await Git("--git-dir=" + cache, "cat-file", "blob", entry.ObjectId);
            if (content.LongLength != size) throw new InvalidDataException("Git blob size changed unexpectedly.");
            skills[roots[owner]].Add(new SkillFile(entry.Path[(owner.Length + 1)..], content, entry.Executable));
        }
        foreach (var (name, contents) in skills)
        {
            var document = contents.SingleOrDefault(f => f.Path == "SKILL.md")
                ?? throw new InvalidDataException($"Skill '{name}' needs a SKILL.md file.");
            SkillValidator.ValidateDocument(document.Content, name);
        }
        RegistryPolicy? policy = null;
        var policyEntry = tree.Split('\0').SingleOrDefault(entry => entry.EndsWith("\tskillsync.json", StringComparison.Ordinal));
        if (policyEntry is not null)
        {
            var metadata = policyEntry[..policyEntry.IndexOf('\t')].Split(' ');
            if (metadata.Length != 3 || metadata[0] != "100644" || metadata[1] != "blob")
                throw new InvalidDataException("Registry policy must be a regular file.");
            var size = Utf8.GetString(await Git("--git-dir=" + cache, "cat-file", "-s", metadata[2])).Trim();
            if (!int.TryParse(size, out var length) || length > 1024 * 1024)
                throw new InvalidDataException("Registry policy exceeds 1 MiB.");
            policy = RegistryPolicy.Parse(await Git("--git-dir=" + cache, "cat-file", "blob", metadata[2]), skills.Keys);
        }
        return new Snapshot(commit, skills, roots.ToDictionary(pair => pair.Value, pair => pair.Key[7..]), policy);
    }

    public static async Task<byte[]> RunAsync(string[] arguments, CancellationToken cancellation, string? directory = null)
    {
        var start = new ProcessStartInfo("git")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = directory ?? Environment.CurrentDirectory
        };
        foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray())
            start.Environment.Remove(key);
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Could not start Git. Install Git and retry.");
        try
        {
            var output = ReadLimitedAsync(process.StandardOutput.BaseStream, LocalFiles.MaxTotalBytes + 1024 * 1024, cancellation);
            var error = ReadLimitedAsync(process.StandardError.BaseStream, 1024 * 1024, cancellation);
            await Task.WhenAll(output, error, process.WaitForExitAsync(cancellation));
            if (process.ExitCode != 0) throw new IOException($"Git failed (exit {process.ExitCode}). Check the registry, branch and your Git credentials.");
            return await output;
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
    }

    private static async Task<byte[]> ReadLimitedAsync(Stream stream, int limit, CancellationToken cancellation)
    {
        using var result = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellation)) > 0)
        {
            if (result.Length + read > limit) throw new InvalidDataException("Git output exceeds the size limit.");
            result.Write(buffer, 0, read);
        }
        return result.ToArray();
    }
}
