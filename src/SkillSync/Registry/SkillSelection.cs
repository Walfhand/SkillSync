namespace SkillSync.Registry;

public static class SkillSelection
{
    public static TeamRules Rules(Snapshot snapshot, RegistryConfig config) =>
        config.Team is null ? new TeamRules([], [], []) :
        snapshot.Policy?.Teams.TryGetValue(config.Team, out var rules) == true ? rules :
        throw new InvalidDataException("Unknown team in registry policy.");

    public static Snapshot Apply(Snapshot snapshot, RegistryConfig config)
    {
        RegistryConfig.Validate(config);
        var rules = Rules(snapshot, config);
        var named = config.Skills ?? [];
        var categories = config.Categories ?? [];
        var paths = snapshot.Paths ?? snapshot.Skills.Keys.ToDictionary(name => name, name => name);
        foreach (var name in named)
            if (!snapshot.Skills.ContainsKey(name) || rules.Blocked.Contains(name))
                throw new InvalidDataException($"Unknown or blocked skill: {name}");
        foreach (var category in categories)
            if (!paths.Values.Any(path => path.StartsWith(category + "/", StringComparison.Ordinal)))
                throw new InvalidDataException($"Unknown category: {category}");
        var all = named.Length == 0 && categories.Length == 0;
        var selected = snapshot.Skills.Where(skill => !rules.Blocked.Contains(skill.Key) &&
            (all || named.Contains(skill.Key) || rules.Required.Contains(skill.Key) ||
             categories.Any(category => paths[skill.Key].StartsWith(category + "/", StringComparison.Ordinal))))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        return snapshot with { Skills = selected };
    }
}
