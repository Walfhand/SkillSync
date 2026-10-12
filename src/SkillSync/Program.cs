using SkillSync.Cli;

return await CliApp.RunAsync(args,
    Environment.GetEnvironmentVariable("SKILLSYNC_HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Console.Out, Console.Error);
