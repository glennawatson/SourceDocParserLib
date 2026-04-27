// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using SourceDocParser.LibCompilation;
using SourceDocParser.Model;
using SourceDocParser.SourceLink;
using SourceDocParser.Walk;

namespace SourceDocParser.Tests;

/// <summary>
/// Tests that <see cref="MetadataExtractor"/>'s constructor honours the
/// optional <see cref="ICompilationLoader"/> / <see cref="ISymbolWalker"/>
/// / <see cref="ISourceLinkResolver"/> seams. Verifies call counts and
/// argument routing through the pipeline.
/// </summary>
public class MetadataExtractorInjectionTests
{
    /// <summary>
    /// One TFM group with two assembly paths produces:
    /// one loader (per group), two Load calls, two Walk calls, two
    /// resolver-factory calls.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RunAsyncDispatchesToInjectedCollaboratorsExpectedNumberOfTimes()
    {
        var groups = new List<AssemblyGroup>
        {
            new("net10.0", ["/fake/A.dll", "/fake/B.dll"], []),
        };

        var loader = new MockCompilationLoader();
        var loaderFactoryCalls = 0;
        var walker = new MockSymbolWalker();
        var sourceLinkPaths = new List<string>();
        var extractor = new MetadataExtractor(walker, LoaderFactory, ResolverFactory);

        using var output = new TempDirectory();
        var result = await extractor.RunAsync(new FakeAssemblySource(groups), output.Path, new RecordingEmitter());

        await Assert.That(loaderFactoryCalls).IsEqualTo(1);
        await Assert.That(loader.LoadCalls.Count).IsEqualTo(2);
        await Assert.That(walker.WalkCalls.Count).IsEqualTo(2);
        await Assert.That(sourceLinkPaths.Count).IsEqualTo(2);

        // Eager retire + LoaderRegistry safety net both dispose the loader.
        // CompilationLoader.Dispose is idempotent so this is correct in production;
        // the mock just counts every call.
        await Assert.That(loader.DisposeCount).IsGreaterThanOrEqualTo(1);
        await Assert.That(result.LoadFailures).IsEqualTo(0);

        ICompilationLoader LoaderFactory(ILogger logger)
        {
            Interlocked.Increment(ref loaderFactoryCalls);
            return loader;
        }

        ISourceLinkResolver ResolverFactory(string path)
        {
            lock (sourceLinkPaths)
            {
                sourceLinkPaths.Add(path);
            }

            return new NullSourceLinkResolver();
        }
    }

    /// <summary>
    /// Two TFM groups produce two loader-factory calls (one loader per group).
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RunAsyncCreatesOneLoaderPerTfmGroup()
    {
        var groups = new List<AssemblyGroup>
        {
            new("net9.0", ["/fake/A.dll"], []),
            new("net10.0", ["/fake/B.dll"], []),
        };

        var loaders = new List<MockCompilationLoader>();
        var walker = new MockSymbolWalker();
        var extractor = new MetadataExtractor(walker, LoaderFactory, static _ => new NullSourceLinkResolver());

        using var output = new TempDirectory();
        await extractor.RunAsync(new FakeAssemblySource(groups), output.Path, new RecordingEmitter());

        await Assert.That(loaders.Count).IsEqualTo(2);
        await Assert.That(loaders.All(static l => l.DisposeCount >= 1)).IsTrue();

        ICompilationLoader LoaderFactory(ILogger logger)
        {
            var loader = new MockCompilationLoader();
            lock (loaders)
            {
                loaders.Add(loader);
            }

            return loader;
        }
    }

    /// <summary>
    /// A loader that throws on Load is reported as a load failure rather than aborting the run.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RunAsyncCountsLoaderExceptionsAsLoadFailures()
    {
        var groups = new List<AssemblyGroup>
        {
            new("net10.0", ["/fake/Boom.dll"], []),
        };

        var walker = new MockSymbolWalker();
        var extractor = new MetadataExtractor(walker, LoaderFactory, static _ => new NullSourceLinkResolver());

        using var output = new TempDirectory();
        var result = await extractor.RunAsync(new FakeAssemblySource(groups), output.Path, new RecordingEmitter());

        await Assert.That(result.LoadFailures).IsEqualTo(1);
        await Assert.That(walker.WalkCalls.Count).IsEqualTo(0);

        static ICompilationLoader LoaderFactory(ILogger logger) => new ThrowingCompilationLoader();
    }

    /// <summary>
    /// The TFM string passed to the walker matches the group's TFM.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RunAsyncForwardsTfmToWalker()
    {
        var groups = new List<AssemblyGroup>
        {
            new("net9.0", ["/fake/A.dll"], []),
            new("net10.0", ["/fake/B.dll"], []),
        };

        var walker = new MockSymbolWalker();
        var extractor = new MetadataExtractor(
            walker,
            LoaderFactory,
            ResolverFactory);

        using var output = new TempDirectory();
        await extractor.RunAsync(new FakeAssemblySource(groups), output.Path, new RecordingEmitter());

        List<string> observedTfms = [.. walker.WalkCalls.Select(static c => c.Tfm).OrderBy(static s => s, StringComparer.Ordinal)];
        await Assert.That(observedTfms).IsEquivalentTo((List<string>)["net10.0", "net9.0"]);

        static ICompilationLoader LoaderFactory(ILogger logger) => new MockCompilationLoader();

        static ISourceLinkResolver ResolverFactory(string path) => new NullSourceLinkResolver();
    }

    /// <summary>
    /// Source-link resolver factory receives each assembly path exactly once.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RunAsyncForwardsAssemblyPathToResolverFactory()
    {
        var groups = new List<AssemblyGroup>
        {
            new("net10.0", ["/fake/A.dll", "/fake/B.dll"], []),
        };

        var observed = new List<string>();
        var extractor = new MetadataExtractor(
            new MockSymbolWalker(),
            LoaderFactory,
            ResolverFactory);

        using var output = new TempDirectory();
        await extractor.RunAsync(new FakeAssemblySource(groups), output.Path, new RecordingEmitter());

        List<string> sorted = [.. observed.OrderBy(static s => s, StringComparer.Ordinal)];
        await Assert.That(sorted).IsEquivalentTo((List<string>)["/fake/A.dll", "/fake/B.dll"]);

        static ICompilationLoader LoaderFactory(ILogger logger) => new MockCompilationLoader();

        ISourceLinkResolver ResolverFactory(string path)
        {
            lock (observed)
            {
                observed.Add(path);
            }

            return new NullSourceLinkResolver();
        }
    }

    /// <summary>
    /// <see cref="ICompilationLoader"/> mock that records every Load call
    /// and returns a synthetic compilation built once per Load.
    /// </summary>
    private sealed class MockCompilationLoader : ICompilationLoader
    {
        /// <summary>Gets the recorded Load calls in invocation order (thread-safe via lock).</summary>
        public List<(string AssemblyPath, Dictionary<string, string> Fallback)> LoadCalls { get; } = [];

        /// <summary>Gets the number of times Dispose was invoked.</summary>
        public int DisposeCount { get; private set; }

        /// <inheritdoc />
        public (CSharpCompilation Compilation, IAssemblySymbol Assembly) Load(
            string assemblyPath,
            Dictionary<string, string> fallbackReferences,
            bool includePrivateMembers = false)
        {
            lock (LoadCalls)
            {
                LoadCalls.Add((assemblyPath, fallbackReferences));
            }

            var tree = CSharpSyntaxTree.ParseText("public class Probe { }");
            var compilation = CSharpCompilation.Create("Probe", [tree]);
            return (compilation, compilation.Assembly);
        }

        /// <inheritdoc />
        public void Dispose() => DisposeCount++;
    }

    /// <summary>
    /// Loader that always throws — used to exercise the load-failure path.
    /// </summary>
    private sealed class ThrowingCompilationLoader : ICompilationLoader
    {
        /// <inheritdoc />
        public (CSharpCompilation Compilation, IAssemblySymbol Assembly) Load(
            string assemblyPath,
            Dictionary<string, string> fallbackReferences,
            bool includePrivateMembers = false) =>
            throw new InvalidOperationException("simulated load failure");

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }

    /// <summary>
    /// <see cref="ISymbolWalker"/> mock that records every Walk call and
    /// returns a minimal empty catalog.
    /// </summary>
    private sealed class MockSymbolWalker : ISymbolWalker
    {
        /// <summary>Gets the recorded Walk calls in invocation order (thread-safe via lock).</summary>
        public List<(string Tfm, IAssemblySymbol Assembly, Compilation Compilation, ISourceLinkResolver Resolver)> WalkCalls { get; } = [];

        /// <inheritdoc />
        public ApiCatalog Walk(string tfm, IAssemblySymbol assembly, Compilation compilation, ISourceLinkResolver sourceLinks)
        {
            lock (WalkCalls)
            {
                WalkCalls.Add((tfm, assembly, compilation, sourceLinks));
            }

            return new(tfm, []);
        }
    }

    /// <summary>
    /// <see cref="ISourceLinkResolver"/> mock that always returns null.
    /// </summary>
    private sealed class NullSourceLinkResolver : ISourceLinkResolver
    {
        /// <inheritdoc />
        public string? Resolve(ISymbol symbol) => null;

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }

    /// <summary>
    /// Recording emitter that captures the merged catalog the extractor hands it.
    /// </summary>
    private sealed class RecordingEmitter : IDocumentationEmitter
    {
        /// <inheritdoc />
        public Task<int> EmitAsync(ApiType[] types, string outputRoot, CancellationToken cancellationToken = default) =>
            Task.FromResult(types.Length);
    }

    /// <summary>
    /// Fake source that yields a pre-built list of <see cref="AssemblyGroup"/>s.
    /// </summary>
    /// <param name="groups">Groups to yield in DiscoverAsync.</param>
    private sealed class FakeAssemblySource(List<AssemblyGroup> groups) : IAssemblySource
    {
        /// <inheritdoc />
        public async IAsyncEnumerable<AssemblyGroup> DiscoverAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            for (var i = 0; i < groups.Count; i++)
            {
                var group = groups[i];
                cancellationToken.ThrowIfCancellationRequested();
                yield return group;
                await Task.Yield();
            }
        }
    }

    /// <summary>
    /// Disposable scratch directory the test deletes on dispose.
    /// </summary>
    private sealed class TempDirectory : IDisposable
    {
        /// <summary>Initializes a new instance of the <see cref="TempDirectory"/> class.</summary>
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sdp-tests-{Guid.NewGuid():N}");
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
}
