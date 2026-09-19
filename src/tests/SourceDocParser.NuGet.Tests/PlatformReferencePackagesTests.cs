// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.Protocol;
using NuGet.Versioning;
using SourceDocParser.NuGet.Infrastructure;
using SourceDocParser.TestHelpers;

namespace SourceDocParser.NuGet.Tests;

/// <summary>Verifies platform reference resolution through NuGet-only fixtures.</summary>
public sealed class PlatformReferencePackagesTests
{
    /// <summary>The Windows projection package identity.</summary>
    private const string WindowsSdk = "Microsoft.Windows.SDK.NET.Ref";

    /// <summary>The Windows API build used by projection fixtures.</summary>
    private const int WindowsApiBuild = 19_041;

    /// <summary>The base framework used by portable fixture assets.</summary>
    private const string Net8 = "net8.0";

    /// <summary>The Android workload family.</summary>
    private const string Android = "Android";

    /// <summary>The Android API 34 reference-pack identity.</summary>
    private const string Android34 = "Microsoft.Android.Ref.34";

    /// <summary>The .NET 8 Android framework used by negative and cache cases.</summary>
    private const string Net8Android = "net8.0-android34.0";

    /// <summary>An Android API level can use a pack version from a different release generation.</summary>
    /// <param name="framework">Base framework version declared by the manifest family.</param>
    /// <param name="apiLevel">Requested Android API level.</param>
    /// <param name="release">Manifest and reference-pack release generation.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments("8.0", 34, 34)]
    [Arguments("9.0", 35, 35)]
    [Arguments("9.0", 36, 35)]
    [Arguments("10.0", 36, 36)]
    public async Task AndroidUsesDeclaredPackVersionAndReferenceFramework(string framework, int apiLevel, int release)
    {
        using var fixture = new PackageFeed();
        var version = $"{release}.0.{fixture.Revision}";
        var reference = $"Microsoft.Android.Ref.{apiLevel}";
        fixture.AddManifest(Android, framework, version, reference, version);
        fixture.AddPackage(reference, version, $"ref/net{framework}/Mono.Android.dll", string.Empty);
        using var session = fixture.CreateSession();
        var downloads = new List<DownloadDependency>();

        await PlatformReferencePackages.AddDownloadsAsync(session, NuGetFramework.ParseFolder($"net{framework}-android{apiLevel}.0"), downloads, CancellationToken.None);

        await Assert.That(downloads.Count).IsEqualTo(1);
        await Assert.That(downloads[0].Name).IsEqualTo(reference);
        await Assert.That(downloads[0].VersionRange).IsEqualTo(VersionRange.Parse($"[{version}]"));
    }

    /// <summary>Apple reference manifests retain generic and framework-qualified package identities.</summary>
    /// <param name="platform">Manifest platform name.</param>
    /// <param name="framework">Base .NET version.</param>
    /// <param name="platformVersion">Apple platform API version.</param>
    /// <param name="qualified">True when the reference package includes its framework and OS version.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments("iOS", "8.0", "17.0", false)]
    [Arguments("iOS", "9.0", "18.0", true)]
    [Arguments("iOS", "10.0", "26.0", true)]
    [Arguments("macOS", "8.0", "14.0", false)]
    public async Task AppleUsesManifestIdentityAcrossNamingGenerations(string platform, string framework, string platformVersion, bool qualified)
    {
        using var fixture = new PackageFeed();
        var version = $"{platformVersion}.{fixture.Revision}";
        var reference = qualified ? $"Microsoft.{platform}.Ref.net{framework}_{platformVersion}" : $"Microsoft.{platform}.Ref";
        fixture.AddManifest(platform, framework, version, reference, version);
        fixture.AddPackage(reference, version, $"ref/net{framework}/Platform.dll", string.Empty);
        using var session = fixture.CreateSession();
        var downloads = new List<DownloadDependency>();

        await PlatformReferencePackages.AddDownloadsAsync(session, NuGetFramework.ParseFolder($"net{framework}-{platform.ToLowerInvariant()}{platformVersion}"), downloads, CancellationToken.None);

        await Assert.That(downloads.Count).IsEqualTo(1);
        await Assert.That(downloads[0].Name).IsEqualTo(reference);
        await Assert.That(downloads[0].VersionRange).IsEqualTo(VersionRange.Parse($"[{version}]"));
    }

    /// <summary>An explicit reference-only pin does not require a discoverable workload manifest.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task ExplicitPlatformReferencePinTakesPrecedence()
    {
        using var fixture = new PackageFeed();
        using var session = fixture.CreateSession();
        var pin = new DownloadDependency("Microsoft.iOS.Ref", VersionRange.Parse("[17.0.1]"));
        var downloads = new List<DownloadDependency> { pin };

        await PlatformReferencePackages.AddDownloadsAsync(session, NuGetFramework.ParseFolder("net8.0-ios17.0"), downloads, CancellationToken.None);

        await Assert.That(downloads.Count).IsEqualTo(1);
        await Assert.That(downloads[0]).IsSameReferenceAs(pin);
    }

    /// <summary>A manifest cannot supply reference assets for a different .NET generation.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task IncompatibleReferenceAssetFrameworkIsRejected()
    {
        using var fixture = new PackageFeed();
        var version = $"34.0.{fixture.Revision}";
        fixture.AddManifest(Android, "8.0", version, Android34, version);
        fixture.AddPackage(Android34, version, "ref/net9.0/Mono.Android.dll", string.Empty);
        using var session = fixture.CreateSession();

        await Assert.That(async () =>
        {
            await PlatformReferencePackages.AddDownloadsAsync(session, NuGetFramework.ParseFolder(Net8Android), [], CancellationToken.None);
        }).Throws<InvalidOperationException>().WithMessageContaining("compatible assets");
    }

    /// <summary>Windows projection metadata rejects incompatible .NET versions before archive acquisition.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task WindowsProjectionScreensDependencyGroupsBeforeDownload()
    {
        using var fixture = new PackageFeed();
        var compatible = new NuGetVersion(new Version(10, 0, WindowsApiBuild, fixture.Revision));
        var incompatible = new NuGetVersion(new Version(10, 0, WindowsApiBuild, fixture.Revision + 1));
        fixture.AddPackage(WindowsSdk, compatible.ToNormalizedString(), "lib/net8.0/Windows.dll", string.Empty, Net8);
        fixture.AddPackage(WindowsSdk, incompatible.ToNormalizedString(), "lib/net9.0/Windows.dll", string.Empty, "net9.0");
        using var session = fixture.CreateSession();
        var downloads = new List<DownloadDependency>();

        await PlatformReferencePackages.AddDownloadsAsync(session, NuGetFramework.ParseFolder("net8.0-windows10.0.19041.0"), downloads, CancellationToken.None);

        await Assert.That(downloads.Count).IsEqualTo(1);
        await Assert.That(downloads[0].VersionRange.MinVersion).IsEqualTo(compatible);
        using var rejected = GlobalPackagesFolderUtility.GetPackage(new(WindowsSdk, incompatible), session.PackagesPath);
        await Assert.That(rejected).IsNull();
    }

    /// <summary>Reference-pack cache results preserve the identity selected from the configured feed.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task CachedResolutionMatchesUncachedSelection()
    {
        using var fixture = new PackageFeed();
        var version = $"34.0.{fixture.Revision}";
        fixture.AddManifest(Android, "8.0", version, Android34, version);
        fixture.AddPackage(Android34, version, "ref/net8.0/Mono.Android.dll", string.Empty);
        using var session = fixture.CreateSession();
        var framework = NuGetFramework.ParseFolder(Net8Android);
        var first = new List<DownloadDependency>();
        var second = new List<DownloadDependency>();

        await PlatformReferencePackages.AddDownloadsAsync(session, framework, first, CancellationToken.None);
        await PlatformReferencePackages.AddDownloadsAsync(session, framework, second, CancellationToken.None);

        await Assert.That(first.Count).IsEqualTo(1);
        await Assert.That(second.Count).IsEqualTo(1);
        await Assert.That(second[0].Name).IsEqualTo(first[0].Name);
        await Assert.That(second[0].VersionRange).IsEqualTo(first[0].VersionRange);

        using var freshSession = fixture.CreateSession();
        var uncached = new List<DownloadDependency>();
        await PlatformReferencePackages.AddDownloadsAsync(freshSession, framework, uncached, CancellationToken.None);
        await Assert.That(uncached[0].Name).IsEqualTo(first[0].Name);
        await Assert.That(uncached[0].VersionRange).IsEqualTo(first[0].VersionRange);
    }

    /// <summary>Workload runtime and SDK entries do not become metadata reference downloads.</summary>
    /// <param name="reference">Manifest entry identity.</param>
    /// <param name="kind">Manifest pack classification.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments("Microsoft.Android.Runtime.android-arm64", "framework")]
    [Arguments(Android34, "sdk")]
    public async Task RuntimeAndSdkPacksAreNotSelected(string reference, string kind)
    {
        using var fixture = new PackageFeed();
        var version = $"34.0.{fixture.Revision}";
        fixture.AddManifest(Android, "8.0", version, reference, version, kind);
        using var session = fixture.CreateSession();

        await Assert.That(async () =>
        {
            await PlatformReferencePackages.AddDownloadsAsync(session, NuGetFramework.ParseFolder(Net8Android), [], CancellationToken.None);
        }).Throws<InvalidOperationException>().WithMessageContaining("No NuGet manifest");
    }

    /// <summary>A stable reference declaration takes precedence over a newer preview in the same family.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task StableManifestPrecedesNewerPrerelease()
    {
        using var fixture = new PackageFeed();
        var stable = $"34.0.{fixture.Revision}";
        var preview = $"34.0.{fixture.Revision + 1}-preview.1";
        fixture.AddManifest(Android, "8.0", stable, Android34, stable);
        fixture.AddManifest(Android, "8.0", preview, Android34, preview);
        fixture.AddPackage(Android34, stable, "ref/net8.0/Mono.Android.dll", string.Empty);
        using var session = fixture.CreateSession();
        var downloads = new List<DownloadDependency>();

        await PlatformReferencePackages.AddDownloadsAsync(session, NuGetFramework.ParseFolder(Net8Android), downloads, CancellationToken.None);

        await Assert.That(downloads[0].VersionRange.MinVersion).IsEqualTo(NuGetVersion.Parse(stable));
    }

    /// <summary>Cancellation is honored before reference catalogs are queried.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    public async Task CanceledResolutionDoesNotAcquirePackages()
    {
        using var fixture = new PackageFeed();
        using var session = fixture.CreateSession();

        await Assert.That(async () =>
        {
            await PlatformReferencePackages.AddDownloadsAsync(session, NuGetFramework.ParseFolder(Net8), [], new(true));
        }).Throws<OperationCanceledException>();
    }

    /// <summary>Owns a local feed and isolated package identities.</summary>
    private sealed class PackageFeed : IDisposable
    {
        /// <summary>Revisions beyond published platform pack versions isolate fixture archives.</summary>
        private const int MinimumFixtureRevision = 100_000;

        /// <summary>The temporary feed and configuration directory.</summary>
        private readonly ScratchDirectory _directory = new("platform-reference-packages");

        /// <summary>The local package source.</summary>
        private readonly string _feed;

        /// <summary>A manifest identifier suffix unique to this fixture.</summary>
        private readonly string _suffix = Guid.NewGuid().ToString("N");

        /// <summary>Initializes a new instance of the <see cref="PackageFeed"/> class.</summary>
        public PackageFeed()
        {
            _feed = Directory.CreateDirectory(Path.Combine(_directory.Path, "feed")).FullName;
            var source = new XElement("add", new XAttribute("key", "fixture"), new XAttribute("value", _feed));
            var sources = new XElement("packageSources", new XElement("clear"), source);
            new XDocument(new XElement("configuration", sources)).Save(Path.Combine(_directory.Path, "NuGet.Config"));
        }

        /// <summary>Gets a package revision that does not collide with published reference packages.</summary>
        public int Revision { get; } = RandomNumberGenerator.GetInt32(MinimumFixtureRevision, int.MaxValue - 1);

        /// <summary>Creates a NuGet session restricted to the fixture feed.</summary>
        /// <returns>The configured session.</returns>
        public PackageRestoreSession CreateSession() => new(_directory.Path, NullLogger.Instance);

        /// <summary>Adds a small NuGet workload manifest.</summary>
        /// <param name="platform">Platform manifest family.</param>
        /// <param name="framework">Base .NET framework version.</param>
        /// <param name="version">Manifest package version.</param>
        /// <param name="reference">Declared reference-pack identifier.</param>
        /// <param name="referenceVersion">Declared reference-pack version.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddManifest(string platform, string framework, string version, string reference, string referenceVersion) =>
            AddManifest(platform, framework, version, reference, referenceVersion, "framework");

        /// <summary>Adds a manifest with a caller-selected pack classification.</summary>
        /// <param name="platform">Platform manifest family.</param>
        /// <param name="framework">Base .NET framework version.</param>
        /// <param name="version">Manifest package version.</param>
        /// <param name="reference">Declared pack identifier.</param>
        /// <param name="referenceVersion">Declared pack version.</param>
        /// <param name="kind">Pack classification.</param>
        public void AddManifest(string platform, string framework, string version, string reference, string referenceVersion, string kind)
        {
            var id = $"Microsoft.NET.Sdk.{platform}.Manifest-{framework}.100-fixture.{_suffix}";
            var manifest = new JsonObject { ["packs"] = new JsonObject { [reference] = new JsonObject { [nameof(kind)] = kind, [nameof(version)] = referenceVersion }, }, };
            AddPackage(id, version, "data/WorkloadManifest.json", manifest.ToJsonString());
        }

        /// <summary>Adds a NuGet archive containing a reference or manifest asset.</summary>
        /// <param name="id">Package identifier.</param>
        /// <param name="version">Package version.</param>
        /// <param name="asset">Package-relative asset path.</param>
        /// <param name="content">Manifest content; ignored for assembly assets.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddPackage(string id, string version, string asset, string content) => AddPackage(id, version, asset, content, null);

        /// <summary>Adds an archive with a declared .NET dependency group.</summary>
        /// <param name="id">Package identifier.</param>
        /// <param name="version">Package version.</param>
        /// <param name="asset">Package-relative asset path.</param>
        /// <param name="content">Manifest content; ignored for assembly assets.</param>
        /// <param name="dependencyFramework">The package's minimum framework, or null for none.</param>
        public void AddPackage(string id, string version, string asset, string content, string? dependencyFramework)
        {
            using var archive = ZipFile.Open(Path.Combine(_feed, $"{id}.{version}.nupkg"), ZipArchiveMode.Create);
            using (var stream = archive.CreateEntry($"{id}.nuspec").Open())
            {
                var metadata = new XElement(
                    "metadata",
                    new XElement(nameof(id), id),
                    new XElement(nameof(version), version),
                    new XElement("authors", "Fixture"),
                    new XElement("description", "Platform reference fixture."));
                if (dependencyFramework is not null)
                {
                    metadata.Add(new XElement("dependencies", new XElement("group", new XAttribute("targetFramework", dependencyFramework))));
                }

                new XDocument(new XElement("package", metadata)).Save(stream);
            }

            using var output = archive.CreateEntry(asset).Open();
            if (asset.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                using var input = File.OpenRead(typeof(PlatformReferencePackagesTests).Assembly.Location);
                input.CopyTo(output);
            }
            else
            {
                using var writer = new StreamWriter(output);
                writer.Write(content);
            }
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose() => _directory.Dispose();
    }
}
