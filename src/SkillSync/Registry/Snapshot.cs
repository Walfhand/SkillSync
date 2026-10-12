namespace SkillSync.Registry;

public sealed record SkillFile(string Path, byte[] Content, bool Executable);
public sealed record Snapshot(string Commit, Dictionary<string, List<SkillFile>> Skills,
    Dictionary<string, string>? Paths = null, RegistryPolicy? Policy = null);
