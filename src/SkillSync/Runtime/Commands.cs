using System.Diagnostics;

namespace SkillSync.Runtime;

public sealed record CommandResult(int ExitCode, string Output, string Error);

public static class Commands
{
    public static async Task<CommandResult> RunAsync(string executable, string[] arguments, string? directory, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true, WorkingDirectory = directory ?? Environment.CurrentDirectory
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException($"Could not start {executable}.");
        try
        {
            var output = ReadAsync(process.StandardOutput, timeout.Token);
            var error = ReadAsync(process.StandardError, timeout.Token);
            await Task.WhenAll(output, error, process.WaitForExitAsync(timeout.Token));
            return new CommandResult(process.ExitCode, await output, await error);
        }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
    }

    private static async Task<string> ReadAsync(StreamReader reader, CancellationToken cancellation)
    {
        var text = new System.Text.StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer, cancellation)) > 0)
        {
            if (text.Length + count > 1024 * 1024) throw new IOException("Command output exceeds 1 MiB.");
            text.Append(buffer, 0, count);
        }
        return text.ToString();
    }
}
