// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;

namespace SourceDocParser.Tests;

/// <summary>
/// Tests for <see cref="MetadataExtractor"/> as a pipeline orchestrator —
/// uses fake <see cref="IAssemblySource"/> and recording
/// <see cref="IDocumentationEmitter"/> implementations so we never
/// touch Roslyn or the disk beyond a temp output directory.
/// </summary>
public class MetadataExtractorTests
{
    /// <summary>
    /// An empty source (no TFM groups) throws InvalidOperationException —
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

        var path = output.Path;
        await Assert.That(Task () => extractor.RunAsync(source, path, emitter))
            .Throws<InvalidOperationException>();
    }

    /// <summary>
    /// Null source / null emitter throw ArgumentNullException; null
    /// outputRoot throws ArgumentException (whitespace check).
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RunAsyncValidatesArguments()
    {
        var source = new FakeAssemblySource([]);
        var emitter = new RecordingEmitter();
        using var output = new TempDirectory();
        var extractor = new MetadataExtractor();

        var path = output.Path;
        await Assert.That(Task () => extractor.RunAsync(null!, path, emitter))
            .Throws<ArgumentNullException>();
        await Assert.That(Task () => extractor.RunAsync(source, path, null!))
            .Throws<ArgumentNullException>();
        await Assert.That(Task () => extractor.RunAsync(source, "   ", emitter))
            .Throws<ArgumentException>();
    }

    /// <summary>
    /// Verifies that <see cref="MetadataExtractor.RunAsync"/> correctly passes
    /// the merged types and output root to the emitter.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
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

        var path = output.Path;
        await extractor.RunAsync(source, path, emitter);

        await Assert.That(emitter.CapturedOutputRoot).IsEqualTo(path);
        await Assert.That(emitter.CapturedTypes).IsNotNull();
    }

    /// <summary>
    /// Recording emitter that captures the merged catalog the extractor hands it.
    /// </summary>
    private sealed class RecordingEmitter : IDocumentationEmitter
    {
        /// <summary>Gets the catalog captured on the most recent invocation.</summary>
        public List<ApiType> CapturedTypes { get; private set; } = [];

        /// <summary>Gets the output root captured on the most recent invocation.</summary>
        public string CapturedOutputRoot { get; private set; } = string.Empty;

        /// <inheritdoc />
        public Task<int> EmitAsync(List<ApiType> types, string outputRoot, CancellationToken cancellationToken = default)
        {
            CapturedTypes = types;
            CapturedOutputRoot = outputRoot;
            return Task.FromResult(types.Count);
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
        public async IAsyncEnumerable<AssemblyGroup> DiscoverAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var group in groups)
            {
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
