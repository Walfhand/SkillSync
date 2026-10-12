using System.ComponentModel;
using System.Text.Json;
using SkillSync.Storage;
using YamlDotNet.Core;

namespace SkillSync.Cli;

public sealed class CommandOutput(TextWriter output, TextWriter error)
{
    public TextWriter Writer => output;

    public void Print(SyncState state) => output.WriteLine(
        $"Revision {state.Commit[..Math.Min(12, state.Commit.Length)]}: {state.Skills.Length} installed copies; synced {state.SyncedAt:O}.");

    public async Task<int> ExecuteAsync(Func<Task> action, CancellationToken cancellation)
    {
        try { await action(); return 0; }
        catch (OperationCanceledException)
        {
            if (cancellation.IsCancellationRequested) return 130;
            error.WriteLine("Synchronization timed out. Existing skills were kept.");
            return 1;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or
                                       ArgumentException or JsonException or YamlException or Win32Exception)
        {
            // Registry-controlled exception text must never become terminal escape sequences.
            error.WriteLine(new string(ex.Message.Select(c => char.IsControl(c) ? ' ' : c).ToArray()));
            return 1;
        }
    }
}
