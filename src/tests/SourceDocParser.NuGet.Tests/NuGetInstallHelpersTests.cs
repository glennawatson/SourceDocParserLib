// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SourceDocParser.NuGet.Infrastructure;
using SourceDocParser.NuGet.Models;

namespace SourceDocParser.NuGet.Tests;

/// <summary>Pins the installation helpers in <see cref="NuGetInstallHelpers"/>.</summary>
public class NuGetInstallHelpersTests
{
    /// <summary>Fixture value for NupkgMetadata.</summary>
    private const string NupkgMetadata = ".nupkg.metadata";

    /// <summary>Fixture value for NugetOrg.</summary>
    private const string NugetOrg = "nuget.org";

    /// <summary>Fixture value for HttpsApiNugetOrgV3IndexJson.</summary>
    private const string HttpsApiNugetOrgV3IndexJson = "https://api.nuget.org/v3/index.json";

    /// <summary>Fixture value for HttpsFlatExample.</summary>
    private const string HttpsFlatExample = "https://flat.example/";

    /// <summary>Fixture value for NugetFlatContainerOverride.</summary>
    private const string NugetFlatContainerOverride = "NUGET_FLAT_CONTAINER_OVERRIDE";

    /// <summary>Fixture value for Version100.</summary>
    private const string Version100 = "1.0.0";

    /// <summary>Expected fixture value used by WriteNupkgMetadataAsyncWritesValidJson.</summary>
    private const int WriteNupkgMetadataAsyncWritesValidJsonExpectedValue = 2;

    /// <summary>ComputeContentHashAsync returns the SHA-512 / Base64 hash of a file's content.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ComputeContentHashAsyncReturnsCorrectHash()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var content = "Hello NuGet!"u8.ToArray();
            await File.WriteAllBytesAsync(tempFile, content);

            var expectedHash = Convert.ToBase64String(SHA512.HashData(content));
            var actualHash = await NuGetInstallHelpers.ComputeContentHashAsync(tempFile, CancellationToken.None);

            await Assert.That(actualHash).IsEqualTo(expectedHash);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    /// <summary>WriteNupkgMetadataAsync writes a valid JSON marker file with the package hash and source.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task WriteNupkgMetadataAsyncWritesValidJson()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"sdp-metadata-{Guid.NewGuid():N}");
        var tempNupkg = Path.Combine(tempDir, "test.nupkg");
        try
        {
            _ = Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(tempNupkg, "fake nupkg content");

            var source = new PackageSource("TestFeed", "https://example.com/nuget");
            await NuGetInstallHelpers.WriteNupkgMetadataAsync(tempDir, tempNupkg, source, CancellationToken.None);

            var metadataPath = Path.Combine(tempDir, NupkgMetadata);
            await Assert.That(File.Exists(metadataPath)).IsTrue();

            var json = await File.ReadAllTextAsync(metadataPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            await Assert.That(root.GetProperty("version"u8).GetInt32()).IsEqualTo(WriteNupkgMetadataAsyncWritesValidJsonExpectedValue);
            await Assert.That(root.GetProperty("source"u8).GetString()).IsEqualTo(source.Url);
            await Assert.That(root.GetProperty("contentHash"u8).GetString()).IsNotNull();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    /// <summary>GetFlatContainerUrlAsync returns the cached value without any HTTP roundtrip.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetFlatContainerUrlReturnsCachedValue()
    {
        var feed = new RecordingFeedHttpClient();
        var source = new PackageSource(NugetOrg, HttpsApiNugetOrgV3IndexJson);
        var cache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { [source.Key] = HttpsFlatExample, };

        var url = await NuGetInstallHelpers.GetFlatContainerUrlAsync(
            source,
            credentials: [],
            feed,
            cache,
            CancellationToken.None);

        await Assert.That(url).IsEqualTo(HttpsFlatExample);
        await Assert.That(feed.ServiceIndexCalls).IsEqualTo(0);
    }

    /// <summary>GetFlatContainerUrlAsync honours the env-var override and skips the HTTP call.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetFlatContainerUrlHonoursEnvOverride()
    {
        var prior = Environment.GetEnvironmentVariable(NugetFlatContainerOverride);
        try
        {
            Environment.SetEnvironmentVariable(NugetFlatContainerOverride, "https://override.example");
            var feed = new RecordingFeedHttpClient();
            var source = new PackageSource(NugetOrg, HttpsApiNugetOrgV3IndexJson);
            var cache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            var url = await NuGetInstallHelpers.GetFlatContainerUrlAsync(source, [], feed, cache, CancellationToken.None);

            await Assert.That(url).IsEqualTo("https://override.example/");
            await Assert.That(cache[source.Key]).IsEqualTo("https://override.example/");
            await Assert.That(feed.ServiceIndexCalls).IsEqualTo(0);
        }
        finally
        {
            Environment.SetEnvironmentVariable(NugetFlatContainerOverride, prior);
        }
    }

    /// <summary>GetFlatContainerUrlAsync reads the service-index when no cache + no override.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetFlatContainerUrlReadsServiceIndex()
    {
        var prior = Environment.GetEnvironmentVariable(NugetFlatContainerOverride);
        try
        {
            Environment.SetEnvironmentVariable(NugetFlatContainerOverride, null);
            var feed = new RecordingFeedHttpClient { ServiceIndexBody = """{"version":"3.0.0","resources":[{"@id":"https://flat.example/","@type":"PackageBaseAddress/3.0.0"}]}""", };
            var source = new PackageSource(NugetOrg, HttpsApiNugetOrgV3IndexJson);
            var cache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            var url = await NuGetInstallHelpers.GetFlatContainerUrlAsync(source, [], feed, cache, CancellationToken.None);

            await Assert.That(url).IsEqualTo(HttpsFlatExample);
            await Assert.That(cache[source.Key]).IsEqualTo(HttpsFlatExample);
            await Assert.That(feed.ServiceIndexCalls).IsEqualTo(1);
        }
        finally
        {
            Environment.SetEnvironmentVariable(NugetFlatContainerOverride, prior);
        }
    }

    /// <summary>TryInstallFromSourceAsync returns false when the feed has no flat-container endpoint.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryInstallFromSourceReturnsFalseWhenNoFlatContainer()
    {
        var feed = new RecordingFeedHttpClient { ServiceIndexBody = """{"version":"3.0.0","resources":[]}""", };
        var source = new PackageSource(NugetOrg, HttpsApiNugetOrgV3IndexJson);
        var cache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        var result = await NuGetInstallHelpers.TryInstallFromSourceAsync(
            source,
            CreateInstallRequest(
                source,
                feed,
                cache,
                "Foo",
                Version100,
                Path.Combine(Path.GetTempPath(), $"sdp-{Guid.NewGuid():N}")));

        await Assert.That(result).IsFalse();
    }

    /// <summary>TryInstallFromSourceAsync returns false when the source 404s the package.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryInstallFromSourceReturnsFalseOn404()
    {
        var feed = new RecordingFeedHttpClient { ServiceIndexBody = """{"version":"3.0.0","resources":[{"@id":"https://flat.example/","@type":"PackageBaseAddress/3.0.0"}]}""", NupkgResponse = null, };
        var source = new PackageSource(NugetOrg, HttpsApiNugetOrgV3IndexJson);
        var cache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        var result = await NuGetInstallHelpers.TryInstallFromSourceAsync(
            source,
            CreateInstallRequest(
                source,
                feed,
                cache,
                "Foo",
                Version100,
                Path.Combine(Path.GetTempPath(), $"sdp-{Guid.NewGuid():N}")));

        await Assert.That(result).IsFalse();
    }

    /// <summary>TryInstallFromSourceAsync downloads, extracts, and writes the metadata marker.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryInstallFromSourceInstallsAndWritesMetadata()
    {
        var prior = Environment.GetEnvironmentVariable(NugetFlatContainerOverride);
        var installPath = Path.Combine(Path.GetTempPath(), $"sdp-install-{Guid.NewGuid():N}");
        try
        {
            Environment.SetEnvironmentVariable(NugetFlatContainerOverride, HttpsFlatExample);
            var feed = new RecordingFeedHttpClient { NupkgResponse = BuildFakeNupkg(), };
            var source = new PackageSource(NugetOrg, HttpsApiNugetOrgV3IndexJson);
            var cache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            var result = await NuGetInstallHelpers.TryInstallFromSourceAsync(
                source,
                CreateInstallRequest(source, feed, cache, "Foo", Version100, installPath));

            await Assert.That(result).IsTrue();
            await Assert.That(File.Exists(Path.Combine(installPath, NupkgMetadata))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(installPath, "lib", "net8.0", "Foo.dll"))).IsTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable(NugetFlatContainerOverride, prior);
            if (Directory.Exists(installPath))
            {
                Directory.Delete(installPath, recursive: true);
            }
        }
    }

    /// <summary>InstallFromSourcesAsync skips a source whose service-index throws and uses the next.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task InstallFromSourcesFallsThroughOnHttpError()
    {
        var prior = Environment.GetEnvironmentVariable(NugetFlatContainerOverride);
        var installPath = Path.Combine(Path.GetTempPath(), $"sdp-install-{Guid.NewGuid():N}");
        try
        {
            Environment.SetEnvironmentVariable(NugetFlatContainerOverride, HttpsFlatExample);
            var failingFeed = new SequencedFakeFeed(
            [
                static _ => throw new HttpRequestException("boom"),
                static _ => new MemoryStream(BuildFakeNupkg()),
            ]);

            var sources = new[]
            {
                new PackageSource("broken", "https://broken.example/index.json"),
                new PackageSource("good", "https://good.example/index.json"),
            };
            var cache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            await NuGetInstallHelpers.InstallFromSourcesAsync(
                new(
                    sources,
                    [with(StringComparer.OrdinalIgnoreCase)],
                    failingFeed,
                    NullLogger.Instance,
                    cache,
                    "Foo",
                    Version100,
                    installPath,
                    CancellationToken.None));

            await Assert.That(File.Exists(Path.Combine(installPath, NupkgMetadata))).IsTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable(NugetFlatContainerOverride, prior);
            if (Directory.Exists(installPath))
            {
                Directory.Delete(installPath, recursive: true);
            }
        }
    }

    /// <summary>InstallFromSourcesAsync throws when no source serves the package.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task InstallFromSourcesThrowsWhenAllSourcesMiss()
    {
        var prior = Environment.GetEnvironmentVariable(NugetFlatContainerOverride);
        try
        {
            Environment.SetEnvironmentVariable(NugetFlatContainerOverride, HttpsFlatExample);
            var feed = new RecordingFeedHttpClient { NupkgResponse = null };
            var sources = new[] { new PackageSource(NugetOrg, HttpsApiNugetOrgV3IndexJson) };
            var cache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            async Task Act() => await NuGetInstallHelpers.InstallFromSourcesAsync(
                new(
                    sources,
                    [with(StringComparer.OrdinalIgnoreCase)],
                    feed,
                    NullLogger.Instance,
                    cache,
                    "Foo",
                    Version100,
                    Path.Combine(Path.GetTempPath(), $"sdp-{Guid.NewGuid():N}"),
                    CancellationToken.None));

            await Assert.That(Act).Throws<InvalidOperationException>();
        }
        finally
        {
            Environment.SetEnvironmentVariable(NugetFlatContainerOverride, prior);
        }
    }

    /// <summary>Builds a minimal valid .nupkg (zip) with one lib-folder file.</summary>
    /// <returns>The .nupkg as a byte array.</returns>
    private static byte[] BuildFakeNupkg()
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("lib/net8.0/Foo.dll");
            using var s = entry.Open();
            s.Write([0x4D, 0x5A]);
        }

        return ms.ToArray();
    }

    /// <summary>Creates a request for tests that install from a single source with no credentials.</summary>
    /// <param name="source">Source to probe.</param>
    /// <param name="feed">Fake HTTP surface.</param>
    /// <param name="cache">Flat-container cache.</param>
    /// <param name="packageId">Package ID to install.</param>
    /// <param name="packageVersion">Package version to install.</param>
    /// <param name="installPath">Destination install path.</param>
    /// <returns>A request struct matching the production helper APIs.</returns>
    private static NuGetInstallRequest CreateInstallRequest(
        PackageSource source,
        INuGetFeedHttpClient feed,
        Dictionary<string, string?> cache,
        string packageId,
        string packageVersion,
        string installPath) =>
        new(
            [source],
            [with(StringComparer.OrdinalIgnoreCase)],
            feed,
            NullLogger.Instance,
            cache,
            packageId,
            packageVersion,
            installPath,
            CancellationToken.None);

    /// <summary>Test helper -- feed client backed by static service-index/nupkg responses + counters.</summary>
    private sealed class RecordingFeedHttpClient : INuGetFeedHttpClient
    {
        /// <summary>Gets or sets the body returned by service-index reads.</summary>
        public string ServiceIndexBody { get; set; } = string.Empty;

        /// <summary>Gets or sets the bytes returned by nupkg downloads; null forces a 404.</summary>
        public byte[]? NupkgResponse { get; init; } = [];

        /// <summary>Gets the count of service-index reads invoked.</summary>
        public int ServiceIndexCalls { get; private set; }

        /// <summary>Gets the count of nupkg download attempts invoked.</summary>
        public int NupkgCalls { get; private set; }

        /// <inheritdoc />
        public Task<Stream> ReadServiceIndexAsync(string url, PackageSourceCredential? credential, CancellationToken cancellationToken)
        {
            ServiceIndexCalls++;
            return Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(ServiceIndexBody)));
        }

        /// <inheritdoc />
        public Task<Stream?> TryDownloadNupkgAsync(string url, PackageSourceCredential? credential, CancellationToken cancellationToken)
        {
            NupkgCalls++;
            return Task.FromResult<Stream?>(NupkgResponse is null ? null : new MemoryStream(NupkgResponse));
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }

    /// <summary>Test helper -- feed client returning queued responses per call.</summary>
    private sealed class SequencedFakeFeed : INuGetFeedHttpClient
    {
        /// <summary>One responder per expected nupkg call, consumed in order.</summary>
        private readonly Func<string, Stream>[] _nupkgResponders;

        /// <summary>Index into <see cref="_nupkgResponders"/>; advanced on each call.</summary>
        private int _nupkgIndex;

        /// <summary>Initializes a new instance of the <see cref="SequencedFakeFeed"/> class.</summary>
        /// <param name="nupkgResponders">One responder per expected nupkg call.</param>
        public SequencedFakeFeed(Func<string, Stream>[] nupkgResponders) => _nupkgResponders = nupkgResponders;

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task<Stream> ReadServiceIndexAsync(string url, PackageSourceCredential? credential, CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new MemoryStream("""{"version":"3.0.0","resources":[]}"""u8.ToArray(), writable: false));

        /// <inheritdoc />
        public Task<Stream?> TryDownloadNupkgAsync(string url, PackageSourceCredential? credential, CancellationToken cancellationToken)
        {
            var responder = _nupkgResponders[_nupkgIndex];
            _nupkgIndex++;
            return Task.FromResult<Stream?>(responder(url));
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }
}
