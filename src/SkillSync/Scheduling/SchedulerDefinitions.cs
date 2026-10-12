namespace SkillSync.Scheduling;

public static class SchedulerDefinitions
{
    public static Dictionary<string, string> Build(string platform, string executable, string home, int minutes, string user, string searchPath)
    {
        if (minutes is < 1 or > 1440 || executable.Any(char.IsControl) || home.Any(char.IsControl) || searchPath.Any(char.IsControl))
            throw new ArgumentException("Interval must be between 1 and 1440 minutes; paths must not contain control characters.");
        static string Unit(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("%", "%%") + "\"";
        static string Xml(string text) => System.Security.SecurityElement.Escape(text)!;
        static string Ps(string text) => "'" + text.Replace("'", "''") + "'";
        if (platform == "linux") return new()
        {
            [".config/systemd/user/skillsync.service"] = $"# Managed by SkillSync\n[Unit]\nDescription=Synchronize team skills\n[Service]\nType=oneshot\nEnvironment={Unit("SKILLSYNC_HOME=" + home)}\nEnvironment={Unit("PATH=" + searchPath)}\nExecStart=/usr/bin/env -- {Unit(executable.Replace("$", "$$"))} sync --scheduled\n",
            [".config/systemd/user/skillsync.timer"] = $"# Managed by SkillSync\n[Unit]\nDescription=Schedule team skill synchronization\n[Timer]\nOnStartupSec=15s\nOnUnitActiveSec={minutes}min\n[Install]\nWantedBy=timers.target\n"
        };
        if (platform == "osx") return new()
        {
            ["Library/LaunchAgents/io.skillsync.sync.plist"] =
                $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!-- Managed by SkillSync -->\n<plist version=\"1.0\"><dict><key>Label</key><string>io.skillsync.sync</string>" +
                $"<key>ProgramArguments</key><array><string>{Xml(executable)}</string><string>sync</string><string>--scheduled</string></array>" +
                $"<key>EnvironmentVariables</key><dict><key>SKILLSYNC_HOME</key><string>{Xml(home)}</string><key>PATH</key><string>{Xml(searchPath)}</string></dict>" +
                $"<key>RunAtLoad</key><true/><key>StartInterval</key><integer>{minutes * 60}</integer></dict></plist>"
        };
        if (platform != "win") throw new ArgumentException("Unsupported scheduler platform.");
        var action = $"-NoProfile -NonInteractive -Command \"$env:SKILLSYNC_HOME={Ps(home)}; & {Ps(executable)} sync --scheduled; exit $LASTEXITCODE\"";
        return new()
        {
            [".skillsync/schedule.xml"] =
                $"<?xml version=\"1.0\" encoding=\"UTF-16\"?><!-- Managed by SkillSync --><Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">" +
                $"<Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{Xml(user)}</UserId></LogonTrigger><TimeTrigger><Repetition><Interval>PT{minutes}M</Interval><StopAtDurationEnd>false</StopAtDurationEnd></Repetition><StartBoundary>{DateTime.Now.AddSeconds(15):yyyy-MM-ddTHH:mm:ss}</StartBoundary><Enabled>true</Enabled></TimeTrigger></Triggers>" +
                $"<Principals><Principal id=\"Author\"><UserId>{Xml(user)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>" +
                "<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT2M</ExecutionTimeLimit></Settings>" +
                $"<Actions Context=\"Author\"><Exec><Command>{Xml(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))}</Command><Arguments>{Xml(action)}</Arguments></Exec></Actions></Task>"
        };
    }
}
