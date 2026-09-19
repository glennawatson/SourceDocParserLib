// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.Versioning;

namespace SourceDocParser.NuGet.Infrastructure;

/// <summary>Acquires framework reference assemblies from NuGet for the documented target.</summary>
internal static class FrameworkReferencePackages
{
    /// <summary>The reference pack for modern .NET frameworks.</summary>
    private const string CorePack = "Microsoft.NETCore.App.Ref";

    /// <summary>The reference pack for .NET Standard 2.1.</summary>
    private const string StandardPack = "NETStandard.Library.Ref";

    /// <summary>The package contract used by earlier .NET Standard frameworks.</summary>
    private const string StandardLibrary = "NETStandard.Library";

    /// <summary>The reference-assembly package family for .NET Framework.</summary>
    private const string FrameworkPackPrefix = "Microsoft.NETFramework.ReferenceAssemblies.";

    /// <summary>The .NET Core release that introduced standalone targeting packs.</summary>
    private const int TargetingPackMajorVersion = 3;

    /// <summary>The .NET Standard release using the expanded library contract.</summary>
    private const int ExpandedStandardMajorVersion = 2;

    /// <summary>Adds framework references without requiring an SDK installation.</summary>
    /// <param name="session">Configured NuGet session.</param>
    /// <param name="framework">Documentation target framework.</param>
    /// <param name="dependencies">Direct package constraints.</param>
    /// <param name="downloads">Reference-only package downloads.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing framework package selection.</returns>
    internal static async Task AddDownloadsAsync(
        PackageRestoreSession session,
        NuGetFramework framework,
        List<LibraryDependency> dependencies,
        List<DownloadDependency> downloads,
        CancellationToken cancellationToken)
    {
        if (framework.Framework is FrameworkConstants.FrameworkIdentifiers.NetStandard && framework.Version < new Version(2, 1))
        {
            AddStandardDependency(framework, dependencies);
            return;
        }

        if (framework.Framework is FrameworkConstants.FrameworkIdentifiers.NetCoreApp && framework.Version.Major < TargetingPackMajorVersion)
        {
            var contractVersion = $"{framework.Version.Major}.{framework.Version.Minor}.0";
            AddImplicitDependency("Microsoft.NETCore.App", contractVersion, dependencies);
            return;
        }

        var id = framework.Framework switch
        {
            FrameworkConstants.FrameworkIdentifiers.NetCoreApp => CorePack,
            FrameworkConstants.FrameworkIdentifiers.NetStandard => StandardPack,
            FrameworkConstants.FrameworkIdentifiers.Net => $"{FrameworkPackPrefix}{framework.GetShortFolderName()}",
            _ => null,
        };
        if (id is null || ContainsDownload(downloads, id))
        {
            return;
        }

        var family = framework.Framework is FrameworkConstants.FrameworkIdentifiers.Net ? null : framework.Version;
        var version = await ResolveVersionAsync(session, id, family, cancellationToken).ConfigureAwait(false);
        downloads.Add(new(id, new(version, true, version, true)));
    }

    /// <summary>Selects a published reference pack within the requested framework release.</summary>
    /// <param name="session">Configured NuGet session.</param>
    /// <param name="id">Reference-pack package identifier.</param>
    /// <param name="frameworkVersion">Framework release family, or null for framework-independent package versions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The latest stable compatible version, or a prerelease when that framework has no stable pack.</returns>
    /// <exception cref="InvalidOperationException">No matching reference pack is available from the configured sources.</exception>
    internal static async Task<NuGetVersion> ResolveVersionAsync(PackageRestoreSession session, string id, Version? frameworkVersion, CancellationToken cancellationToken)
    {
        var versions = await session.GetVersionsAsync(id, cancellationToken).ConfigureAwait(false);
        NuGetVersion? stable = null;
        NuGetVersion? prerelease = null;
        for (var i = 0; i < versions.Length; i++)
        {
            var version = versions[i];
            if (!MatchesFrameworkVersion(version, frameworkVersion))
            {
                continue;
            }

            if (version.IsPrerelease)
            {
                if (prerelease is null || version > prerelease)
                {
                    prerelease = version;
                }

                continue;
            }

            if (stable is null || version > stable)
            {
                stable = version;
            }
        }

        return stable ?? prerelease
            ?? throw new InvalidOperationException($"No reference pack '{id}' for '{frameworkVersion}' is available. Check NuGet sources, source mappings, or referencePackages.");
    }

    /// <summary>Restricts framework-versioned packs to the documented release family.</summary>
    /// <param name="version">Available package version.</param>
    /// <param name="frameworkVersion">Required framework release, or null for independent package versions.</param>
    /// <returns>True when the package version belongs to the requested family.</returns>
    private static bool MatchesFrameworkVersion(NuGetVersion version, Version? frameworkVersion) =>
        frameworkVersion is null || (version.Major == frameworkVersion.Major && version.Minor == frameworkVersion.Minor);

    /// <summary>Retains explicitly configured reference-pack versions.</summary>
    /// <param name="downloads">Selected reference packages.</param>
    /// <param name="id">Reference-pack identifier.</param>
    /// <returns>True when the reference pack has been selected.</returns>
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

    /// <summary>Supplies the package contract for .NET Standard without overriding explicit pins.</summary>
    /// <param name="framework">Documentation framework.</param>
    /// <param name="dependencies">Direct package constraints.</param>
    private static void AddStandardDependency(NuGetFramework framework, List<LibraryDependency> dependencies)
    {
        var version = framework.Version.Major >= ExpandedStandardMajorVersion ? "2.0.3" : "1.6.1";
        AddImplicitDependency(StandardLibrary, version, dependencies);
    }

    /// <summary>Adds an implicit framework contract while retaining explicit package pins.</summary>
    /// <param name="id">Framework contract package.</param>
    /// <param name="version">Minimum contract version.</param>
    /// <param name="dependencies">Direct package constraints.</param>
    private static void AddImplicitDependency(string id, string version, List<LibraryDependency> dependencies)
    {
        for (var i = 0; i < dependencies.Count; i++)
        {
            if (dependencies[i].Name.Equals(id, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        dependencies.Add(PackageGraphRestore.Dependency(id, VersionRange.Parse(version)));
    }
}
