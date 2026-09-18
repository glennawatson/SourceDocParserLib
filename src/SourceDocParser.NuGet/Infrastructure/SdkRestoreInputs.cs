// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.ProjectModel;
using SourceDocParser.LibCompilation;

namespace SourceDocParser.NuGet.Infrastructure;

/// <summary>Obtains restore policy from the installed SDK's project evaluation.</summary>
internal static class SdkRestoreInputs
{
    /// <summary>Completed SDK evaluations reusable by documentation graphs.</summary>
    private static readonly ConcurrentDictionary<string, TargetFrameworkInformation> _inputs = new(StringComparer.Ordinal);

    /// <summary>Adds SDK framework dependencies, pruning rules, and runtime compatibility metadata.</summary>
    /// <param name="session">Documentation configuration and package session.</param>
    /// <param name="target">NuGet documentation target to enrich.</param>
    /// <param name="runtimeIdentifier">Optional runtime identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The documentation target enriched with evaluated SDK metadata.</returns>
    internal static async Task<TargetFrameworkInformation> ApplyAsync(
        PackageRestoreSession session,
        TargetFrameworkInformation target,
        string? runtimeIdentifier,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = GetCacheKey(session.RootDirectory, target.FrameworkName, runtimeIdentifier);
        if (!_inputs.TryGetValue(key, out var sdk))
        {
            sdk = await EvaluateAsync(session, target.FrameworkName, runtimeIdentifier, cancellationToken).ConfigureAwait(false);
            _ = _inputs.TryAdd(key, sdk);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var dependencies = new List<LibraryDependency>(target.Dependencies.Length + sdk.Dependencies.Length);
        dependencies.AddRange(target.Dependencies);
        for (var i = 0; i < sdk.Dependencies.Length; i++)
        {
            if (!ContainsDependency(dependencies, sdk.Dependencies[i].Name))
            {
                dependencies.Add(sdk.Dependencies[i]);
            }
        }

        var downloads = new List<DownloadDependency>(target.DownloadDependencies.Length + sdk.DownloadDependencies.Length);
        downloads.AddRange(target.DownloadDependencies);
        for (var i = 0; i < sdk.DownloadDependencies.Length; i++)
        {
            if (!ContainsDownload(downloads, sdk.DownloadDependencies[i].Name))
            {
                downloads.Add(sdk.DownloadDependencies[i]);
            }
        }

        return new(target)
        {
            PackagesToPrune = sdk.PackagesToPrune,
            RuntimeIdentifierGraphPath = sdk.RuntimeIdentifierGraphPath,
            FrameworkReferences = sdk.FrameworkReferences,
            Dependencies = [.. dependencies],
            DownloadDependencies = [.. downloads],
        };
    }

    /// <summary>Identifies the SDK selection and framework inputs governing evaluation.</summary>
    /// <param name="root">Directory governing SDK selection.</param>
    /// <param name="framework">Documentation framework.</param>
    /// <param name="runtimeIdentifier">Optional runtime identifier.</param>
    /// <returns>A cache key that changes with installed SDKs and global SDK configuration.</returns>
    private static string GetCacheKey(string root, NuGetFramework framework, string? runtimeIdentifier)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"{root}|{framework.GetShortFolderName()}|{runtimeIdentifier}"));
        var directory = Path.GetFullPath(root);
        while (directory is not null)
        {
            var global = Path.Combine(directory, "global.json");
            if (File.Exists(global))
            {
                hash.AppendData(File.ReadAllBytes(global));
                break;
            }

            directory = Path.GetDirectoryName(directory);
        }

        var roots = DotNetSdkLocator.EnumerateInstallRoots();
        for (var i = 0; i < roots.Count; i++)
        {
            var sdk = Path.Combine(roots[i], "sdk");
            if (!Directory.Exists(sdk))
            {
                continue;
            }

            var versions = Directory.GetDirectories(sdk);
            Array.Sort(versions, StringComparer.Ordinal);
            for (var v = 0; v < versions.Length; v++)
            {
                hash.AppendData(Encoding.UTF8.GetBytes(versions[v]));
            }
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>Evaluates a synthetic SDK project without restoring documentation packages.</summary>
    /// <param name="session">Configuration governing SDK selection.</param>
    /// <param name="framework">Documentation target framework.</param>
    /// <param name="runtimeIdentifier">Optional runtime identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The SDK's restore target metadata.</returns>
    /// <exception cref="InvalidOperationException">SDK evaluation fails or produces no target metadata.</exception>
    private static async Task<TargetFrameworkInformation> EvaluateAsync(
        PackageRestoreSession session,
        NuGetFramework framework,
        string? runtimeIdentifier,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"sourcedocparser-sdk-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(directory);
        try
        {
            var projectPath = Path.Combine(directory, "documentation.csproj");
            var graphPath = Path.Combine(directory, "sdk.dg.json");
            await WriteProjectAsync(projectPath, framework, runtimeIdentifier, cancellationToken).ConfigureAwait(false);
            var result = await RunEvaluationAsync(session.RootDirectory, projectPath, graphPath, cancellationToken).ConfigureAwait(false);
            if (!result.Success && framework.HasPlatform
                && (result.Output.Contains("NETSDK1147", StringComparison.Ordinal) || result.Output.Contains("NETSDK1178", StringComparison.Ordinal)))
            {
                session.Logger.LogDebug($"Using base SDK restore policy for '{framework}'; platform reference packs are resolved separately. SDK evaluation: {result.Output}");
                await WriteProjectAsync(projectPath, new(framework.Framework, framework.Version), runtimeIdentifier, cancellationToken).ConfigureAwait(false);
                result = await RunEvaluationAsync(session.RootDirectory, projectPath, graphPath, cancellationToken).ConfigureAwait(false);
            }

            if (!result.Success)
            {
                throw new InvalidOperationException($"SDK restore metadata evaluation failed for '{framework}'. Install a compatible .NET SDK and required targeting packs. {result.Output}");
            }

            var graph = DependencyGraphSpec.Load(graphPath);
            for (var i = 0; i < graph.Projects.Count; i++)
            {
                if (graph.Projects[i].TargetFrameworks is [var target])
                {
                    return target;
                }
            }

            throw new InvalidOperationException($"SDK restore metadata evaluation produced no target framework for '{framework}'.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>Writes the project whose SDK defaults supply restore metadata.</summary>
    /// <param name="path">Synthetic project path.</param>
    /// <param name="framework">Framework to evaluate.</param>
    /// <param name="runtimeIdentifier">Optional runtime identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing project creation.</returns>
    private static Task WriteProjectAsync(string path, NuGetFramework framework, string? runtimeIdentifier, CancellationToken cancellationToken)
    {
        var properties = new XElement("PropertyGroup", new XElement("TargetFramework", framework.GetShortFolderName()));
        if (framework.Platform.Equals("windows", StringComparison.OrdinalIgnoreCase))
        {
            properties.Add(new XElement("EnableWindowsTargeting", true));
        }

        if (runtimeIdentifier is { Length: > 0 })
        {
            properties.Add(new XElement("RuntimeIdentifier", runtimeIdentifier));
        }

        var project = new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"), properties));
        return File.WriteAllTextAsync(path, project.ToString(), cancellationToken);
    }

    /// <summary>Runs the SDK's restore-input generation target.</summary>
    /// <param name="root">Directory governing SDK selection.</param>
    /// <param name="project">Synthetic SDK project.</param>
    /// <param name="graph">Destination dependency graph file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The process success state and diagnostic output.</returns>
    /// <exception cref="InvalidOperationException">The installed SDK cannot be started.</exception>
    private static async Task<(bool Success, string Output)> RunEvaluationAsync(string root, string project, string graph, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, };
        start.ArgumentList.Add("msbuild");
        start.ArgumentList.Add(project);
        start.ArgumentList.Add("-nologo");
        start.ArgumentList.Add("-target:GenerateRestoreGraphFile");
        start.ArgumentList.Add($"-property:RestoreGraphOutputPath={graph}");
        start.ArgumentList.Add("-property:ImportDirectoryBuildProps=false");
        start.ArgumentList.Add("-property:ImportDirectoryBuildTargets=false");
        start.ArgumentList.Add("-property:ManagePackageVersionsCentrally=false");
        using var process = StartEvaluation(start);
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
#if NET11_0_OR_GREATER
            var status = await process.WaitForExitStatusAsync(cancellationToken).ConfigureAwait(false);
            var success = status is { Signal: null, ExitCode: 0 };
#else
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var success = process.ExitCode is 0;
#endif
            var output = await stdout.ConfigureAwait(false);
            var errors = await stderr.ConfigureAwait(false);
            return (success, $"{output}{errors}");
        }
        catch (OperationCanceledException)
        {
            StopEvaluation(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Starts SDK evaluation with an actionable error when the SDK command is unavailable.</summary>
    /// <param name="start">SDK command and evaluation arguments.</param>
    /// <returns>The running SDK process.</returns>
    /// <exception cref="InvalidOperationException">The dotnet SDK command cannot be started.</exception>
    private static Process StartEvaluation(ProcessStartInfo start)
    {
        try
        {
            return Process.Start(start) ?? throw new InvalidOperationException("Could not start dotnet to evaluate SDK restore metadata.");
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException("SDK restore metadata requires a compatible .NET SDK and the dotnet command on PATH.", exception);
        }
    }

    /// <summary>Stops a canceled SDK evaluation without racing normal process exit.</summary>
    /// <param name="process">The SDK process being canceled.</param>
    private static void StopEvaluation(Process process)
    {
        if (process.HasExited)
        {
            return;
        }

        try
        {
            process.Kill(true);
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
        }
    }

    /// <summary>Preserves explicit dependency constraints over SDK defaults.</summary>
    /// <param name="dependencies">Existing direct dependencies.</param>
    /// <param name="id">SDK dependency identifier.</param>
    /// <returns>True when an explicit constraint exists.</returns>
    private static bool ContainsDependency(List<LibraryDependency> dependencies, string id)
    {
        for (var i = 0; i < dependencies.Count; i++)
        {
            if (dependencies[i].Name.Equals(id, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Preserves explicit targeting-pack pins over SDK defaults.</summary>
    /// <param name="downloads">Existing targeting-pack downloads.</param>
    /// <param name="id">SDK targeting-pack identifier.</param>
    /// <returns>True when an explicit targeting-pack constraint exists.</returns>
    private static bool ContainsDownload(List<DownloadDependency> downloads, string id)
    {
        for (var i = 0; i < downloads.Count; i++)
        {
            if (downloads[i].Name.Equals(id, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
