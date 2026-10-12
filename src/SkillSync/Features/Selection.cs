using System.CommandLine;
using SkillSync.Cli;
using SkillSync.Installation;
using SkillSync.Registry;
using SkillSync.Storage;

namespace SkillSync.Features;

public sealed class Selection(LocalFiles files, StateStore states, SkillInstaller installer)
{
    public Command ListCommand(CommandOutput output)
    {
        var command = new Command("list", "List all registry skills, categories and team rules.");
        command.SetAction(async (_, token) => await output.ExecuteAsync(async () =>
        {
            using var runLock = files.Lock();
            installer.Recover();
            var state = states.Require();
            var snapshot = await GitRegistry.ReadAsync(state.Config, files, token);
            var selected = SkillSelection.Apply(snapshot, state.Config);
            var rules = SkillSelection.Rules(snapshot, state.Config);
            foreach (var name in snapshot.Skills.Keys.Order(StringComparer.Ordinal))
            {
                var rule = rules.Required.Contains(name) ? "required" : rules.Blocked.Contains(name) ? "blocked" :
                    rules.Recommended.Contains(name) ? "recommended" : "optional";
                output.Writer.WriteLine($"{snapshot.Paths![name]}: {rule}, {(selected.Skills.ContainsKey(name) ? "selected" : "unselected")}");
            }
        }, token));
        return command;
    }

    public Command SelectCommand(CommandOutput output)
    {
        var skills = new Option<string[]>("--skill") { AllowMultipleArgumentsPerToken = true };
        var categories = new Option<string[]>("--category") { AllowMultipleArgumentsPerToken = true };
        var all = new Option<bool>("--all");
        var revision = new Option<string?>("--revision");
        var follow = new Option<bool>("--follow-branch");
        var team = new Option<string?>("--team");
        var command = new Command("select", "Update the persisted selection, team or revision and synchronize.") { skills, categories, all, revision, follow, team };
        command.SetAction(async (parse, token) => await output.ExecuteAsync(async () =>
        {
            var named = parse.GetValue(skills) ?? [];
            var groups = parse.GetValue(categories) ?? [];
            var replace = named.Length > 0 || groups.Length > 0 || parse.GetValue(all);
            if (!replace && parse.GetValue(revision) is null && !parse.GetValue(follow) && parse.GetValue(team) is null)
                throw new ArgumentException("Choose --all, --skill/--category, --team, --revision or --follow-branch.");
            if (parse.GetValue(all) && (named.Length > 0 || groups.Length > 0) ||
                parse.GetValue(follow) && parse.GetValue(revision) is not null)
                throw new ArgumentException("Conflicting selection options.");
            using var runLock = files.Lock();
            installer.Recover();
            var previous = states.Require();
            var config = previous.Config with
            {
                Skills = replace ? named : previous.Config.Skills,
                Categories = replace ? groups : previous.Config.Categories,
                Revision = parse.GetValue(follow) ? null : parse.GetValue(revision) ?? previous.Config.Revision,
                Team = parse.GetValue(team) ?? previous.Config.Team
            };
            var snapshot = await GitRegistry.ReadAsync(config, files, token);
            output.Print(installer.Apply(config, previous, SkillSelection.Apply(snapshot, config)));
        }, token));
        return command;
    }
}
