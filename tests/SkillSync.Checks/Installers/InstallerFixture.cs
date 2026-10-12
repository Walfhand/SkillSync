using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace SkillSync.Checks.Installers;

internal sealed class InstallerFixture : IAsyncDisposable
{
    private readonly HttpListener listener = new();
    private readonly Task server;
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "skillsync-installer-" + Guid.NewGuid().ToString("N"));
    public string InstallDir => Path.Combine(Root, "bin with spaces 'quotes' é");
    public string Profile => Path.Combine(Root, "shell profile");
    public string Binary => Path.Combine(InstallDir, OperatingSystem.IsWindows() ? "skillsync.exe" : "skillsync");
    public static string Script => Path.Combine(ProjectRoot(), "scripts", OperatingSystem.IsWindows() ? "install.ps1" : "install.sh");
    public static string Shell => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe")
        : "/bin/bash";
    public string BaseUrl { get; }
    public string Rid { get; }
    public string AssetName => "skillsync-" + Rid + (OperatingSystem.IsWindows() ? ".zip" : ".tar.gz");
    public byte[] Payload { get; } = Encoding.UTF8.GetBytes("#!/bin/sh\nprintf '%s\\n' '9.8.7'\n");
    public bool BadChecksum { get; set; }
    public bool MissingAsset { get; set; }
    public bool MissingBinary { get; set; }
    public int Requests;
    private readonly byte[] archive;
    private readonly byte[] malformedArchive;

    public InstallerFixture(byte[]? payload = null)
    {
        if (payload is not null) Payload = payload;
        Directory.CreateDirectory(Root);
        var os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        var architecture = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        Rid = os + "-" + architecture;
        archive = CreateArchive(OperatingSystem.IsWindows() ? "skillsync.exe" : "skillsync");
        malformedArchive = CreateArchive("../escaped");
        var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        BaseUrl = $"http://127.0.0.1:{port}/releases";
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        server = ServeAsync();
    }

    private static string ProjectRoot([CallerFilePath] string file = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../.."));

    private byte[] CreateArchive(string name)
    {
        using var buffer = new MemoryStream();
        if (OperatingSystem.IsWindows())
        {
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            using (var output = zip.CreateEntry(name).Open()) output.Write(Payload);
        }
        else
        {
            using (var gzip = new GZipStream(buffer, CompressionMode.Compress, leaveOpen: true))
            using (var tar = new TarWriter(gzip, leaveOpen: false))
            using (var input = new MemoryStream(Payload))
                tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = input });
        }
        return buffer.ToArray();
    }

    private async Task ServeAsync()
    {
        try
        {
            while (listener.IsListening)
            {
                var context = await listener.GetContextAsync();
                Interlocked.Increment(ref Requests);
                var bytes = MissingBinary ? malformedArchive : archive;
                var request = context.Request.Url!.AbsolutePath;
                if (request.EndsWith("/" + AssetName + ".sha256", StringComparison.Ordinal))
                {
                    var hash = BadChecksum ? new string('0', 64) : Convert.ToHexStringLower(SHA256.HashData(bytes));
                    bytes = Encoding.ASCII.GetBytes(hash + "  " + AssetName + "\n");
                }
                else if (MissingAsset || !request.EndsWith("/" + AssetName, StringComparison.Ordinal))
                {
                    context.Response.StatusCode = 404;
                    bytes = [];
                }
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { }
    }

    public Task<(int Exit, string Output, string Error)> InstallAsync(string version = "v9.8.7", bool noGit = false)
    {
        Assertions.Check(File.Exists(Script), "Installer script is missing: " + Path.GetFileName(Script));
        string[] arguments = OperatingSystem.IsWindows()
            ? ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Script,
                "-Version", version, "-InstallDir", InstallDir, "-NoPathUpdate"]
            : [Script, "--version", version, "--install-dir", InstallDir, "--profile", Profile];
        return RunAsync(arguments, noGit);
    }

    public async Task<(int Exit, string Output, string Error)> RunAsync(string[] arguments, bool noGit = false)
    {
        var start = new ProcessStartInfo(Shell) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["SKILLSYNC_RELEASE_BASE_URL"] = BaseUrl;
        start.Environment["NO_PROXY"] = "127.0.0.1,localhost";
        if (noGit)
        {
            var empty = Path.Combine(Root, "empty-path");
            Directory.CreateDirectory(empty);
            start.Environment["PATH"] = empty;
        }
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Payload.Length > 1024 * 1024 ? 180 : 60));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await output, await error);
    }

    public async Task<(int Exit, string Output, string Error)> CliAsync(string home, params string[] arguments)
    {
        var start = new ProcessStartInfo(Binary) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["SKILLSYNC_HOME"] = home;
        start.Environment["SKILLSYNC_RELEASE_BASE_URL"] = BaseUrl;
        start.Environment["NO_PROXY"] = "127.0.0.1,localhost";
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await output, await error);
    }

    public void ExistingInstallation()
    {
        Directory.CreateDirectory(InstallDir);
        File.WriteAllText(Binary, "previous binary");
        File.WriteAllText(Profile, "export USER_VALUE='kept'\n");
    }

    public void AssertPreviousKept()
    {
        Assertions.Check(File.ReadAllText(Binary) == "previous binary", "Existing binary was changed after rejection.");
        Assertions.Check(File.ReadAllText(Profile) == "export USER_VALUE='kept'\n", "Existing shell configuration was changed.");
    }

    public async ValueTask DisposeAsync()
    {
        listener.Close();
        await server;
        Directory.Delete(Root, recursive: true);
    }
}
