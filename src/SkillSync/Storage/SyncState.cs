using SkillSync.Registry;

namespace SkillSync.Storage;

public sealed record InstalledSkill(string Target, string Name, string Hash);
public sealed record SyncState(string TransactionId, RegistryConfig Config, string Commit,
    DateTimeOffset SyncedAt, InstalledSkill[] Skills, bool Uninstalled = false);
