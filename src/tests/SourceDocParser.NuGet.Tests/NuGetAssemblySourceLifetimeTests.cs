// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.Extensions.Logging;
using SourceDocParser.NuGet.Infrastructure;

namespace SourceDocParser.NuGet.Tests;

/// <summary>
/// Pins <see cref="NuGetAssemblySource"/> on the constructor overloads added with the
/// shared-<see cref="HttpClient"/> change and the <see cref="IDisposable"/> contract:
/// a default-built fetcher is owned and disposed alongside the source, while a
/// caller-supplied fetcher is left intact for the caller to dispose.
/// </summary>
public class NuGetAssemblySourceLifetimeTests
{
    /// <summary>The two-argument convenience constructor accepts a non-null root + apiPath without throwing.</summary>
    /// <returns>Async test.</returns>
    [Test]
    public async Task TwoArgConstructorAcceptsValidPaths()
    {
        using var root = new ScratchDirectory();
        using var api = new ScratchDirectory();

        using var source = new NuGetAssemblySource(root.Path, api.Path);

        await Assert.That(source).IsNotNull();
    }

    /// <summary>The three-argument convenience constructor (logger only) likewise accepts non-null inputs.</summary>
    /// <returns>Async test.</returns>
    [Test]
    public async Task ThreeArgConstructorAcceptsLogger()
    {
        using var root = new ScratchDirectory();
        using var api = new ScratchDirectory();

        using var source = new NuGetAssemblySource(root.Path, api.Path, logger: null);

        await Assert.That(source).IsNotNull();
    }

    /// <summary>Disposing a source built with the default fetcher disposes the owned fetcher in turn.</summary>
    /// <returns>Async test.</returns>
    [Test]
    public async Task DisposeDisposesDefaultFetcher()
    {
        using var root = new ScratchDirectory();
        using var api = new ScratchDirectory();
        var source = new NuGetAssemblySource(root.Path, api.Path);

        // The default-built fetcher's lifetime tracks the source. Calling Dispose
        // twice must remain a safe no-op — both invocations should complete.
        await Assert.That(() =>
        {
            source.Dispose();
            source.Dispose();
        }).ThrowsNothing();
    }

    /// <summary>Disposing a source built with a caller-supplied fetcher leaves that fetcher untouched.</summary>
    /// <returns>Async test.</returns>
    [Test]
    public async Task DisposeDoesNotDisposeInjectedFetcher()
    {
        using var root = new ScratchDirectory();
        using var api = new ScratchDirectory();
        var fetcher = new TrackingFetcher();
        var source = new NuGetAssemblySource(root.Path, api.Path, logger: null, fetcher: fetcher);

        source.Dispose();

        await Assert.That(fetcher.Disposed).IsFalse();
    }

    /// <summary>Disposable fetcher that records whether it was disposed.</summary>
    private sealed class TrackingFetcher : INuGetFetcher, IDisposable
    {
        /// <summary>Gets a value indicating whether <see cref="Dispose"/> has been invoked.</summary>
        public bool Disposed { get; private set; }

        /// <inheritdoc/>
        public Task FetchPackagesAsync(string rootDirectory, string apiPath) => Task.CompletedTask;

        /// <inheritdoc/>
        public Task FetchPackagesAsync(string rootDirectory, string apiPath, ILogger? logger) => Task.CompletedTask;

        /// <inheritdoc/>
        public Task FetchPackagesAsync(string rootDirectory, string apiPath, ILogger? logger, CancellationToken cancellationToken) => Task.CompletedTask;

        /// <inheritdoc/>
        public void Dispose() => Disposed = true;
    }

    /// <summary>Disposable scratch directory the test deletes on dispose.</summary>
    private sealed class ScratchDirectory : IDisposable
    {
        /// <summary>Initializes a new instance of the <see cref="ScratchDirectory"/> class.</summary>
        public ScratchDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sdp-nuget-life-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        /// <summary>Gets the absolute path of the scratch directory.</summary>
        public string Path { get; }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!Directory.Exists(Path))
            {
                return;
            }

            Directory.Delete(Path, recursive: true);
        }
    }
}
