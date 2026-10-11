namespace SkillSync.Registry;

public static class SkillCatalog
{
    public static Dictionary<string, string> FindRoots(IEnumerable<string> paths)
    {
        var roots = new Dictionary<string, string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in paths.Where(path => path.EndsWith("/SKILL.md", StringComparison.Ordinal))
                     .Select(path => path[..path.LastIndexOf('/')]).OrderBy(path => path.Length).ThenBy(path => path, StringComparer.Ordinal))
        {
            if (root == "skills") throw new InvalidDataException("Put each skill in its own folder below skills/.");
            // A nested SKILL.md can be a template bundled in an already discovered skill.
            if (roots.Keys.Any(parent => root.StartsWith(parent + "/", StringComparison.Ordinal))) continue;
            var name = root[(root.LastIndexOf('/') + 1)..];
            SkillValidator.ValidateName(name);
            if (!names.Add(name))
                throw new InvalidDataException($"Skill name '{name}' appears in multiple folders. Names must be unique across the registry.");
            roots.Add(root, name);
        }
        return roots;
    }
}
