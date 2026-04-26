// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.Extensions.Logging;

namespace SourceDocParser.NuGet.Tests;

/// <summary>
/// Constructor-level coverage for <see cref="NuGetAssemblySource"/>.
/// Heavier scenarios that touch nuget.org live in
/// <c>SourceDocParser.IntegrationTests</c>.
/// </summary>
public class NuGetAssemblySourceTests
{
    /// <summary>Constructing with a null root directory throws.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RejectsNullRootDirectory()
    {
        await Assert.That(Act).Throws<ArgumentNullException>();

        static void Act() => _ = new NuGetAssemblySource(rootDirectory: null!, apiPath: "/tmp/api");
    }

    /// <summary>Constructing with a null api path throws.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RejectsNullApiPath()
    {
        await Assert.That(Act).Throws<ArgumentNullException>();

        static void Act() => _ = new NuGetAssemblySource(rootDirectory: "/tmp/repo", apiPath: null!);
    }

    /// <summary>
    /// Discovery excludes package DLLs whose simple name is already supplied by
    /// the matched refs/ TFM, so the parser walks the implementation assembly set
    /// without duplicating co-located reference shims.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DiscoverAsyncSkipsLibAssembliesShadowedByRefs()
    {
        using var root = new TempDirectory();
        using var api = new TempDirectory();
        var libTfmDir = Path.Combine(api.Path, "lib", "net10.0");
        var refsTfmDir = Path.Combine(api.Path, "refs", "net10.0");
        Directory.CreateDirectory(libTfmDir);
        Directory.CreateDirectory(refsTfmDir);

        File.WriteAllBytes(Path.Combine(libTfmDir, "Package.dll"), []);
        File.WriteAllBytes(Path.Combine(libTfmDir, "Shared.dll"), []);
        File.WriteAllBytes(Path.Combine(refsTfmDir, "Shared.dll"), []);

        var source = new NuGetAssemblySource(root.Path, api.Path, fetcher: new NoOpFetcher());
        List<AssemblyGroup> groups = [];
        await foreach (var group in source.DiscoverAsync())
        {
            groups.Add(group);
        }

        await Assert.That(groups.Count).IsEqualTo(1);
        await Assert.That(groups[0].Tfm).IsEqualTo("net10.0");
        await Assert.That(groups[0].AssemblyPaths.Length).IsEqualTo(1);
        await Assert.That(Path.GetFileName(groups[0].AssemblyPaths[0])).IsEqualTo("Package.dll");
    }

    /// <summary>
    /// Disposable scratch directory the test deletes on dispose.
    /// </summary>
    private sealed class TempDirectory : IDisposable
    {
        /// <summary>Initializes a new instance of the <see cref="TempDirectory"/> class.</summary>
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sdp-nuget-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        /// <summary>Gets the absolute path of the scratch directory.</summary>
        public string Path { get; }

        /// <inheritdoc />
        public void Dispose()
        {
            if (!Directory.Exists(Path))
            {
                return;
            }

            Directory.Delete(Path, recursive: true);
        }
    }

    /// <summary>
    /// Fetcher fake used by discovery tests that prepare the lib/refs layout directly.
    /// </summary>
    private sealed class NoOpFetcher : INuGetFetcher
    {
        /// <inheritdoc />
        public Task FetchPackagesAsync(string rootDirectory, string apiPath, ILogger? logger = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
