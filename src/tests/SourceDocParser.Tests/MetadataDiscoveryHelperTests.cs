// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging.Abstractions;
using SourceDocParser.LibCompilation;
using SourceDocParser.Model;

namespace SourceDocParser.Tests;

/// <summary>
/// Pins <see cref="MetadataDiscoveryHelper.DiscoverTfmGroupsAsync"/>:
/// streams the source's TFM groups, calls the loader factory once per
/// group, registers each loader with the supplied
/// <see cref="LoaderRegistry"/>, and throws when the source produces
/// no groups (which would otherwise leave the rest of the pipeline
/// with nothing to walk).
/// </summary>
public class MetadataDiscoveryHelperTests
{
    /// <summary>One loader is created per discovered group and tracked in the registry.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DiscoverTfmGroupsAsyncCreatesOneLoaderPerGroup()
    {
        var source = new FakeAssemblySource(
        [
            new("net9.0", ["/a/A.dll"], []),
            new("net10.0", ["/b/B.dll", "/b/C.dll"], []),
        ]);

        using var registry = new LoaderRegistry();
        var loaders = new List<RecordingLoader>();

        var groups = await MetadataDiscoveryHelper.DiscoverTfmGroupsAsync(
            source,
            _ =>
            {
                var loader = new RecordingLoader();
                loaders.Add(loader);
                return loader;
            },
            registry,
            NullLogger.Instance,
            CancellationToken.None);

        await Assert.That(groups.Count).IsEqualTo(2);
        await Assert.That(loaders.Count).IsEqualTo(2);
        await Assert.That(groups[0].Group.Tfm).IsEqualTo("net9.0");
        await Assert.That(groups[1].Group.Tfm).IsEqualTo("net10.0");

        // Disposing the registry should flow through to every tracked loader.
        registry.Dispose();
        await Assert.That(loaders[0].DisposeCount).IsEqualTo(1);
        await Assert.That(loaders[1].DisposeCount).IsEqualTo(1);
    }

    /// <summary>An empty source surfaces an <see cref="InvalidOperationException"/>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DiscoverTfmGroupsAsyncThrowsWhenSourceYieldsNoGroups()
    {
        var source = new FakeAssemblySource([]);
        using var registry = new LoaderRegistry();

        await Assert.That(async () =>
        {
            await MetadataDiscoveryHelper.DiscoverTfmGroupsAsync(
                source,
                static _ => new RecordingLoader(),
                registry,
                NullLogger.Instance,
                CancellationToken.None);
        }).Throws<InvalidOperationException>();
    }

    /// <summary>Each discovered group's <see cref="TfmGroup"/> records the assembly count for the retire counter.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DiscoverTfmGroupsAsyncSeedsRetireCounter()
    {
        var source = new FakeAssemblySource(
        [
            new("net10.0", ["/a/A.dll", "/a/B.dll", "/a/C.dll"], []),
        ]);

        using var registry = new LoaderRegistry();
        var groups = await MetadataDiscoveryHelper.DiscoverTfmGroupsAsync(
            source,
            static _ => new RecordingLoader(),
            registry,
            NullLogger.Instance,
            CancellationToken.None);

        // The TfmGroup retires its loader after AssemblyPaths.Length retires —
        // first two retires return early, the third disposes.
        var loader = (RecordingLoader)groups[0].Loader;
        groups[0].TryRetire();
        groups[0].TryRetire();
        await Assert.That(loader.DisposeCount).IsEqualTo(0);
        groups[0].TryRetire();
        await Assert.That(loader.DisposeCount).IsEqualTo(1);
    }

    /// <summary>Assembly source that yields a pre-built list of groups asynchronously.</summary>
    /// <param name="groups">Groups to yield.</param>
    private sealed class FakeAssemblySource(List<AssemblyGroup> groups) : IAssemblySource
    {
        /// <inheritdoc />
        public IAsyncEnumerable<AssemblyGroup> DiscoverAsync() => DiscoverAsync(CancellationToken.None);

        /// <inheritdoc />
        public async IAsyncEnumerable<AssemblyGroup> DiscoverAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            for (var i = 0; i < groups.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return groups[i];
                await Task.Yield();
            }
        }
    }

    /// <summary>Compilation loader that records dispose counts and returns a synthetic empty compilation.</summary>
    private sealed class RecordingLoader : ICompilationLoader
    {
        /// <summary>Gets the number of times <see cref="Dispose"/> has been invoked.</summary>
        public int DisposeCount { get; private set; }

        /// <inheritdoc />
        public (CSharpCompilation Compilation, IAssemblySymbol Assembly) Load(string assemblyPath, Dictionary<string, string> fallbackReferences) =>
            Load(assemblyPath, fallbackReferences, includePrivateMembers: false);

        /// <inheritdoc />
        public (CSharpCompilation Compilation, IAssemblySymbol Assembly) Load(string assemblyPath, Dictionary<string, string> fallbackReferences, bool includePrivateMembers)
        {
            var compilation = CSharpCompilation.Create("Probe");
            return (compilation, compilation.Assembly);
        }

        /// <inheritdoc />
        public void Dispose() => DisposeCount++;
    }
}
