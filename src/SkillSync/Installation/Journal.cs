namespace SkillSync.Installation;

public sealed record Change(string Target, string Name, bool HadOriginal, bool Install);
public sealed record Journal(string Id, Change[] Changes);
