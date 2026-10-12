using System.Text.RegularExpressions;
using SkillSync.Agents;

namespace SkillSync.Registry;

public sealed record RegistryConfig(string Source, string Branch, string[] Agents,
    string[]? Skills = null, string[]? Categories = null, string? Team = null, string? Revision = null)
{
    public static void Validate(RegistryConfig config)
    {
        if (config is null || config.Agents is null || config.Agents.Length == 0 ||
            config.Agents.Distinct(StringComparer.Ordinal).Count() != config.Agents.Length ||
            string.IsNullOrEmpty(config.Branch)) throw new InvalidDataException("Invalid registry configuration.");
        ValidateSource(config.Source);
        foreach (var agent in config.Agents) AgentTargets.Target(agent);
        foreach (var skill in config.Skills ?? []) SkillValidator.ValidateName(skill);
        foreach (var category in config.Categories ?? []) Storage.LocalFiles.ValidateRelative(category);
        if (config.Team is not null) SkillValidator.ValidateName(config.Team);
        if (config.Revision is not null && !Regex.IsMatch(config.Revision, "^(?:[a-f0-9]{40}|[a-f0-9]{64})$"))
            throw new InvalidDataException("Revision must be a full lowercase Git commit hash.");
    }

    public static void ValidateSource(string source)
    {
        if (string.IsNullOrWhiteSpace(source) || source.Any(char.IsControl) || source.StartsWith('-'))
            throw new InvalidDataException("Provide a Git URL or an absolute local repository path.");
        if (Path.IsPathFullyQualified(source) && !source.Contains("://", StringComparison.Ordinal)) return;
        if (Regex.IsMatch(source, "^[a-zA-Z0-9._-]+@[a-zA-Z0-9.-]+:[a-zA-Z0-9_./-]+$")) return;
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "ssh") ||
            uri.Host.Length == 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            (uri.Scheme == "https" && uri.UserInfo.Length != 0) || uri.UserInfo.Contains(':'))
            throw new InvalidDataException("Use HTTPS without embedded credentials, SSH, or an absolute local Git path.");
    }

}
