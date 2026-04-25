// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics;

namespace SourceDocParser.IntegrationTests;

/// <summary>
/// Render-smoke that points Zensical at the bundled mock-site fixture
/// and asserts a strict build completes. The test bootstraps its own
/// Python virtualenv under <c>zensical/.venv</c> on first run
/// (assuming <c>python3</c> is on PATH) and reuses the venv on
/// subsequent runs. Falls back to silently skipping when
/// <c>python3</c> is not installed so the suite still runs on bare
/// dev machines.
/// </summary>
public class ZensicalRenderSmokeTests
{
    /// <summary>Path (relative to the test project's <c>zensical/</c> folder) of the bundled mock site.</summary>
    private const string MockSiteRelativePath = "zensical/mock-site";

    /// <summary>Path (relative to the test project's <c>zensical/</c> folder) of the Python virtual environment.</summary>
    private const string VenvRelativePath = "zensical/.venv";

    /// <summary>Path (relative to the test project's <c>zensical/</c> folder) of the requirements file.</summary>
    private const string RequirementsRelativePath = "zensical/requirements.txt";

    /// <summary>
    /// Runs <c>zensical build --strict</c> against the mock-site fixture
    /// and asserts a zero exit code (no warnings or unresolved refs).
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ZensicalStrictBuildSucceeds()
    {
        if (!IsOnPath("python3"))
        {
            return; // Python not installed; treat as skipped.
        }

        var projectRoot = LocateProjectRoot();
        var venvDir = Path.Combine(projectRoot, VenvRelativePath);
        var requirements = Path.Combine(projectRoot, RequirementsRelativePath);
        var docsRoot = Path.Combine(projectRoot, MockSiteRelativePath);

        if (!File.Exists(requirements) || !Directory.Exists(docsRoot))
        {
            return; // Fixture incomplete; treat as skipped.
        }

        await EnsureVenvAsync(venvDir, requirements);

        var zensical = ResolveVenvBinary(venvDir, "zensical");
        var (exitCode, stdout, stderr) = await RunAsync(zensical, ["build", "--strict"], docsRoot);

        await Assert.That(exitCode)
            .IsEqualTo(0)
            .Because($"zensical failed:\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
    }

    /// <summary>
    /// Returns true when <paramref name="tool"/> resolves on the system
    /// PATH. Uses <c>which</c> on Unix and <c>where</c> on Windows so
    /// the lookup matches the host shell.
    /// </summary>
    /// <param name="tool">Executable name to look up.</param>
    /// <returns>True when the tool resolves.</returns>
    private static bool IsOnPath(string tool)
    {
        var locator = OperatingSystem.IsWindows() ? "where" : "which";
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = locator,
                Arguments = tool,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process is null)
            {
                return false;
            }

            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns the absolute path of the test project root (the
    /// directory containing <c>SourceDocParser.IntegrationTests.csproj</c>).
    /// Walks up from <see cref="AppContext.BaseDirectory"/> so the
    /// path resolves regardless of the test binary's nested bin path.
    /// </summary>
    /// <returns>Absolute path to the test project root.</returns>
    /// <exception cref="DirectoryNotFoundException">When no parent contains the marker csproj.</exception>
    private static string LocateProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SourceDocParser.IntegrationTests.csproj")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate SourceDocParser.IntegrationTests project root.");
    }

    /// <summary>
    /// Returns the absolute path of <paramref name="toolName"/> as it
    /// lives inside the venv layout for the current OS — <c>bin/</c>
    /// on Unix, <c>Scripts/</c> on Windows, with a <c>.exe</c> suffix
    /// on Windows.
    /// </summary>
    /// <param name="venvDir">Absolute path to the venv root.</param>
    /// <param name="toolName">Tool basename (no extension).</param>
    /// <returns>Absolute path to the venv-local executable.</returns>
    private static string ResolveVenvBinary(string venvDir, string toolName)
    {
        var subdir = OperatingSystem.IsWindows() ? "Scripts" : "bin";
        var ext = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
        return Path.Combine(venvDir, subdir, toolName + ext);
    }

    /// <summary>
    /// Creates <paramref name="venvDir"/> via <c>python3 -m venv</c> when
    /// missing, then installs <paramref name="requirements"/> into it
    /// when the requirements file is newer than the install marker (or
    /// the marker doesn't exist yet). Subsequent runs are no-ops.
    /// </summary>
    /// <param name="venvDir">Absolute path to the venv root.</param>
    /// <param name="requirements">Absolute path to the pip requirements file.</param>
    /// <returns>A task representing the asynchronous bootstrap.</returns>
    private static async Task EnsureVenvAsync(string venvDir, string requirements)
    {
        if (!Directory.Exists(venvDir))
        {
            var (exit, stdout, stderr) = await RunAsync("python3", ["-m", "venv", venvDir], workingDirectory: null);
            if (exit != 0)
            {
                throw new InvalidOperationException(
                    $"Failed to create venv at '{venvDir}'.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
            }
        }

        var marker = Path.Combine(venvDir, ".requirements-installed");
        var requirementsStamp = File.GetLastWriteTimeUtc(requirements);
        if (File.Exists(marker) && File.GetLastWriteTimeUtc(marker) >= requirementsStamp)
        {
            return;
        }

        var pip = ResolveVenvBinary(venvDir, "pip");
        var (pipExit, pipStdout, pipStderr) = await RunAsync(pip, ["install", "-r", requirements], workingDirectory: null);
        if (pipExit != 0)
        {
            throw new InvalidOperationException(
                $"pip install failed.\nSTDOUT:\n{pipStdout}\nSTDERR:\n{pipStderr}");
        }

        await File.WriteAllTextAsync(marker, requirementsStamp.ToString("O"));
    }

    /// <summary>
    /// Spawns <paramref name="tool"/> with <paramref name="args"/> in
    /// <paramref name="workingDirectory"/> and captures stdout / stderr
    /// for any failure message.
    /// </summary>
    /// <param name="tool">Executable.</param>
    /// <param name="args">Arguments.</param>
    /// <param name="workingDirectory">Working directory; <see langword="null"/> inherits the current process directory.</param>
    /// <returns>The exit code plus captured streams.</returns>
    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(string tool, string[] args, string? workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = tool,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (workingDirectory is not null)
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start {tool}");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await stdoutTask, await stderrTask);
    }
}
