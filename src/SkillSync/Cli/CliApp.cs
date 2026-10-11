using System.CommandLine;
using SkillSync.Features;
using SkillSync.Installation;
using SkillSync.Storage;

namespace SkillSync.Cli;

public static class CliApp
{
    public static Task<int> RunAsync(string[] args, string home, TextWriter output, TextWriter error,
        CancellationToken cancellation = default)
    {
        var files = new LocalFiles(home);
        var states = new StateStore(files);
        var installer = new SkillInstaller(files, states);
        var console = new CommandOutput(output, error);
        var sync = new Sync(files, states, installer);
        var status = new Status(files, states, installer);
        var root = new RootCommand("Share your team's Agent Skills through Git.");
        root.Subcommands.Add(new Init(files, states, installer).CreateCommand(console));
        root.Subcommands.Add(sync.CreateCommand(console));
        root.Subcommands.Add(status.CreateCommand(console));
        root.Subcommands.Add(new Watch(sync, status).CreateCommand(console));
        root.Subcommands.Add(new Uninstall(files, states, installer).CreateCommand(console));
        return root.Parse(args).InvokeAsync(new InvocationConfiguration { Output = output, Error = error }, cancellation);
    }
}
