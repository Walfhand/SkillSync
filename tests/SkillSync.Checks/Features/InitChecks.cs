using static SkillSync.Checks.Assertions;

namespace SkillSync.Checks.Features;

internal static class InitChecks
{
    public static readonly (string Name, Func<Task> Run)[] All =
    [
        ("nested categories and flat skills install individually with their resources", async () =>
            {
                using var fixture = new Fixture();
                fixture.Skill("ddd-review", "domain checks", "architecture/ddd");
                fixture.Skill("solid-review", "SOLID checks", "architecture");
                fixture.Skill("pr-review", "pull request checks", "pull request");
                fixture.Skill("commit", "flat skill");
                var directory = System.IO.Path.Combine(fixture.Repo, "skills/architecture/ddd/ddd-review");
                Directory.CreateDirectory(System.IO.Path.Combine(directory, "scripts"));
                Directory.CreateDirectory(System.IO.Path.Combine(directory, "references"));
                Directory.CreateDirectory(System.IO.Path.Combine(directory, "assets"));
                File.WriteAllText(System.IO.Path.Combine(directory, "scripts/check.ps1"), "Write-Output 'check'");
                File.WriteAllText(System.IO.Path.Combine(directory, "references/rules.md"), "Domain rules");
                byte[] asset = [0, 255, 13, 10, 0];
                File.WriteAllBytes(System.IO.Path.Combine(directory, "assets/example.bin"), asset);
                File.WriteAllText(System.IO.Path.Combine(fixture.Repo, "skills/architecture/README.md"), "Architecture skills");
                fixture.Commit();
                var state = await fixture.Init.ExecuteAsync(fixture.Config);
                Check(state.Skills.Length == 8, "Each skill should have two installed copies.");
                Check(fixture.Read(".agents/skills/ddd-review/SKILL.md").Contains("domain checks"), "Deeply nested skill was not discovered.");
                Check(fixture.Read(".agents/skills/solid-review/SKILL.md").Contains("SOLID checks"), "Sibling skill was not discovered.");
                Check(fixture.Read(".claude/skills/pr-review/SKILL.md").Contains("pull request checks"), "Category containing spaces failed.");
                Check(fixture.Read(".agents/skills/commit/SKILL.md").Contains("flat skill"), "Existing flat layout stopped working.");
                Check(fixture.Read(".agents/skills/ddd-review/scripts/check.ps1") == "Write-Output 'check'", "Script path changed.");
                Check(fixture.Read(".agents/skills/ddd-review/references/rules.md") == "Domain rules", "Reference path changed.");
                Check(File.ReadAllBytes(fixture.Path(".agents/skills/ddd-review/assets/example.bin")).SequenceEqual(asset), "Binary resource changed.");
                Check(!Directory.Exists(fixture.Path(".agents/skills/architecture")), "A category was installed as a skill.");
                Check(fixture.Status.Execute().Lines.All(line => line.EndsWith("synced")), "Nested skills differ from their installed hashes.");
            }),
        ("SKILL.md inside a skill's resources does not become a second skill", async () =>
            {
                using var fixture = new Fixture();
                fixture.Skill("review", "v1", "architecture");
                var directory = System.IO.Path.Combine(fixture.Repo, "skills/architecture/review/assets/template");
                Directory.CreateDirectory(directory);
                File.WriteAllText(System.IO.Path.Combine(directory, "SKILL.md"), "Example skill template without frontmatter");
                fixture.Commit();
                var state = await fixture.Init.ExecuteAsync(fixture.Config);
                Check(state.Skills.Length == 2, "A template was discovered as a skill.");
                Check(fixture.Read(".agents/skills/review/assets/template/SKILL.md") == "Example skill template without frontmatter", "Nested template was not preserved.");
            }),
        ("personal collision refuses the entire installation", async () =>
            {
                using var fixture = new Fixture();
                fixture.Skill("review", "registry");
                fixture.Commit();
                fixture.Write(".claude/skills/review/SKILL.md", "personal");
                await Reject(() => fixture.Init.ExecuteAsync(fixture.Config), "Personal skill");
                Check(fixture.Read(".claude/skills/review/SKILL.md") == "personal", "Personal content changed.");
                Check(!Directory.Exists(fixture.Path(".agents/skills/review")), "Another target was partially installed.");
                Check(!File.Exists(fixture.Path(".skillsync/state.json")), "Failed init committed its configuration.");
            }),
        ("invalid skill metadata installs nothing", async () =>
            {
                using var fixture = new Fixture();
                fixture.Skill("review", "v1");
                File.WriteAllText(System.IO.Path.Combine(fixture.Repo, "skills/review/SKILL.md"), "---\nname: wrong\ndescription: review\n---\nBody");
                fixture.Commit();
                await Reject(() => fixture.Init.ExecuteAsync(fixture.Config), "matching name");
                Check(!Directory.Exists(fixture.Path(".agents/skills/review")), "Invalid skill was installed.");
            }),
        ("tracked symlinks are rejected without checking out registry code", async () =>
            {
                using var fixture = new Fixture();
                fixture.Skill("review", "v1");
                fixture.Commit();
                var blob = fixture.Git("rev-parse", "HEAD:skills/review/SKILL.md").Trim();
                fixture.Git("update-index", "--add", "--cacheinfo", "120000", blob, "skills/review/link");
                fixture.Git("commit", "-m", "Add unsafe link");
                await Reject(() => fixture.Init.ExecuteAsync(fixture.Config), "Symlinks");
                Check(!Directory.Exists(fixture.Path(".agents/skills/review")), "Unsafe registry was partially installed.");
            }),
        ("a symlink at the skills root cannot masquerade as an empty registry", async () =>
            {
                using var fixture = new Fixture();
                fixture.Skill("review", "v1");
                fixture.Commit();
                var blob = fixture.Git("rev-parse", "HEAD:skills/review/SKILL.md").Trim();
                fixture.Git("rm", "-r", "skills");
                fixture.Git("update-index", "--add", "--cacheinfo", "120000", blob, "skills");
                fixture.Git("commit", "-m", "Replace root with a link");
                await Reject(() => fixture.Init.ExecuteAsync(fixture.Config), "Symlinks");
            }),
        ("portable paths reject Windows device names", async () =>
            {
                using var fixture = new Fixture();
                // Allow the deliberately invalid index entry in this temporary repository on Windows.
                fixture.Git("config", "core.protectNTFS", "false");
                fixture.Skill("review", "v1");
                fixture.Commit();
                var blob = fixture.Git("rev-parse", "HEAD:skills/review/SKILL.md").Trim();
                fixture.Git("update-index", "--add", "--cacheinfo", "100644", blob, "skills/review/CON.txt");
                fixture.Git("commit", "-m", "Add unsafe path");
                await Reject(() => fixture.Init.ExecuteAsync(fixture.Config), "unsafe path");
            }),
        ("symbolic installation parents are refused", async () =>
            {
                if (OperatingSystem.IsWindows()) { Console.WriteLine("  SKIP: creating symlinks can require Windows privileges."); return; }
                using var fixture = new Fixture();
                fixture.Skill("review", "v1");
                fixture.Commit();
                var outside = System.IO.Path.Combine(fixture.Root, "outside");
                Directory.CreateDirectory(outside);
                Directory.CreateSymbolicLink(fixture.Path(".agents"), outside);
                await Reject(() => fixture.Init.ExecuteAsync(fixture.Config), "Symbolic links");
                Check(!Directory.EnumerateFileSystemEntries(outside).Any(), "Symlink target was modified.");
            }),
    ];
}
