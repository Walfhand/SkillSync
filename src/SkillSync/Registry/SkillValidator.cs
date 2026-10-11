using System.Text;
using System.Text.RegularExpressions;
using SkillSync.Storage;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace SkillSync.Registry;

public static class SkillValidator
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static void ValidateName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 64 ||
            !Regex.IsMatch(name, "^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("Skill names must be lowercase slugs of at most 64 characters.");
        LocalFiles.ValidateRelative(name);
    }

    public static void ValidateDocument(byte[] bytes, string name)
    {
        if (bytes.Length > 128 * 1024) throw new InvalidDataException("SKILL.md exceeds 128 KiB; move long references into separate files.");
        using var reader = new StringReader(Utf8.GetString(bytes).TrimStart('\uFEFF'));
        if (reader.ReadLine() != "---") throw new InvalidDataException($"Skill '{name}' needs YAML frontmatter.");
        var frontmatter = new StringBuilder();
        var closed = false;
        while (reader.ReadLine() is { } line)
        {
            if (line == "---") { closed = true; break; }
            frontmatter.AppendLine(line);
        }
        if (!closed) throw new InvalidDataException("Skill frontmatter is not closed.");
        var parser = new Parser(new StringReader(frontmatter.ToString()));
        var depth = 0;
        var count = 0;
        while (parser.MoveNext())
        {
            if (parser.Current is MappingStart or SequenceStart) depth++;
            if (parser.Current is MappingEnd or SequenceEnd) depth--;
            if (depth > 32 || ++count > 10000 || parser.Current is AnchorAlias)
                throw new InvalidDataException("Skill frontmatter is too complex; YAML aliases are unsupported.");
        }
        var yaml = new YamlStream();
        yaml.Load(new StringReader(frontmatter.ToString()));
        if (yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not YamlMappingNode mapping ||
            !mapping.Children.TryGetValue(new YamlScalarNode("name"), out var nameNode) ||
            nameNode is not YamlScalarNode { Value: { } actualName } || actualName != name ||
            !mapping.Children.TryGetValue(new YamlScalarNode("description"), out var descriptionNode) ||
            descriptionNode is not YamlScalarNode { Value: { } description } ||
            string.IsNullOrWhiteSpace(description) || description.Length > 1024)
            throw new InvalidDataException($"Skill '{name}' needs a matching name and a non-empty description (up to 1024 characters).");
    }

}
