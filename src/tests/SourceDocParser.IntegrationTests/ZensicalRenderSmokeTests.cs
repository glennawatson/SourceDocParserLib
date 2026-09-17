// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics;

namespace SourceDocParser.IntegrationTests;

/// <summary>
/// Runs a strict Zensical build of the bundled fixture in an isolated temporary directory.
/// Each invocation owns its Python environment and site output so framework test processes can run concurrently.
/// </summary>
public class ZensicalRenderSmokeTests
{
    /// <summary>Path (relative to the test project's <c>zensical/</c> folder) of the bundled mock site.</summary>
    private const string MockSiteRelativePath = "zensical/mock-site";

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
        var requirements = Path.Combine(projectRoot, RequirementsRelativePath);
        var docsRoot = Path.Combine(projectRoot, MockSiteRelativePath);

        if (!File.Exists(requirements) || !Directory.Exists(docsRoot))
        {
            return; // Fixture incomplete; treat as skipped.
        }

        var workspace = Directory.CreateTempSubdirectory("sourcedocparser-zensical-");
        try
        {
            var venvDir = Path.Combine(workspace.FullName, ".venv");
            CopyMockSite(docsRoot, workspace.FullName);
            await EnsureVenvAsync(venvDir, requirements);

            var zensical = ResolveVenvBinary(venvDir, "zensical");
            var (exitCode, stdout, stderr) = await RunAsync(zensical, ["build", "--strict"], workspace.FullName);

            await Assert.That(exitCode)
                .IsEqualTo(0)
                .Because($"zensical failed:\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        }
        finally
        {
            workspace.Delete(recursive: true);
        }
    }

    /// <summary>Copies fixture inputs without sharing generated site or cache files between test processes.</summary>
    /// <param name="source">Bundled mock-site directory.</param>
    /// <param name="destination">Isolated test workspace.</param>
    private static void CopyMockSite(string source, string destination)
    {
        const string ConfigName = "mkdocs.yml";
        File.Copy(Path.Combine(source, ConfigName), Path.Combine(destination, ConfigName));
        foreach (var file in Directory.EnumerateFiles(Path.Combine(source, "docs"), "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            _ = Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
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

#if NET11_0_OR_GREATER
            return process.WaitForExitStatus() is { Signal: null, ExitCode: 0 };
#else
            process.WaitForExit();
            return process.ExitCode == 0;
#endif
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
    /// lives inside the venv layout for the current OS -- <c>bin/</c>
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

    /// <summary>Creates an isolated Python environment and installs the fixture requirements.</summary>
    /// <param name="venvDir">Absolute path to the venv root.</param>
    /// <param name="requirements">Absolute path to the pip requirements file.</param>
    /// <returns>A task representing the asynchronous bootstrap.</returns>
    /// <exception cref="InvalidOperationException">Thrown when <c>exit != 0</c>.</exception>
    private static async Task EnsureVenvAsync(string venvDir, string requirements)
    {
        var (exit, stdout, stderr) = await RunAsync("python3", ["-m", "venv", venvDir], workingDirectory: null);
        if (exit != 0)
        {
            throw new InvalidOperationException(
                $"Failed to create venv at '{venvDir}'.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        }

        var pip = ResolveVenvBinary(venvDir, "pip");
        var (pipExit, pipStdout, pipStderr) = await RunAsync(pip, ["install", "-r", requirements], workingDirectory: null);
        if (pipExit != 0)
        {
            throw new InvalidOperationException(
                $"pip install failed.\nSTDOUT:\n{pipStdout}\nSTDERR:\n{pipStderr}");
        }
    }

    /// <summary>Spawns <paramref name="tool"/> with <paramref name="args"/> in <paramref name="workingDirectory"/> and captures stdout / stderr for any failure message.</summary>
    /// <param name="tool">Executable.</param>
    /// <param name="args">Arguments.</param>
    /// <param name="workingDirectory">Working directory; <see langword="null"/> inherits the current process directory.</param>
    /// <returns>The exit code plus captured streams.</returns>
    /// <exception cref="InvalidOperationException">Thrown when <c>Process.Start(startInfo)</c> is <see langword="null"/>.</exception>
    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(string tool, string[] args, string? workingDirectory)
    {
        var startInfo = new ProcessStartInfo { FileName = tool, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, };

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
#if NET11_0_OR_GREATER
        var status = await process.WaitForExitStatusAsync();
        var exitCode = status.Signal is null ? status.ExitCode : -1;
#else
        await process.WaitForExitAsync();
        var exitCode = process.ExitCode;
#endif
        return (exitCode, await stdoutTask, await stderrTask);
    }
}
