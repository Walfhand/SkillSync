using SkillSync.Agents;
using SkillSync.Registry;

namespace SkillSync.Storage;

public sealed class StateStore(LocalFiles files)
{
    public SyncState? Read()
    {
        var state = files.ReadJson<SyncState>(files.StatePath);
        if (state is null) return null;
        LocalFiles.ValidateId(state.TransactionId);
        RegistryConfig.Validate(state.Config);
        if (state.Skills is null || state.Commit is null) throw new InvalidDataException("Invalid state metadata.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var skill in state.Skills)
        {
            if (skill is null) throw new InvalidDataException("Invalid state entry.");
            AgentTargets.ValidateTarget(skill.Target);
            SkillValidator.ValidateName(skill.Name);
            if (skill.Hash is null || skill.Hash.Length != 64 || !skill.Hash.All(char.IsAsciiHexDigit) ||
                !keys.Add(skill.Target + "/" + skill.Name)) throw new InvalidDataException("Invalid or duplicate state entry.");
        }
        return state;
    }

    public SyncState Require() => Read() is { Uninstalled: false } state ? state
        : throw new InvalidOperationException("No registry configured. Run skillsync init first.");

    public void Write(SyncState state) => files.WriteJson(files.StatePath, state);
}
