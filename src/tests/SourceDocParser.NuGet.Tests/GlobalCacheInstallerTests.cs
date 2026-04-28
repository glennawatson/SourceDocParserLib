// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.IO.Compression;
using System.Text;
using SourceDocParser.NuGet.Infrastructure;
using SourceDocParser.NuGet.Models;

namespace SourceDocParser.NuGet.Tests;

/// <summary>
/// Contains unit tests for the <c>GlobalCacheInstaller</c> class,
/// ensuring its behavior conforms to expected functionality under various scenarios.
/// </summary>
public class GlobalCacheInstallerTests
{
    /// <summary>Constructor rejects a null/blank working folder.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConstructorRejectsBlankWorkingFolder()
    {
        await Assert.That(() => new GlobalCacheInstaller(string.Empty)).Throws<ArgumentException>();
        await Assert.That(() => new GlobalCacheInstaller("   ")).Throws<ArgumentException>();
    }

    /// <summary>The <c>GlobalPackagesFolder</c> property throws until <c>InitializeAsync</c> has run.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GlobalPackagesFolderThrowsBeforeInitialize()
    {
        using var installer = new GlobalCacheInstaller(AppContext.BaseDirectory);

        await Assert.That(() => installer.GlobalPackagesFolder).Throws<InvalidOperationException>();
    }

    /// <summary>The <c>EnabledSources</c> property throws until <c>InitializeAsync</c> has run.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EnabledSourcesThrowsBeforeInitialize()
    {
        using var installer = new GlobalCacheInstaller(AppContext.BaseDirectory);

        await Assert.That(() => installer.EnabledSources).Throws<InvalidOperationException>();
    }

    /// <summary><c>InstallAsync</c> throws when the installer hasn't been initialised.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task InstallAsyncThrowsBeforeInitialize()
    {
        await Assert.That(Act).Throws<InvalidOperationException>();
        static async Task Act()
        {
            using var installer = new GlobalCacheInstaller(AppContext.BaseDirectory);
            await installer.InstallAsync("Foo", "1.0.0");
        }
    }

    /// <summary><c>InstallAsync</c> rejects blank package id / version arguments.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task InstallAsyncRejectsBlankArguments()
    {
        await Assert.That(BlankId).Throws<ArgumentException>();
        await Assert.That(BlankVersion).Throws<ArgumentException>();
        static async Task BlankId()
        {
            using var installer = new GlobalCacheInstaller(AppContext.BaseDirectory);

            await installer.InstallAsync(string.Empty, "1.0.0");
        }

        static async Task BlankVersion()
        {
            using var installer = new GlobalCacheInstaller(AppContext.BaseDirectory);

            await installer.InstallAsync("Foo", string.Empty);
        }
    }

    /// <summary>
    /// <c>InitializeAsync</c> integrates the nuget.config discovery
    /// chain with <see cref="GlobalCacheInstaller.GlobalPackagesFolder"/>
    /// + <see cref="GlobalCacheInstaller.EnabledSources"/>. The
    /// <c>with-sources</c> fixture declares two sources and a custom
    /// global packages folder.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task InitializeAsyncResolvesFromFixture()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "with-sources");
        using var installer = new GlobalCacheInstaller(fixture);

        await installer.InitializeAsync().ConfigureAwait(false);

        // The fixture is one of the existing NuGetConfigDiscoveryTests
        // fixtures and declares two sources without a <clear/>.
        await Assert.That(installer.EnabledSources.Count).IsGreaterThanOrEqualTo(2);
        await Assert.That(installer.GlobalPackagesFolder).IsNotEmpty();
    }

    /// <summary>The internal constructor rejects a null feed client.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task InternalConstructorRejectsNullFeed() =>
        await Assert.That(() => new GlobalCacheInstaller(AppContext.BaseDirectory, logger: null, feedHttp: null!))
            .Throws<ArgumentNullException>();

    /// <summary>The internal constructor rejects a blank working folder.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task InternalConstructorRejectsBlankWorkingFolder() =>
        await Assert.That(() => new GlobalCacheInstaller(string.Empty, logger: null, new FakeFeed()))
            .Throws<ArgumentException>();

    /// <summary>The public constructor accepts a caller-owned <see cref="HttpClient"/> without throwing.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task PublicConstructorAcceptsExplicitHttpClient()
    {
        using var http = new HttpClient();
        using var installer = new GlobalCacheInstaller(AppContext.BaseDirectory, logger: null, http);

        await Assert.That(() => installer.GlobalPackagesFolder).Throws<InvalidOperationException>();
    }

    /// <summary>Disposing an installer built with the internal ctor does NOT dispose the injected feed client.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task InternalCtorDisposeLeavesInjectedFeedAlive()
    {
        var fake = new FakeFeed();
        var installer = new GlobalCacheInstaller(AppContext.BaseDirectory, logger: null, fake);

        installer.Dispose();

        await Assert.That(fake.Disposed).IsFalse();
    }

    /// <summary><c>InstallAsync</c> short-circuits when the package is already installed in the global cache.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task InstallAsyncShortCircuitsOnCacheHit()
    {
        using var fixture = new InstallerFixture();
        var pkgFolder = fixture.PrePopulateGlobalPackage("Foo", "1.0.0");

        var fake = new FakeFeed();
        using var installer = new GlobalCacheInstaller(fixture.WorkingFolder, logger: null, fake);
        await installer.InitializeAsync();

        var result = await installer.InstallAsync("Foo", "1.0.0");

        await Assert.That(result).IsEqualTo(pkgFolder);
        await Assert.That(fake.NupkgCalls).IsEqualTo(0);
    }

    /// <summary><c>InstallAsync</c> returns the fallback-folder hit without touching the network.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task InstallAsyncReturnsFallbackHit()
    {
        using var fixture = new InstallerFixture();
        var fallback = fixture.PrePopulateFallbackPackage("Foo", "1.0.0");

        var fake = new FakeFeed();
        using var installer = new GlobalCacheInstaller(fixture.WorkingFolder, logger: null, fake);
        await installer.InitializeAsync();

        var result = await installer.InstallAsync("Foo", "1.0.0");

        await Assert.That(result).IsEqualTo(fallback);
        await Assert.That(fake.NupkgCalls).IsEqualTo(0);
    }

    /// <summary><c>InstallAsync</c> downloads + extracts via the feed client and writes the SDK marker.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task InstallAsyncDownloadsAndExtracts()
    {
        var prior = Environment.GetEnvironmentVariable("NUGET_FLAT_CONTAINER_OVERRIDE");
        try
        {
            Environment.SetEnvironmentVariable("NUGET_FLAT_CONTAINER_OVERRIDE", "https://flat.example/");
            using var fixture = new InstallerFixture();
            var fake = new FakeFeed { NupkgResponse = BuildFakeNupkg() };

            using var installer = new GlobalCacheInstaller(fixture.WorkingFolder, logger: null, fake);
            await installer.InitializeAsync();

            var result = await installer.InstallAsync("Foo", "1.0.0");

            await Assert.That(File.Exists(Path.Combine(result, ".nupkg.metadata"))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(result, "lib", "net8.0", "Foo.dll"))).IsTrue();
            await Assert.That(fake.NupkgCalls).IsEqualTo(1);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NUGET_FLAT_CONTAINER_OVERRIDE", prior);
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

    /// <summary>Test helper -- isolated nuget.config + global/fallback folders under a per-test temp directory.</summary>
    private sealed class InstallerFixture : IDisposable
    {
        /// <summary>Root of the fixture; cleaned up on Dispose.</summary>
        private readonly string _root;

        /// <summary>Initializes a new instance of the <see cref="InstallerFixture"/> class.</summary>
        public InstallerFixture()
        {
            _root = Path.Combine(Path.GetTempPath(), $"sdp-installer-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_root);
            WorkingFolder = Path.Combine(_root, "work");
            GlobalFolder = Path.Combine(_root, "global");
            FallbackFolder = Path.Combine(_root, "fallback");
            Directory.CreateDirectory(WorkingFolder);
            Directory.CreateDirectory(GlobalFolder);
            Directory.CreateDirectory(FallbackFolder);

            var config = $"""
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <config>
                    <add key="globalPackagesFolder" value="{GlobalFolder}" />
                  </config>
                  <packageSources>
                    <clear />
                    <add key="test-feed" value="https://test.example/index.json" />
                  </packageSources>
                  <fallbackPackageFolders>
                    <clear />
                    <add key="fallback" value="{FallbackFolder}" />
                  </fallbackPackageFolders>
                </configuration>
                """;
            File.WriteAllText(Path.Combine(WorkingFolder, "nuget.config"), config);
        }

        /// <summary>Gets the directory the installer is rooted at.</summary>
        public string WorkingFolder { get; }

        /// <summary>Gets the resolved global packages folder.</summary>
        public string GlobalFolder { get; }

        /// <summary>Gets the resolved fallback package folder.</summary>
        public string FallbackFolder { get; }

        /// <summary>Pre-populates the global cache with a marker so the cache-hit short-circuit fires.</summary>
        /// <param name="id">Package id.</param>
        /// <param name="version">Package version.</param>
        /// <returns>The install path under the global folder.</returns>
        public string PrePopulateGlobalPackage(string id, string version) =>
            CreateInstalledPackage(GlobalFolder, id, version);

        /// <summary>Pre-populates a fallback folder with the package marker.</summary>
        /// <param name="id">Package id.</param>
        /// <param name="version">Package version.</param>
        /// <returns>The install path under the fallback folder.</returns>
        public string PrePopulateFallbackPackage(string id, string version) =>
            CreateInstalledPackage(FallbackFolder, id, version);

        /// <inheritdoc />
        public void Dispose()
        {
            if (!Directory.Exists(_root))
            {
                return;
            }

            Directory.Delete(_root, recursive: true);
        }

        /// <summary>Writes a minimal <c>.nupkg.metadata</c> marker so <c>NuGetGlobalCache.IsPackageInstalled</c> returns true.</summary>
        /// <param name="root">Cache root.</param>
        /// <param name="id">Package id.</param>
        /// <param name="version">Package version.</param>
        /// <returns>The created install path.</returns>
        private static string CreateInstalledPackage(string root, string id, string version)
        {
            var path = NuGetGlobalCache.GetPackageInstallPath(root, id, version);
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, ".nupkg.metadata"), "{}");
            return path;
        }
    }

    /// <summary>Test helper -- feed client recording calls + returning a configurable nupkg byte payload.</summary>
    private sealed class FakeFeed : INuGetFeedHttpClient
    {
        /// <summary>Gets or sets the bytes returned by nupkg downloads; null forces a 404.</summary>
        public byte[]? NupkgResponse { get; set; }

        /// <summary>Gets the count of nupkg download attempts invoked.</summary>
        public int NupkgCalls { get; private set; }

        /// <summary>Gets a value indicating whether the feed has been disposed.</summary>
        public bool Disposed { get; private set; }

        /// <inheritdoc />
        public Task<Stream> ReadServiceIndexAsync(string url, PackageSourceCredential? credential, CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("""{"version":"3.0.0","resources":[{"@id":"https://flat.example/","@type":"PackageBaseAddress/3.0.0"}]}""")));

        /// <inheritdoc />
        public Task<Stream?> TryDownloadNupkgAsync(string url, PackageSourceCredential? credential, CancellationToken cancellationToken)
        {
            NupkgCalls++;
            return Task.FromResult<Stream?>(NupkgResponse is null ? null : new MemoryStream(NupkgResponse));
        }

        /// <inheritdoc />
        public void Dispose() => Disposed = true;
    }
}
