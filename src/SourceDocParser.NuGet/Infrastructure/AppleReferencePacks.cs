// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text.Json;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.Versioning;
using SourceDocParser.LibCompilation;

namespace SourceDocParser.NuGet.Infrastructure;

/// <summary>Supplies Apple reference packs declared by installed workload manifests.</summary>
internal static class AppleReferencePacks
{
    /// <summary>The workload manifest filename shared by SDK feature bands.</summary>
    private const string ManifestFileName = "WorkloadManifest.json";

    /// <summary>Apple targeting-pack identifiers contain major and minor platform versions.</summary>
    private const int PlatformVersionComponents = 2;

    /// <summary>Adds a reference-only Apple targeting pack without installing a workload.</summary>
    /// <param name="session">Configured package session and diagnostics.</param>
    /// <param name="framework">Documentation target framework.</param>
    /// <param name="downloads">Explicit and inferred targeting-pack downloads.</param>
    internal static void AddDownloads(PackageRestoreSession session, NuGetFramework framework, List<DownloadDependency> downloads)
    {
        var id = GetPackId(framework);
        if (id is null)
        {
            return;
        }

        for (var i = 0; i < downloads.Count; i++)
        {
            if (downloads[i].Name.Equals(id, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        var dependency = FindDownload(DotNetSdkLocator.EnumerateInstallRoots(), framework);
        if (dependency is null)
        {
            session.Logger.LogWarning($"No installed workload manifest declares reference pack '{id}' for '{framework}'. Configure its exact version in referencePackages to resolve Apple APIs.");
            return;
        }

        downloads.Add(dependency);
    }

    /// <summary>Finds an exact framework pack declaration for a .NET and Apple platform pair.</summary>
    /// <param name="installRoots">.NET installation directories containing workload manifests.</param>
    /// <param name="framework">Documentation target framework.</param>
    /// <returns>The declared reference-pack version, or null when no matching declaration exists.</returns>
    internal static DownloadDependency? FindDownload(IReadOnlyList<string> installRoots, NuGetFramework framework)
    {
        var id = GetPackId(framework);
        if (id is null)
        {
            return null;
        }

        NuGetVersion? selected = null;
        for (var r = 0; r < installRoots.Count; r++)
        {
            var manifests = Path.Combine(installRoots[r], "sdk-manifests");
            if (!Directory.Exists(manifests))
            {
                continue;
            }

            var bands = Directory.GetDirectories(manifests);
            for (var b = 0; b < bands.Length; b++)
            {
                var manifest = Path.Combine(bands[b], $"microsoft.net.sdk.{framework.Platform.ToLowerInvariant()}");
                if (!Directory.Exists(manifest))
                {
                    continue;
                }

                var files = Directory.GetFiles(manifest, ManifestFileName, SearchOption.AllDirectories);
                selected = SelectDeclaredVersion(files, id, selected);
            }
        }

        return selected is null ? null : new(id, new(selected, true, selected, true));
    }

    /// <summary>Identifies the reference-pack family associated with the requested Apple framework.</summary>
    /// <param name="framework">Documentation target framework.</param>
    /// <returns>The exact reference-pack identifier, or null for other platforms.</returns>
    private static string? GetPackId(NuGetFramework framework)
    {
        var platform = framework.Platform.ToLowerInvariant() switch
        {
            "ios" => "iOS",
            "maccatalyst" => "MacCatalyst",
            "macos" => "macOS",
            "tvos" => "tvOS",
            _ => null,
        };
        if (platform is null || framework.Framework is not FrameworkConstants.FrameworkIdentifiers.NetCoreApp)
        {
            return null;
        }

        var target = new NuGetFramework(framework.Framework, framework.Version).GetShortFolderName();
        return $"Microsoft.{platform}.Ref.{target}_{framework.PlatformVersion.ToString(PlatformVersionComponents)}";
    }

    /// <summary>Reads matching reference-pack versions from workload declarations.</summary>
    /// <param name="files">Workload manifest paths.</param>
    /// <param name="id">Exact targeting-pack identifier.</param>
    /// <param name="selected">Highest matching declaration encountered so far.</param>
    /// <returns>The highest version explicitly declared for the same targeting-pack identifier.</returns>
    private static NuGetVersion? SelectDeclaredVersion(string[] files, string id, NuGetVersion? selected)
    {
        for (var i = 0; i < files.Length; i++)
        {
            using var stream = File.OpenRead(files[i]);
            using var document = JsonDocument.Parse(stream, new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (!document.RootElement.TryGetProperty("packs", out var packs))
            {
                continue;
            }

            foreach (var pack in packs.EnumerateObject())
            {
                var version = ReadReferenceVersion(pack, id);
                if (version is not null && (selected is null || version > selected))
                {
                    selected = version;
                }
            }
        }

        return selected;
    }

    /// <summary>Reads the version of an exact reference-pack declaration.</summary>
    /// <param name="pack">Workload pack declaration.</param>
    /// <param name="id">Exact reference-pack identifier.</param>
    /// <returns>The declared version, or null for other pack kinds or identities.</returns>
    private static NuGetVersion? ReadReferenceVersion(JsonProperty pack, string id)
    {
        if (!pack.Name.Equals(id, StringComparison.OrdinalIgnoreCase)
            || !pack.Value.TryGetProperty("kind", out var kind)
            || !kind.ValueEquals("framework"u8)
            || !pack.Value.TryGetProperty("version", out var version))
        {
            return null;
        }

        return NuGetVersion.TryParse(version.GetString(), out var parsed) ? parsed : null;
    }
}
