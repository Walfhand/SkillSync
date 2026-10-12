using System.CommandLine;
using System.Text;
using System.Text.Json;
using SkillSync.Cli;
using SkillSync.Registry;
using SkillSync.Storage;

namespace SkillSync.Features;

public sealed class Authoring
{
    public Command NewCommand(CommandOutput output)
    {
        var name = new Argument<string>("name");
        var repo = new Option<string>("--repo") { DefaultValueFactory = _ => Environment.CurrentDirectory };
        var category = new Option<string?>("--category");
        var description = new Option<string>("--description") { DefaultValueFactory = _ => "Describe when to use this skill." };
        var command = new Command("new", "Create a skill folder in a local registry.") { name, repo, category, description };
        command.SetAction(async (parse, token) => await output.ExecuteAsync(() =>
        {
            var skill = parse.GetValue(name)!; SkillValidator.ValidateName(skill);
            var group = parse.GetValue(category);
            if (group is not null) LocalFiles.ValidateRelative(group);
            var root = new LocalFiles(parse.GetValue(repo)!);
            var current = WorkingRegistry.Read(root.Home);
            if (current.Skills.ContainsKey(skill)) throw new InvalidOperationException("Skill already exists.");
            var relative = "skills/" + (group is null ? "" : group + "/") + skill;
            var destination = root.At(relative);
            var ancestor = Path.GetDirectoryName(destination)!;
            while (ancestor != root.Home)
            {
                if (File.Exists(Path.Combine(ancestor, "SKILL.md"))) throw new InvalidDataException("Cannot create a skill inside an existing skill.");
                ancestor = Path.GetDirectoryName(ancestor)!;
            }
            if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Destination already exists.");
            var document = Encoding.UTF8.GetBytes($"---\nname: {skill}\ndescription: {JsonSerializer.Serialize(parse.GetValue(description))}\n---\n\n# {skill}\n\nDescribe the workflow here. Reference scripts/, references/ and assets/ when needed.\n");
            SkillValidator.ValidateDocument(document, skill);
            Directory.CreateDirectory(destination);
            File.WriteAllBytes(Path.Combine(destination, "SKILL.md"), document);
            output.Writer.WriteLine(destination);
            return Task.CompletedTask;
        }, token));
        return command;
    }

    public Command ValidateCommand(CommandOutput output)
    {
        var repo = new Option<string>("--repo") { DefaultValueFactory = _ => Environment.CurrentDirectory };
        var command = new Command("validate", "Validate local skills and policy, including uncommitted files.") { repo };
        command.SetAction(async (parse, token) => await output.ExecuteAsync(() =>
        {
            var snapshot = WorkingRegistry.Read(parse.GetValue(repo)!);
            output.Writer.WriteLine($"Valid registry: {snapshot.Skills.Count} skills.");
            return Task.CompletedTask;
        }, token));
        return command;
    }
}
