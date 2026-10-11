using System.CommandLine;
using SkillSync.Agents;
using SkillSync.Cli;
using SkillSync.Installation;
using SkillSync.Registry;
using SkillSync.Storage;

namespace SkillSync.Features;

public sealed class Init(LocalFiles files, StateStore states, SkillInstaller installer)
{
    public async Task<SyncState> ExecuteAsync(RegistryConfig config, CancellationToken cancellation = default)
    {
        RegistryConfig.Validate(config);
        using var runLock = files.Lock();
        installer.Recover();
        var previous = states.Read();
        if (previous is { Uninstalled: false } &&
            (previous.Config.Source != config.Source || previous.Config.Branch != config.Branch ||
             !previous.Config.Agents.SequenceEqual(config.Agents)))
            throw new InvalidOperationException("Already connected to a different configuration. Run uninstall before switching registries or agents.");
        var snapshot = await GitRegistry.ReadAsync(config, files, cancellation);
        return installer.Apply(config, previous, snapshot);
    }

    public Command CreateCommand(CommandOutput output)
    {
        var source = new Argument<string>("registry") { Description = "Git HTTPS/SSH URL or local repository path." };
        var branch = new Option<string>("--branch") { DefaultValueFactory = _ => "main", Description = "Registry branch to follow." };
        var agents = new Option<string[]>("--agent")
        {
            DefaultValueFactory = _ => AgentTargets.Names,
            AllowMultipleArgumentsPerToken = true,
            Description = "claude-code, codex, cursor. Defaults to all three."
        };
        var init = new Command("init", "Connect to a registry and install its skills.") { source, branch, agents };
        init.SetAction(async (parse, token) => await output.ExecuteAsync(async () =>
        {
            var address = parse.GetValue(source)!;
            if (Directory.Exists(address)) address = Path.GetFullPath(address);
            output.Print(await ExecuteAsync(new RegistryConfig(address, parse.GetValue(branch)!, parse.GetValue(agents)!), token));
        }, token));
        return init;
    }
}
