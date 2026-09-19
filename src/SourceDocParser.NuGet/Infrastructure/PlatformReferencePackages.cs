// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace SourceDocParser.NuGet.Infrastructure;

/// <summary>Resolves platform reference packs from configured NuGet sources.</summary>
internal static class PlatformReferencePackages
{
    /// <summary>The Windows projection package supplies managed Windows SDK contracts.</summary>
    private const string WindowsReferencePackage = "Microsoft.Windows.SDK.NET.Ref";

    /// <summary>The Windows platform name used by reference-pack identities.</summary>
    private const string WindowsPlatform = "Windows";

    /// <summary>The Android platform name used by reference-pack identities.</summary>
    private const string AndroidPlatform = "Android";

    /// <summary>The initial feature-band package provides stable workload manifests.</summary>
    private const string InitialFeatureBand = "100";

    /// <summary>Workload manifest archives contain this metadata file.</summary>
    private const string ManifestFileName = "WorkloadManifest.json";

    /// <summary>Stable reference declarations precede prerelease-only framework support.</summary>
    private const int VersionSelectionPasses = 2;

    /// <summary>Windows SDK projection packages describe Windows 10 or later.</summary>
    private const int WindowsProjectionMajor = 10;

    /// <summary>Resolved platform identities remain scoped to the sources of one NuGet session.</summary>
    private static readonly ConditionalWeakTable<PackageRestoreSession, ConcurrentDictionary<NuGetFramework, DownloadDependency>> _resolved = new();

    /// <summary>Adds platform reference packages without requiring an installed SDK or workload.</summary>
    /// <param name="session">Configured NuGet sources and package cache.</param>
    /// <param name="framework">Documentation target framework.</param>
    /// <param name="downloads">Targeting-pack downloads, including explicit pins.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing platform reference resolution.</returns>
    /// <exception cref="InvalidOperationException">No matching reference pack is available from the configured sources.</exception>
    internal static async Task AddDownloadsAsync(PackageRestoreSession session, NuGetFramework framework, List<DownloadDependency> downloads, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(framework);
        ArgumentNullException.ThrowIfNull(downloads);
        cancellationToken.ThrowIfCancellationRequested();
        var platform = GetPlatform(framework);
        if (platform is null || HasExplicitPack(downloads, framework, platform))
        {
            return;
        }

        var cache = _resolved.GetOrCreateValue(session);
        if (!cache.TryGetValue(framework, out var dependency))
        {
            dependency = platform is WindowsPlatform
                ? await ResolveWindowsAsync(session, framework, cancellationToken).ConfigureAwait(false)
                : await ResolveManifestAsync(session, framework, platform, cancellationToken).ConfigureAwait(false);
            _ = cache.TryAdd(framework, dependency);
        }

        downloads.Add(dependency);
    }

    /// <summary>Identifies platforms whose managed reference packages are published through NuGet.</summary>
    /// <param name="framework">Documentation target framework.</param>
    /// <returns>The package platform name, or null when no platform pack is required.</returns>
    private static string? GetPlatform(NuGetFramework framework) =>
        framework.Framework is not FrameworkConstants.FrameworkIdentifiers.NetCoreApp || !framework.HasPlatform
            ? null
            : framework.Platform.ToLowerInvariant() switch
        {
            "android" => AndroidPlatform,
            "ios" => "iOS",
            "maccatalyst" => "MacCatalyst",
            "macos" => "macOS",
            "tvos" => "tvOS",
            "windows" when framework.PlatformVersion.Major >= WindowsProjectionMajor => WindowsPlatform,
            _ => null,
        };

    /// <summary>Preserves an explicitly selected platform reference package.</summary>
    /// <param name="downloads">Configured targeting-pack downloads.</param>
    /// <param name="framework">Documentation target framework.</param>
    /// <param name="platform">Package platform name.</param>
    /// <returns>True when the caller supplied the platform pack.</returns>
    private static bool HasExplicitPack(List<DownloadDependency> downloads, NuGetFramework framework, string platform)
    {
        var expected = GetReferenceId(framework, platform);
        var generic = $"Microsoft.{platform}.Ref";
        for (var i = 0; i < downloads.Count; i++)
        {
            if (downloads[i].Name.Equals(expected, StringComparison.OrdinalIgnoreCase) || downloads[i].Name.Equals(generic, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns the reference-pack identity for a platform API version.</summary>
    /// <param name="framework">Documentation target framework.</param>
    /// <param name="platform">Package platform name.</param>
    /// <returns>The platform reference-pack identifier.</returns>
    private static string GetReferenceId(NuGetFramework framework, string platform)
    {
        var version = framework.PlatformVersion;
        if (platform is WindowsPlatform)
        {
            return WindowsReferencePackage;
        }

        if (platform is AndroidPlatform)
        {
            var suffix = version.Minor is 0 ? version.Major.ToString(System.Globalization.CultureInfo.InvariantCulture) : $"{version.Major}.{version.Minor}";
            return $"Microsoft.Android.Ref.{suffix}";
        }

        var target = new NuGetFramework(framework.Framework, framework.Version).GetShortFolderName();
        return $"Microsoft.{platform}.Ref.{target}_{version.Major}.{version.Minor}";
    }

    /// <summary>Resolves a mobile reference pack from published NuGet workload manifests.</summary>
    /// <param name="session">Configured package session.</param>
    /// <param name="framework">Documentation target framework.</param>
    /// <param name="platform">Package platform name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The exact reference-pack download.</returns>
    /// <exception cref="InvalidOperationException">No published manifest declares a matching reference pack.</exception>
    private static async Task<DownloadDependency> ResolveManifestAsync(PackageRestoreSession session, NuGetFramework framework, string platform, CancellationToken cancellationToken)
    {
        var prefix = $"Microsoft.NET.Sdk.{platform}.Manifest-{framework.Version.Major}.{framework.Version.Minor}.";
        var canonical = prefix + InitialFeatureBand;
        var resolved = await TryManifestFamilyAsync(session, canonical, framework, platform, cancellationToken).ConfigureAwait(false);
        if (resolved is not null)
        {
            return resolved;
        }

        var discovered = await session.FindPackageIdsAsync(prefix, cancellationToken).ConfigureAwait(false);
        var families = GetManifestFamilies(discovered, canonical, prefix);
        for (var i = 0; i < families.Length; i++)
        {
            resolved = await TryManifestFamilyAsync(session, families[i].Id, framework, platform, cancellationToken).ConfigureAwait(false);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        throw new InvalidOperationException($"No NuGet manifest '{prefix}' supplies reference packs for '{framework}'. Configure compatible referencePackages and feeds.");
    }

    /// <summary>Orders alternative manifest feature bands published by a package source.</summary>
    /// <param name="ids">Package identifiers discovered from the configured feeds.</param>
    /// <param name="canonical">The manifest identity considered first.</param>
    /// <param name="prefix">Manifest package family prefix.</param>
    /// <returns>Manifest identities ordered by descending feature band.</returns>
    private static (string Id, NuGetVersion Band)[] GetManifestFamilies(string[] ids, string canonical, string prefix)
    {
        var families = new List<(string Id, NuGetVersion Band)>(ids.Length);
        for (var i = 0; i < ids.Length; i++)
        {
            var id = ids[i];
            if (id.Equals(canonical, StringComparison.OrdinalIgnoreCase) || !id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || id.Contains(".Msi.", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (NuGetVersion.TryParse(id[prefix.Length..], out var band))
            {
                families.Add((id, band));
            }
        }

        var result = families.ToArray();
        Array.Sort(result, static (left, right) => right.Band.CompareTo(left.Band));
        return result;
    }

    /// <summary>Finds a matching reference declaration without downloading unrelated targeting packs.</summary>
    /// <param name="session">Configured package session.</param>
    /// <param name="id">Published manifest package identifier.</param>
    /// <param name="framework">Documentation target framework.</param>
    /// <param name="platform">Package platform name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching pack, or null when this manifest family does not supply it.</returns>
    private static async Task<DownloadDependency?> TryManifestFamilyAsync(
        PackageRestoreSession session,
        string id,
        NuGetFramework framework,
        string platform,
        CancellationToken cancellationToken)
    {
        NuGetVersion[] versions = [.. await session.GetVersionsAsync(id, cancellationToken).ConfigureAwait(false)];
        Array.Sort(versions);
        for (var pass = 0; pass < VersionSelectionPasses; pass++)
        {
            for (var i = versions.Length - 1; i >= 0; i--)
            {
                if (versions[i].IsPrerelease != (pass is 1))
                {
                    continue;
                }

                using var manifest = await session.DownloadAsync(id, versions[i].ToNormalizedString(), cancellationToken).ConfigureAwait(false);
                var reference = ReadManifestReference(manifest.PackageReader!, framework, platform);
                if (reference is null)
                {
                    continue;
                }

                await ValidateReferenceAsync(session, reference, framework, true, cancellationToken).ConfigureAwait(false);
                return new(reference.Id, new(reference.Version, true, reference.Version, true));
            }
        }

        return null;
    }

    /// <summary>Reads reference-pack declarations from a manifest archive.</summary>
    /// <param name="reader">Downloaded manifest archive.</param>
    /// <param name="framework">Documentation target framework.</param>
    /// <param name="platform">Package platform name.</param>
    /// <returns>The exact declared pack, or null when absent.</returns>
    private static PackageIdentity? ReadManifestReference(PackageReaderBase reader, NuGetFramework framework, string platform)
    {
        foreach (var path in reader.GetFiles())
        {
            if (!path.EndsWith($"/{ManifestFileName}", StringComparison.OrdinalIgnoreCase) && !path.Equals(ManifestFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var stream = reader.GetStream(path);
            using var document = JsonDocument.Parse(stream, new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (!document.RootElement.TryGetProperty("packs", out var packs))
            {
                continue;
            }

            var exact = GetReferenceId(framework, platform);
            PackageIdentity? generic = null;
            foreach (var pack in packs.EnumerateObject())
            {
                var identity = ReadPack(pack, framework, platform, exact);
                if (identity is not null && identity.Id.Equals(exact, StringComparison.OrdinalIgnoreCase))
                {
                    return identity;
                }

                generic ??= identity;
            }

            if (generic is not null)
            {
                return generic;
            }
        }

        return null;
    }

    /// <summary>Matches exact and legacy Apple reference-pack declarations.</summary>
    /// <param name="pack">A workload manifest pack entry.</param>
    /// <param name="framework">Documentation target framework.</param>
    /// <param name="platform">Package platform name.</param>
    /// <param name="expected">Exact reference-pack identifier.</param>
    /// <returns>The declared package identity, or null for another platform or package kind.</returns>
    private static PackageIdentity? ReadPack(JsonProperty pack, NuGetFramework framework, string platform, string expected)
    {
        if (!pack.Value.TryGetProperty("kind", out var kind) || !kind.ValueEquals("framework"u8)
            || !pack.Value.TryGetProperty("version", out var value) || !NuGetVersion.TryParse(value.GetString(), out var version))
        {
            return null;
        }

        if (pack.Name.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            return new(pack.Name, version);
        }

        var generic = $"Microsoft.{platform}.Ref";
        return platform is not AndroidPlatform && pack.Name.Equals(generic, StringComparison.OrdinalIgnoreCase)
            && version.Major == framework.PlatformVersion.Major && version.Minor == framework.PlatformVersion.Minor
            ? new(pack.Name, version)
            : null;
    }

    /// <summary>Uses package dependency groups to screen Windows projection versions before download.</summary>
    /// <param name="session">Configured package session.</param>
    /// <param name="framework">Documentation target framework.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The latest compatible Windows SDK projection package.</returns>
    /// <exception cref="InvalidOperationException">No compatible projection package is available.</exception>
    private static async Task<DownloadDependency> ResolveWindowsAsync(PackageRestoreSession session, NuGetFramework framework, CancellationToken cancellationToken)
    {
        NuGetVersion[] versions = [.. await session.GetVersionsAsync(WindowsReferencePackage, cancellationToken).ConfigureAwait(false)];
        Array.Sort(versions);
        for (var pass = 0; pass < VersionSelectionPasses; pass++)
        {
            for (var i = versions.Length - 1; i >= 0; i--)
            {
                var version = versions[i];
                if (version.IsPrerelease != (pass is 1) || !MatchesWindowsVersion(version, framework.PlatformVersion))
                {
                    continue;
                }

                var groups = await session.GetDependencyGroupsAsync(WindowsReferencePackage, version, cancellationToken).ConfigureAwait(false);
                if (!HasCompatibleDependencyGroup(groups, framework))
                {
                    continue;
                }

                var identity = new PackageIdentity(WindowsReferencePackage, version);
                await ValidateReferenceAsync(session, identity, framework, false, cancellationToken).ConfigureAwait(false);
                return new(identity.Id, new(version, true, version, true));
            }
        }

        throw new InvalidOperationException($"No '{WindowsReferencePackage}' matches '{framework}'. Check configured feeds or pin a compatible reference package.");
    }

    /// <summary>Matches the Windows SDK API build independently of projection package revision.</summary>
    /// <param name="version">Projection package version.</param>
    /// <param name="platform">Requested Windows API version.</param>
    /// <returns>True when both identify the same Windows SDK API build.</returns>
    private static bool MatchesWindowsVersion(NuGetVersion version, Version platform) => version.Major == platform.Major
        && version.Minor == platform.Minor && version.Version.Build == platform.Build;

    /// <summary>Checks the minimum .NET framework declared by a projection package.</summary>
    /// <param name="groups">NuGet dependency groups.</param>
    /// <param name="framework">Documentation target framework.</param>
    /// <returns>True when a dependency group is compatible or no framework constraint is declared.</returns>
    private static bool HasCompatibleDependencyGroup(PackageDependencyGroup[] groups, NuGetFramework framework)
    {
        if (groups.Length is 0)
        {
            return true;
        }

        var frameworks = new NuGetFramework[groups.Length];
        for (var i = 0; i < groups.Length; i++)
        {
            frameworks[i] = groups[i].TargetFramework;
        }

        return new FrameworkReducer().GetNearest(framework, frameworks) is not null;
    }

    /// <summary>Validates the framework of the selected reference assets.</summary>
    /// <param name="session">Configured package session.</param>
    /// <param name="identity">Selected reference-pack identity.</param>
    /// <param name="framework">Documentation target framework.</param>
    /// <param name="requireExactFramework">True for platform packs declared for a specific .NET release.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing reference validation.</returns>
    /// <exception cref="InvalidOperationException">The selected package has no usable reference assemblies for the target.</exception>
    private static async Task ValidateReferenceAsync(
        PackageRestoreSession session,
        PackageIdentity identity,
        NuGetFramework framework,
        bool requireExactFramework,
        CancellationToken cancellationToken)
    {
        using var package = await session.DownloadAsync(identity.Id, identity.Version.ToNormalizedString(), cancellationToken).ConfigureAwait(false);
        var reader = package.PackageReader!;
        var groups = new List<FrameworkSpecificGroup>(reader.GetItems("ref"));
        if (groups.Count is 0)
        {
            groups.AddRange(reader.GetLibItems());
        }

        var frameworks = new NuGetFramework[groups.Count];
        for (var i = 0; i < groups.Count; i++)
        {
            frameworks[i] = groups[i].TargetFramework;
        }

        var nearest = new FrameworkReducer().GetNearest(framework, frameworks);
        if (nearest is null || (requireExactFramework && !new NuGetFramework(framework.Framework, framework.Version).Equals(nearest)))
        {
            throw new InvalidOperationException($"Reference pack '{identity}' has no compatible assets for '{framework}'. Check its manifest or referencePackages pin.");
        }

        for (var i = 0; i < groups.Count; i++)
        {
            if (!groups[i].TargetFramework.Equals(nearest))
            {
                continue;
            }

            foreach (var path in groups[i].Items)
            {
                if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        throw new InvalidOperationException($"Reference pack '{identity}' has no compile assemblies for '{framework}'.");
    }
}
