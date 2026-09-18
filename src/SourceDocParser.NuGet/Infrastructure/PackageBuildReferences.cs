// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using NuGet.Frameworks;
using NuGet.ProjectModel;

namespace SourceDocParser.NuGet.Infrastructure;

/// <summary>Supplies managed references contributed by evaluated NuGet build assets.</summary>
internal static class PackageBuildReferences
{
    /// <summary>The evaluated reference list retained with its restore graph.</summary>
    internal const string ResultFileName = "build-references.json";

    /// <summary>Completed evaluations indexed by restored graph and SDK selection.</summary>
    private static readonly ConcurrentDictionary<string, string[]> _references = new(StringComparer.Ordinal);

    /// <summary>Serializes evaluation of the same graph while allowing independent graphs to proceed.</summary>
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);

    /// <summary>Supplements a graph with managed references selected by its package build targets.</summary>
    /// <param name="session">NuGet configuration and SDK selection context.</param>
    /// <param name="project">The restored documentation project.</param>
    /// <param name="assets">The resolved package graph.</param>
    /// <param name="references">Authoritative references to supplement.</param>
    /// <param name="cancellationToken">Cancellation for evaluation.</param>
    /// <returns>A task representing reference evaluation.</returns>
    internal static async Task AddAsync(
        PackageRestoreSession session,
        PackageSpec project,
        LockFile assets,
        Dictionary<string, string> references,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(references);
        cancellationToken.ThrowIfCancellationRequested();
        var framework = project.TargetFrameworks[0].FrameworkName;
        var runtimeIdentifier = GetRuntimeIdentifier(project);
        var target = assets.GetTarget(framework, runtimeIdentifier);
        if (target is null || (framework.Framework is not FrameworkConstants.FrameworkIdentifiers.Net && !HasBuildAssets(target)))
        {
            return;
        }

        var key = await GetKeyAsync(session, project, cancellationToken).ConfigureAwait(false);
        if (!_references.TryGetValue(key, out var selected))
        {
            var gate = _gates.GetOrAdd(key, static _ => new(1, 1));
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_references.TryGetValue(key, out selected))
                {
                    selected = await EvaluateAsync(session, project, runtimeIdentifier, cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    _references[key] = selected;
                }
            }
            finally
            {
                _ = gate.Release();
            }
        }

        for (var i = 0; i < selected.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = references.TryAdd(Path.GetFileNameWithoutExtension(selected[i]), selected[i]);
        }
    }

    /// <summary>Identifies a graph whose package props or targets can supply references.</summary>
    /// <param name="target">The selected restore target.</param>
    /// <returns>True when evaluated package build assets are present.</returns>
    internal static bool HasBuildAssets(LockFileTarget target)
    {
        for (var i = 0; i < target.Libraries.Count; i++)
        {
            var library = target.Libraries[i];
            if (HasBuildFiles(library.Build) || HasBuildFiles(library.BuildMultiTargeting))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Identifies build imports while excluding NuGet empty-group markers.</summary>
    /// <param name="items">The selected build assets.</param>
    /// <returns>True when a props or targets file is present.</returns>
    private static bool HasBuildFiles(IList<LockFileItem> items)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Path.EndsWith(".props", StringComparison.OrdinalIgnoreCase) || items[i].Path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns the runtime selected for the synthetic documentation project.</summary>
    /// <param name="project">The restored project.</param>
    /// <returns>The selected runtime identifier, or an empty string.</returns>
    private static string GetRuntimeIdentifier(PackageSpec project)
    {
        using var runtimes = project.RuntimeGraph.Runtimes.GetEnumerator();
        return runtimes.MoveNext() ? runtimes.Current.Key : string.Empty;
    }

    /// <summary>Identifies complete restore outputs and the SDK context evaluating their build assets.</summary>
    /// <param name="session">SDK selection context.</param>
    /// <param name="project">The restored project.</param>
    /// <param name="cancellationToken">Cancellation for reading restore outputs.</param>
    /// <returns>The evaluation cache key.</returns>
    private static async Task<string> GetKeyAsync(PackageRestoreSession session, PackageSpec project, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"{session.RootDirectory}|{project.FilePath}|{project.TargetFrameworks[0].RuntimeIdentifierGraphPath}"));
        string[] files = [Path.Combine(project.RestoreMetadata.OutputPath, PackageGraphRestore.AssetsFileName), $"{project.FilePath}.nuget.g.props", $"{project.FilePath}.nuget.g.targets"];
        for (var i = 0; i < files.Length; i++)
        {
            hash.AppendData(await File.ReadAllBytesAsync(files[i], cancellationToken).ConfigureAwait(false));
        }

        var directory = session.RootDirectory;
        while (directory is not null)
        {
            var global = Path.Combine(directory, "global.json");
            if (File.Exists(global))
            {
                hash.AppendData(await File.ReadAllBytesAsync(global, cancellationToken).ConfigureAwait(false));
                break;
            }

            directory = Path.GetDirectoryName(directory);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>Runs reference resolution against the graph's existing NuGet imports and assets.</summary>
    /// <param name="session">SDK selection context.</param>
    /// <param name="project">The restored documentation project.</param>
    /// <param name="runtimeIdentifier">The selected runtime.</param>
    /// <param name="cancellationToken">Cancellation for evaluation.</param>
    /// <returns>Managed references selected by MSBuild.</returns>
    /// <exception cref="InvalidOperationException">MSBuild cannot evaluate the restored project.</exception>
    private static async Task<string[]> EvaluateAsync(PackageRestoreSession session, PackageSpec project, string runtimeIdentifier, CancellationToken cancellationToken)
    {
        await WriteProjectAsync(project, runtimeIdentifier, cancellationToken).ConfigureAwait(false);
        var outputPath = Path.Combine(project.RestoreMetadata.OutputPath, ResultFileName);
        File.Delete(outputPath);
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = session.RootDirectory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, };
        start.ArgumentList.Add("msbuild");
        start.ArgumentList.Add(project.FilePath);
        start.ArgumentList.Add("-nologo");
        start.ArgumentList.Add("-verbosity:quiet");
        start.ArgumentList.Add("-target:ResolveReferences");
        start.ArgumentList.Add("-property:DesignTimeBuild=true");
        start.ArgumentList.Add("-getItem:ReferencePath");
        start.ArgumentList.Add($"-getResultOutputFile:{outputPath}");
        start.ArgumentList.Add($"-property:BaseIntermediateOutputPath={Path.TrimEndingDirectorySeparator(project.RestoreMetadata.OutputPath)}{Path.DirectorySeparatorChar}");
        start.ArgumentList.Add($"-property:MSBuildProjectExtensionsPath={Path.TrimEndingDirectorySeparator(project.RestoreMetadata.OutputPath)}{Path.DirectorySeparatorChar}");
        start.ArgumentList.Add($"-property:ProjectAssetsFile={Path.Combine(project.RestoreMetadata.OutputPath, PackageGraphRestore.AssetsFileName)}");
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
            if (!success)
            {
                throw new InvalidOperationException($"MSBuild reference evaluation failed for '{project.FilePath}'. {output}{errors}");
            }

            return await ReadReferencesAsync(outputPath, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            StopEvaluation(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            File.Delete(outputPath);
            throw;
        }
    }

    /// <summary>Writes the consuming SDK project while retaining its restored package constraints.</summary>
    /// <param name="project">The NuGet project model.</param>
    /// <param name="runtimeIdentifier">The selected runtime.</param>
    /// <param name="cancellationToken">Cancellation for writing the project.</param>
    /// <returns>A task representing project creation.</returns>
    private static Task WriteProjectAsync(PackageSpec project, string runtimeIdentifier, CancellationToken cancellationToken)
    {
        var target = project.TargetFrameworks[0];
        var properties = new XElement("PropertyGroup", new XElement("TargetFramework", target.FrameworkName.GetShortFolderName()));
        if (target.FrameworkName.Platform.Equals("windows", StringComparison.OrdinalIgnoreCase))
        {
            properties.Add(new XElement("EnableWindowsTargeting", true));
        }

        if (runtimeIdentifier is [_, ..])
        {
            properties.Add(new XElement("RuntimeIdentifier", runtimeIdentifier));
        }

        var references = new XElement("ItemGroup");
        for (var i = 0; i < target.Dependencies.Length; i++)
        {
            var dependency = target.Dependencies[i];
            if (dependency.AutoReferenced)
            {
                continue;
            }

            var version = dependency.LibraryRange.VersionRange?.ToNormalizedString() ?? "*";
            references.Add(new XElement("PackageReference", new XAttribute("Include", dependency.Name), new XAttribute(nameof(Version), version)));
        }

        var document = new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"), properties, references));
        return File.WriteAllTextAsync(project.FilePath, document.ToString(), cancellationToken);
    }

    /// <summary>Reads only managed metadata references from MSBuild's evaluated reference items.</summary>
    /// <param name="path">The MSBuild query output.</param>
    /// <param name="cancellationToken">Cancellation for reading references.</param>
    /// <returns>The valid managed reference paths.</returns>
    private static async Task<string[]> ReadReferencesAsync(string path, CancellationToken cancellationToken)
    {
        var stream = File.OpenRead(path);
        await using (stream.ConfigureAwait(false))
        {
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var items = document.RootElement.GetProperty("Items").GetProperty("ReferencePath");
            var references = new List<string>(items.GetArrayLength());
            foreach (var item in items.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = item.GetProperty("FullPath").GetString();
                if (file is null || !File.Exists(file))
                {
                    continue;
                }

                var candidate = File.OpenRead(file);
                await using (candidate.ConfigureAwait(false))
                {
                    if (!ManagedAssemblyExtractor.IsManagedAssembly(candidate))
                    {
                        continue;
                    }

                    references.Add(file);
                }
            }

            return [.. references];
        }
    }

    /// <summary>Starts MSBuild with an actionable SDK availability error.</summary>
    /// <param name="start">The reference evaluation command.</param>
    /// <returns>The running process.</returns>
    /// <exception cref="InvalidOperationException">The SDK command is unavailable.</exception>
    private static Process StartEvaluation(ProcessStartInfo start)
    {
        try
        {
            return Process.Start(start) ?? throw new InvalidOperationException("Could not start dotnet to evaluate package build references.");
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException("Package build references require a compatible .NET SDK and the dotnet command on PATH.", exception);
        }
    }

    /// <summary>Stops a canceled evaluation without racing normal process exit.</summary>
    /// <param name="process">The SDK evaluation process.</param>
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
}
