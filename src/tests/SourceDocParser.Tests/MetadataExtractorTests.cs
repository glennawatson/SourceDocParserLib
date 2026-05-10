// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using SourceDocParser.Model;

namespace SourceDocParser.Tests;

/// <summary>
/// Tests for <see cref="MetadataExtractor"/> as a pipeline orchestrator --
/// uses fake <see cref="IAssemblySource"/> and recording
/// <see cref="IDocumentationEmitter"/> implementations so we never
/// touch Roslyn or the disk beyond a temp output directory.
/// </summary>
public class MetadataExtractorTests
{
    /// <summary>
    /// An empty source (no TFM groups) throws InvalidOperationException --
    /// the parser refuses to "succeed" on a no-op input because it almost
    /// always indicates a misconfigured source.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RunAsyncThrowsWhenSourceProducesNoGroups()
    {
        var source = new FakeAssemblySource([]);
        var emitter = new RecordingEmitter();
        using var output = new TempDirectory();
        var extractor = new MetadataExtractor();

        IPageSink sink = new FilePageSink(output.Path);
        await Assert.That(Task () => extractor.RunAsync(source, sink, emitter))
            .Throws<InvalidOperationException>();
    }

    /// <summary>
    /// Null source / null emitter / null sink throw ArgumentNullException.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RunAsyncValidatesArguments()
    {
        var source = new FakeAssemblySource([]);
        var emitter = new RecordingEmitter();
        using var output = new TempDirectory();
        var extractor = new MetadataExtractor();

        IPageSink sink = new FilePageSink(output.Path);
        await Assert.That(Task () => extractor.RunAsync(null!, sink, emitter))
            .Throws<ArgumentNullException>();
        await Assert.That(Task () => extractor.RunAsync(source, sink, null!))
            .Throws<ArgumentNullException>();
        await Assert.That(Task () => extractor.RunAsync(source, (IPageSink)null!, emitter))
            .Throws<ArgumentNullException>();
    }

    /// <summary>
    /// Executes the metadata extraction process and captures the output data in the provided emitter.
    /// Verifies that the emitter correctly records both the output directory path and extracted type information.
    /// </summary>
    /// <returns>A task representing the execution of the metadata extraction and subsequent validation.</returns>
    [Test]
    public async Task RunAsyncCapturesDataInEmitter()
    {
        // We need a source that produces at least one group to avoid InvalidOperationException.
        // But since LoadAndWalkAssembly is called, we might need a real-ish dll or a fake that works.
        // Actually, let's see if we can just provide an empty list of assembly paths in a group.
        var groups = new List<AssemblyGroup>
        {
            new("net10.0", [], []),
        };
        var source = new FakeAssemblySource(groups);
        var emitter = new RecordingEmitter();
        using var output = new TempDirectory();
        var extractor = new MetadataExtractor();

        IPageSink sink = new FilePageSink(output.Path);
        await extractor.RunAsync(source, sink, emitter);

        await Assert.That(emitter.CapturedSink).IsSameReferenceAs(sink);
        await Assert.That(emitter.CapturedTypes).IsNotNull();
    }

    /// <summary>
    /// Direct-mode <see cref="MetadataExtractor.ExtractAsync(IAssemblySource)"/> returns the merged catalog
    /// without invoking an emitter or touching disk.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ExtractAsyncReturnsMergedCatalogWithoutEmitter()
    {
        var groups = new List<AssemblyGroup>
        {
            new("net10.0", [], []),
        };
        var source = new FakeAssemblySource(groups);
        var extractor = new MetadataExtractor();

        var result = await extractor.ExtractAsync(source);

        await Assert.That(result).IsNotNull();
        await Assert.That(result.CanonicalTypes).IsNotNull();
        await Assert.That(result.SourceLinks).IsNotNull();
    }

    /// <summary>
    /// <see cref="MetadataExtractor.ExtractAsync(IAssemblySource)"/> rejects a null source.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ExtractAsyncValidatesArguments()
    {
        var extractor = new MetadataExtractor();

        await Assert.That(Task () => extractor.ExtractAsync(null!))
            .Throws<ArgumentNullException>();
    }

    /// <summary>
    /// An empty source (no TFM groups) throws <see cref="InvalidOperationException"/>
    /// from the direct-mode path too.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ExtractAsyncThrowsWhenSourceProducesNoGroups()
    {
        var source = new FakeAssemblySource([]);
        var extractor = new MetadataExtractor();

        await Assert.That(Task () => extractor.ExtractAsync(source))
            .Throws<InvalidOperationException>();
    }

    /// <summary>
    /// Recording emitter that captures the merged catalog the extractor hands it.
    /// </summary>
    private sealed class RecordingEmitter : IDocumentationEmitter
    {
        /// <summary>Gets the catalog captured on the most recent invocation.</summary>
        public ApiType[] CapturedTypes { get; private set; } = [];

        /// <summary>Gets the sink captured on the most recent invocation.</summary>
        public IPageSink? CapturedSink { get; private set; }

        /// <inheritdoc />
        public Task<int> EmitAsync(ApiType[] types, IPageSink sink) => EmitAsync(types, sink, CancellationToken.None);

        /// <inheritdoc />
        public Task<int> EmitAsync(ApiType[] types, IPageSink sink, CancellationToken cancellationToken)
        {
            CapturedTypes = types;
            CapturedSink = sink;
            return Task.FromResult(types.Length);
        }
    }

    /// <summary>
    /// Source that yields a pre-built list of <see cref="AssemblyGroup"/>s
    /// without touching the disk.
    /// </summary>
    /// <param name="groups">Groups to yield in DiscoverAsync.</param>
    private sealed class FakeAssemblySource(List<AssemblyGroup> groups) : IAssemblySource
    {
        /// <inheritdoc />
        public IAsyncEnumerable<AssemblyGroup> DiscoverAsync() => DiscoverAsync(CancellationToken.None);

        /// <inheritdoc />
        public async IAsyncEnumerable<AssemblyGroup> DiscoverAsync([EnumeratorCancellation] CancellationToken cancellationToken)
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
        /// <summary>Initializes a new instance of the <see cref="TempDirectory"/> class..</summary>
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
