// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using NuGet.Packaging;
using SourceDocParser.Model;
using SourceDocParser.NuGet.Models;
using SourceDocParser.NuGet.Readers;
using SourceDocParser.Tfm;

namespace SourceDocParser.NuGet.Infrastructure;

/// <summary>Restores documentation roots with their independent dependency graphs.</summary>
public sealed partial class NuGetFetcher
{
    /// <summary>Expected number of frameworks in a documentation package.</summary>
    private const int DocumentationFrameworkCapacity = 4;

    /// <summary>Resolves the configured documentation packages and their compile references.</summary>
    /// <param name="rootDirectory">Directory containing the package manifest and NuGet configuration.</param>
    /// <param name="apiPath">Directory for restore graphs.</param>
    /// <param name="logger">Destination for restore diagnostics.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One group per documentation root and selected target framework.</returns>
    /// <exception cref="ArgumentException">A required directory is empty.</exception>
    /// <exception cref="InvalidOperationException">The package reader is unavailable.</exception>
    internal async Task<List<AssemblyGroup>> RestoreGroupsAsync(string rootDirectory, string apiPath, ILogger logger, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiPath);
        cancellationToken.ThrowIfCancellationRequested();
        var config = PackageConfigReader.Read(Path.Combine(rootDirectory, "nuget-packages.json"));
        var roots = await DiscoverAllPackagesAsync(_httpClient, config, logger, cancellationToken).ConfigureAwait(false);
        MergeExplicitRoots(roots, config);
        using var session = new PackageRestoreSession(rootDirectory, logger);
        var groups = new List<AssemblyGroup>(roots.Count);
        for (var i = 0; i < roots.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = roots[i];
            if (PackageExclusionFilter.IsExcludedByUser(root.Id, config.ExcludePackages, config.ExcludePackagePrefixes))
            {
                continue;
            }

            using var package = await session.DownloadAsync(root.Id, root.Version, cancellationToken).ConfigureAwait(false);
            var reader = package.PackageReader ?? throw new InvalidOperationException($"NuGet did not supply a package reader for '{root.Id}'.");
            var identity = reader.GetIdentity();
            var available = GetDocumentationFrameworks(reader);
            _ = config.TfmOverrides.TryGetValue(root.Id, out var tfmOverride);
            var selected = TfmResolver.SelectAllSupportedTfms(available, tfmOverride, config.TfmPreference);
            for (var t = 0; t < selected.Count; t++)
            {
                var group = await PackageGraphRestore.RestoreAsync(session, identity, selected[t], config, apiPath, cancellationToken).ConfigureAwait(false);
                if (group.AssemblyPaths is [_, ..])
                {
                    groups.Add(group);
                }
            }
        }

        return groups;
    }

    /// <summary>Applies explicit root versions over owner search results.</summary>
    /// <param name="roots">Owner-discovered packages.</param>
    /// <param name="config">Documentation configuration.</param>
    private static void MergeExplicitRoots(List<(string Id, string? Version)> roots, PackageConfig config)
    {
        for (var i = 0; i < config.AdditionalPackages.Length; i++)
        {
            var package = config.AdditionalPackages[i];
            var index = -1;
            for (var r = 0; r < roots.Count; r++)
            {
                if (!roots[r].Id.Equals(package.Id, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                index = r;
                break;
            }

            if (index < 0)
            {
                roots.Add((package.Id, package.Version));
                continue;
            }

            if (package.Version is not null)
            {
                roots[index] = (package.Id, package.Version);
            }
        }
    }

    /// <summary>Finds frameworks that provide documentation assemblies.</summary>
    /// <param name="reader">Documentation package reader.</param>
    /// <returns>Frameworks to apply the documentation selection policy to.</returns>
    private static List<string> GetDocumentationFrameworks(PackageReaderBase reader)
    {
        var result = new List<string>(DocumentationFrameworkCapacity);
        FrameworkSpecificGroup[] groups = [.. reader.GetLibItems(), .. reader.GetItems("ref")];
        for (var i = 0; i < groups.Length; i++)
        {
            var framework = groups[i].TargetFramework;
            if (framework.IsUnsupported || framework.IsAny)
            {
                continue;
            }

            var tfm = framework.GetShortFolderName();
            if (!result.Contains(tfm))
            {
                result.Add(tfm);
            }
        }

        result.Sort(StringComparer.Ordinal);
        return result;
    }
}
