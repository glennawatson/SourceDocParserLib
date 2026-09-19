// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NuGet.Commands;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.Packaging.Core;
using NuGet.ProjectModel;
using NuGet.RuntimeModel;
using NuGet.Versioning;
using SourceDocParser.Model;
using SourceDocParser.NuGet.Models;

namespace SourceDocParser.NuGet.Infrastructure;

/// <summary>Restores an independent PackageReference graph for a documentation root.</summary>
internal static class PackageGraphRestore
{
    /// <summary>The NuGet assets filename consumed by compilation.</summary>
    internal const string AssetsFileName = "project.assets.json";

    /// <summary>Initial space for a restore dependency-chain diagnostic.</summary>
    private const int DiagnosticCapacity = 256;

    /// <summary>Framework generations that support the PackageReference compatibility fallback.</summary>
    private const int CompatibilityFallbackMajorVersion = 2;

    /// <summary>Restores package dependencies and selects the graph's compile assets.</summary>
    /// <param name="session">Configured NuGet session.</param>
    /// <param name="root">Documentation package identity.</param>
    /// <param name="tfm">Documentation target framework.</param>
    /// <param name="config">Documentation selection and explicit pins.</param>
    /// <param name="outputDirectory">Directory for restore results.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The documentation assemblies and their resolved references.</returns>
    internal static async Task<AssemblyGroup> RestoreAsync(
        PackageRestoreSession session,
        PackageIdentity root,
        string tfm,
        PackageConfig config,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        var framework = NuGetFramework.ParseFolder(tfm);
        var dependencies = new List<LibraryDependency>(config.DependencyPins.Count + config.ReferencePackages.Length + 1) { Dependency(root.Id, new(root.Version, true, root.Version, true)), };
        foreach (var pin in config.DependencyPins)
        {
            if (!pin.Key.Equals(root.Id, StringComparison.OrdinalIgnoreCase))
            {
                dependencies.Add(Dependency(pin.Key, ParsePin(pin.Value)));
            }
        }

        var downloads = await PackageReferenceAssets.AddReferencesAsync(session, config, framework, dependencies, cancellationToken).ConfigureAwait(false);
        var target = CreateTarget(framework, tfm, dependencies, downloads);
        var spec = CreateSpec(session, target, config.RuntimeIdentifier, outputDirectory);
        var key = GetGraphKey(spec, session.Settings, config);
        var graphDirectory = Path.Combine(Path.GetFullPath(outputDirectory), "restore", root.Id.ToLowerInvariant(), root.Version.ToNormalizedString(), tfm, key);
        var projectPath = Path.Combine(graphDirectory, "documentation.csproj");
        spec.FilePath = projectPath;
        spec.RestoreMetadata.ProjectPath = projectPath;
        spec.RestoreMetadata.ProjectUniqueName = projectPath;
        spec.RestoreMetadata.OutputPath = graphDirectory;
        var graph = new DependencyGraphSpec();
        graph.AddProject(spec);
        graph.AddRestore(projectPath);
        var args = new RestoreArgs { CacheContext = session.Cache, Log = session.Logger, AllowNoOp = false, };
        args.PreLoadedRequestProviders.Add(new DependencyGraphSpecRequestProvider(new RestoreCommandProvidersCache(), graph, session.Settings));
        var results = await RestoreRunner.RunAsync(args, cancellationToken).ConfigureAwait(false);
        ValidateRestore(results, root, tfm, config.RuntimeIdentifier);

        var assetsPath = Path.Combine(graphDirectory, AssetsFileName);
        var assets = new LockFileFormat().Read(assetsPath);
        var group = RestoredPackageAssets.Read(assets, root.Id, framework, config.RuntimeIdentifier, assetsPath);
        await PackageReferenceAssets.AddFrameworkAssetsAsync(session, config, framework, root.Id, group.FallbackIndex, assets, cancellationToken).ConfigureAwait(false);
        await FrameworkCompatibilityReferences.AddAsync(session, framework, group.FallbackIndex, cancellationToken).ConfigureAwait(false);
        PackageMetadataReferences.Add(assets, root.Id, framework, config.RuntimeIdentifier, group.FallbackIndex);
        return group;
    }

    /// <summary>Creates a direct package constraint.</summary>
    /// <param name="id">Package identifier.</param>
    /// <param name="range">Version constraint.</param>
    /// <returns>The direct dependency.</returns>
    internal static LibraryDependency Dependency(string id, VersionRange range) => new() { LibraryRange = new(id, range, LibraryDependencyTarget.Package), };

    /// <summary>Parses an explicit pin or NuGet range.</summary>
    /// <param name="version">Version specification.</param>
    /// <returns>The direct constraint.</returns>
    internal static VersionRange ParsePin(string version) => NuGetVersion.TryParse(version, out var exact)
        ? new(exact, true, exact, true)
        : VersionRange.Parse(version);

    /// <summary>Creates a NuGet target with the PackageReference framework compatibility policy.</summary>
    /// <param name="framework">The documentation framework.</param>
    /// <param name="tfm">The original target name.</param>
    /// <param name="dependencies">Direct package constraints.</param>
    /// <param name="downloads">Reference-pack downloads.</param>
    /// <returns>The target passed to NuGet dependency and asset resolution.</returns>
    private static TargetFrameworkInformation CreateTarget(NuGetFramework framework, string tfm, List<LibraryDependency> dependencies, List<DownloadDependency> downloads)
    {
        var fallback = framework.Framework is FrameworkConstants.FrameworkIdentifiers.NetCoreApp or FrameworkConstants.FrameworkIdentifiers.NetStandard
            && framework.Version.Major >= CompatibilityFallbackMajorVersion;
        NuGetFramework[] imports = fallback
            ? [
                NuGetFramework.ParseFolder("net461"),
                NuGetFramework.ParseFolder("net462"),
                NuGetFramework.ParseFolder("net47"),
                NuGetFramework.ParseFolder("net471"),
                NuGetFramework.ParseFolder("net472"),
                NuGetFramework.ParseFolder("net48"),
                NuGetFramework.ParseFolder("net481"),
            ]
            : [];
        return new()
        {
            FrameworkName = fallback ? new AssetTargetFallbackFramework(framework, imports) : framework,
            TargetAlias = tfm,
            Dependencies = [.. dependencies],
            DownloadDependencies = [.. downloads],
            Imports = [.. imports],
            AssetTargetFallback = fallback,
            Warn = true,
        };
    }

    /// <summary>Rejects incomplete restore results with their dependency-chain diagnostics.</summary>
    /// <param name="results">Restore outcomes.</param>
    /// <param name="root">Documentation package.</param>
    /// <param name="tfm">Selected framework.</param>
    /// <param name="runtimeIdentifier">Selected runtime.</param>
    /// <exception cref="InvalidOperationException">NuGet cannot resolve the requested graph.</exception>
    private static void ValidateRestore(IReadOnlyList<RestoreSummary> results, PackageIdentity root, string tfm, string? runtimeIdentifier)
    {
        for (var i = 0; i < results.Count; i++)
        {
            if (results[i].Success)
            {
                continue;
            }

            var errors = new StringBuilder(DiagnosticCapacity);
            for (var e = 0; e < results[i].Errors.Count; e++)
            {
                _ = errors.Append(results[i].Errors[e].Code).Append(": ").AppendLine(results[i].Errors[e].Message);
            }

            throw new InvalidOperationException($"NuGet restore failed for {root.Id} {root.Version}, TFM {tfm}, RID {runtimeIdentifier ?? "none"}. {errors}");
        }
    }

    /// <summary>Creates the equivalent PackageReference restore input.</summary>
    /// <param name="session">Effective NuGet configuration.</param>
    /// <param name="target">Target framework and direct dependencies.</param>
    /// <param name="runtimeIdentifier">Optional runtime identifier.</param>
    /// <param name="outputDirectory">Restore output root.</param>
    /// <returns>The NuGet project model.</returns>
    private static PackageSpec CreateSpec(PackageRestoreSession session, TargetFrameworkInformation target, string? runtimeIdentifier, string outputDirectory)
    {
        var path = Path.Combine(Path.GetFullPath(outputDirectory), "documentation.csproj");
        return new([target])
        {
            Name = "documentation",
            FilePath = path,
            RuntimeGraph = runtimeIdentifier is { Length: > 0 } ? new RuntimeGraph([new RuntimeDescription(runtimeIdentifier)]) : RuntimeGraph.Empty,
            RestoreMetadata = new()
            {
                ProjectStyle = ProjectStyle.PackageReference,
                ProjectPath = path,
                ProjectName = "documentation",
                ProjectUniqueName = path,
                OutputPath = outputDirectory,
                PackagesPath = session.PackagesPath,
                Sources = session.Sources,
                ConfigFilePaths = [.. session.Settings.GetConfigFilePaths()],
                FallbackFolders = [.. SettingsUtility.GetFallbackPackageFolders(session.Settings)],
                OriginalTargetFrameworks = [target.TargetAlias],
                TargetFrameworks = [new(target.FrameworkName) { TargetAlias = target.TargetAlias }],
            },
        };
    }

    /// <summary>Identifies restore inputs including source mappings and package constraints.</summary>
    /// <param name="spec">Restore input.</param>
    /// <param name="settings">Effective configuration.</param>
    /// <param name="config">Explicit reference-asset selections.</param>
    /// <returns>A graph-specific directory name.</returns>
    private static string GetGraphKey(PackageSpec spec, ISettings settings, PackageConfig config)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var graph = new DependencyGraphSpec();
        graph.AddProject(spec);
        graph.AddRestore(spec.FilePath);
        hash.AppendData(Encoding.UTF8.GetBytes(graph.GetHash()));
        using var references = new MemoryStream();
        using (var writer = new Utf8JsonWriter(references))
        {
            writer.WriteStartArray();
            for (var i = 0; i < config.ReferencePackages.Length; i++)
            {
                var reference = config.ReferencePackages[i];
                writer.WriteStartArray();
                writer.WriteStringValue(reference.Id);
                writer.WriteStringValue(reference.Version);
                writer.WriteStringValue(reference.TargetTfm);
                writer.WriteStringValue(reference.PathPrefix);
                writer.WriteEndArray();
            }

            writer.WriteEndArray();
        }

        hash.AppendData(references.GetBuffer().AsSpan(0, (int)references.Length));
        foreach (var path in settings.GetConfigFilePaths())
        {
            hash.AppendData(Encoding.UTF8.GetBytes(path));
            hash.AppendData(File.ReadAllBytes(path));
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
