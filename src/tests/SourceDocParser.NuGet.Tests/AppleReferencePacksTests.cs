// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.Versioning;
using SourceDocParser.NuGet.Infrastructure;

namespace SourceDocParser.NuGet.Tests;

/// <summary>Verifies reference-only Apple workload acquisition from declared pack identities.</summary>
public sealed class AppleReferencePacksTests
{
    /// <summary>The framework-pack version declared by the fixture manifest.</summary>
    private const string PackVersion = "26.0.123";

    /// <summary>The SDK workload manifest filename.</summary>
    private const string ManifestFileName = "WorkloadManifest.json";

    /// <summary>The reference-pack kind in workload manifests.</summary>
    private const string FrameworkKind = "framework";

    /// <summary>The manifest property carrying a pack version.</summary>
    private const string VersionProperty = "version";

    /// <summary>Each Apple platform selects its exact .NET and platform reference pack.</summary>
    /// <param name="platform">Target framework platform identifier.</param>
    /// <param name="packPlatform">Canonical platform spelling in pack identifiers.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments("ios", "iOS")]
    [Arguments("maccatalyst", "MacCatalyst")]
    [Arguments("macos", "macOS")]
    [Arguments("tvos", "tvOS")]
    public async Task FindDownloadMatchesExactFrameworkAndPlatform(string platform, string packPlatform)
    {
        using var root = new ScratchDirectory();
        var id = $"Microsoft.{packPlatform}.Ref.net10.0_26.0";
        var manifest = Directory.CreateDirectory(Path.Combine(root.Path, "sdk-manifests", "10.0.100", $"microsoft.net.sdk.{platform}", PackVersion)).FullName;
        var document = new JsonObject
        {
            ["packs"] = new JsonObject
            {
                [id] = new JsonObject { ["kind"] = FrameworkKind, [VersionProperty] = PackVersion },
                [$"Microsoft.{packPlatform}.Ref.net11.0_26.0"] = new JsonObject { ["kind"] = FrameworkKind, [VersionProperty] = "26.0.999" },
                [$"Microsoft.{packPlatform}.Ref.net10.0_26.2"] = new JsonObject { ["kind"] = FrameworkKind, [VersionProperty] = "26.2.999" },
            },
        };
        await File.WriteAllTextAsync(Path.Combine(manifest, ManifestFileName), document.ToJsonString());

        var result = AppleReferencePacks.FindDownload([root.Path], NuGetFramework.ParseFolder($"net10.0-{platform}26.0"));

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Name).IsEqualTo(id);
        await Assert.That(result.VersionRange).IsEqualTo(VersionRange.Parse($"[{PackVersion}]"));
    }

    /// <summary>Workload SDK and runtime packages cannot substitute for reference packages.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task FindDownloadDoesNotAcquireSdkOrRuntimePacks()
    {
        using var root = new ScratchDirectory();
        var manifest = Directory.CreateDirectory(Path.Combine(root.Path, "sdk-manifests", "10.0.100", "microsoft.net.sdk.ios", PackVersion)).FullName;
        await File.WriteAllTextAsync(Path.Combine(manifest, ManifestFileName), """
            { "packs": {
              "Microsoft.iOS.Sdk.net10.0_26.0": { "kind": "sdk", "version": "26.0.123" },
              "Microsoft.iOS.Runtime.ios-arm64.net10.0_26.0": { "kind": "framework", "version": "26.0.123" },
            } }
            """);

        var result = AppleReferencePacks.FindDownload([root.Path], NuGetFramework.ParseFolder("net10.0-ios26.0"));

        await Assert.That(result).IsNull();
    }

    /// <summary>Explicit reference-pack pins take precedence over workload metadata.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task AddDownloadsPreservesExplicitReferencePin()
    {
        using var session = new PackageRestoreSession(Path.GetTempPath(), NullLogger.Instance);
        var explicitPin = new DownloadDependency("Microsoft.iOS.Ref.net10.0_26.0", VersionRange.Parse("[26.0.1]"));
        var downloads = new List<DownloadDependency> { explicitPin };

        AppleReferencePacks.AddDownloads(session, NuGetFramework.ParseFolder("net10.0-ios26.0"), downloads);

        await Assert.That(downloads.Count).IsEqualTo(1);
        await Assert.That(downloads[0]).IsSameReferenceAs(explicitPin);
    }

    /// <summary>Owns synthetic SDK workload manifests.</summary>
    private sealed class ScratchDirectory : IDisposable
    {
        /// <summary>Initializes a new instance of the <see cref="ScratchDirectory"/> class.</summary>
        public ScratchDirectory() => Directory.CreateDirectory(Path);

        /// <summary>Gets the fixture installation root.</summary>
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose() => Directory.Delete(Path, true);
    }
}
