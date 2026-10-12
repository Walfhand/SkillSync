using System.Text.Json;
using SkillSync.Storage;

namespace SkillSync.Registry;

public sealed record TeamRules(string[] Required, string[] Recommended, string[] Blocked);
public sealed record RegistryPolicy(Dictionary<string, TeamRules> Teams)
{
    public static RegistryPolicy Parse(byte[] bytes, IEnumerable<string> skills)
    {
        if (bytes.Length > 1024 * 1024) throw new InvalidDataException("Registry policy exceeds 1 MiB.");
        var policy = JsonSerializer.Deserialize<RegistryPolicy>(bytes, LocalFiles.Json);
        if (policy?.Teams is null) throw new InvalidDataException("Invalid registry policy.");
        var names = skills.ToHashSet(StringComparer.Ordinal);
        foreach (var (team, rules) in policy.Teams)
        {
            SkillValidator.ValidateName(team);
            if (rules?.Required is null || rules.Recommended is null || rules.Blocked is null)
                throw new InvalidDataException("Registry policy needs required, recommended and blocked arrays.");
            var all = rules.Required.Concat(rules.Recommended).Concat(rules.Blocked).ToArray();
            if (all.Any(name => !names.Contains(name)) || all.Distinct(StringComparer.Ordinal).Count() != all.Length)
                throw new InvalidDataException("Registry policy references missing, duplicate or conflicting skills.");
        }
        return policy;
    }
}
