using SkillSync.Checks;
using SkillSync.Checks.Features;
using SkillSync.Checks.Installation;
using SkillSync.Checks.Registry;

var checks = InitChecks.All.Concat(SyncChecks.All).Concat(RecoveryChecks.All)
    .Concat(LifecycleChecks.All).Concat(ValidationChecks.All).Concat(CliChecks.All).ToArray();

var failures = 0;
foreach (var (name, run) in checks)
{
    try { await run(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {name}: {ex}"); }
}
Console.WriteLine($"{checks.Length - failures}/{checks.Length} checks passed.");
return failures == 0 ? 0 : 1;
