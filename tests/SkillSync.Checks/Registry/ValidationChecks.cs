using SkillSync.Registry;
using SkillSync.Storage;
using static SkillSync.Checks.Assertions;

namespace SkillSync.Checks.Registry;

internal static class ValidationChecks
{
    public static readonly (string Name, Func<Task> Run)[] All =
    [
        ("credentials, traversal and unsupported YAML aliases are refused", async () =>
            {
                await Reject(() => { RegistryConfig.ValidateSource("https://user:secret@example.com/skills.git"); return Task.CompletedTask; }, "without embedded credentials");
                await Reject(() => { LocalFiles.ValidateRelative("references/../../escape"); return Task.CompletedTask; }, "unsafe path");
                await Reject(() => { LocalFiles.ValidateRelative("/absolute"); return Task.CompletedTask; }, "unsafe path");
                await Reject(() => { SkillValidator.ValidateDocument(System.Text.Encoding.UTF8.GetBytes("---\nname: review\ndescription: &a hello\nother: *a\n---\nBody"), "review"); return Task.CompletedTask; }, "aliases");
            }),
    ];
}
