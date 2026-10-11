namespace SkillSync.Checks;

internal static class Assertions
{
    public static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static async Task Reject(Func<Task> action, string message)
    {
        try { await action(); }
        catch (Exception ex) when (ex.Message.Contains(message, StringComparison.OrdinalIgnoreCase)) { return; }
        throw new InvalidOperationException("Expected rejection containing: " + message);
    }
}
