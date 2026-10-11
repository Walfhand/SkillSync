namespace SkillSync.Agents;

public static class AgentTargets
{
    private static readonly Dictionary<string, string> Targets = new(StringComparer.Ordinal)
    {
        ["claude-code"] = ".claude/skills",
        ["codex"] = ".agents/skills",
        ["cursor"] = ".agents/skills"
    };

    public static string[] Names => Targets.Keys.ToArray();

    public static string Target(string agent) => Targets.TryGetValue(agent, out var target) ? target
        : throw new InvalidDataException("Unknown agent. Choose claude-code, codex or cursor.");

    public static void ValidateTarget(string target)
    {
        if (!Targets.Values.Contains(target, StringComparer.Ordinal))
            throw new InvalidDataException("Invalid installation target in metadata.");
    }
}
