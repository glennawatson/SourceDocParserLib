// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using SourceDocParser.LibCompilation;
using SourceDocParser.Merge;
using SourceDocParser.Model;
using SourceDocParser.SourceLink;
using SourceDocParser.Walk;
using CompilationLoader = SourceDocParser.LibCompilation.CompilationLoader;

namespace SourceDocParser;

/// <summary>
/// Drives the documentation pipeline: pulls assemblies from an
/// <see cref="IAssemblySource"/>, walks each into an
/// <see cref="ApiCatalog"/>, merges duplicates across TFMs, and hands
/// the merged catalog to an <see cref="IDocumentationEmitter"/>.
/// </summary>
public sealed class MetadataExtractor : IMetadataExtractor
{
    /// <summary>
    /// Upper bound on concurrent Roslyn compilations. Each compilation
    /// pulls in the BCL ref pack and the assembly's transitive
    /// references, so memory grows quickly with parallelism.
    /// </summary>
    private const int MaxParallelCompilations = 3;

    /// <summary>Walker invoked for each loaded assembly.</summary>
    private readonly ISymbolWalker _symbolWalker;

    /// <summary>Factory invoked once per TFM group to create the loader for that group.</summary>
    private readonly Func<ILogger, ICompilationLoader> _loaderFactory;

    /// <summary>Factory invoked once per assembly to create its scoped source-link resolver.</summary>
    private readonly Func<string, ISourceLinkResolver> _sourceLinkResolverFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="MetadataExtractor"/> class
    /// with the default production collaborators.
    /// </summary>
    public MetadataExtractor()
        : this(null, null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MetadataExtractor"/> class
    /// with the supplied walker and default loader / source-link collaborators.
    /// </summary>
    /// <param name="symbolWalker">Walker invoked for each loaded assembly.</param>
    public MetadataExtractor(ISymbolWalker? symbolWalker)
        : this(symbolWalker, null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MetadataExtractor"/> class
    /// with the supplied walker and loader factory.
    /// </summary>
    /// <param name="symbolWalker">Walker invoked for each loaded assembly.</param>
    /// <param name="loaderFactory">Factory invoked once per TFM group to create the loader for that group.</param>
    public MetadataExtractor(ISymbolWalker? symbolWalker, Func<ILogger, ICompilationLoader>? loaderFactory)
        : this(symbolWalker, loaderFactory, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MetadataExtractor"/> class.
    /// </summary>
    /// <param name="symbolWalker">Walker invoked for each loaded assembly.</param>
    /// <param name="loaderFactory">Factory invoked once per TFM group to create the loader for that group.</param>
    /// <param name="sourceLinkResolverFactory">Factory invoked once per assembly to create its scoped source-link resolver.</param>
    public MetadataExtractor(
        ISymbolWalker? symbolWalker,
        Func<ILogger, ICompilationLoader>? loaderFactory,
        Func<string, ISourceLinkResolver>? sourceLinkResolverFactory)
    {
        _symbolWalker = symbolWalker ?? new SymbolWalker();
        _loaderFactory = loaderFactory ?? (static logger => new CompilationLoader(logger));
        _sourceLinkResolverFactory = sourceLinkResolverFactory ?? (static path => new SourceLinkResolver(path));
    }

    /// <inheritdoc />
    public Task<ExtractionResult> RunAsync(
        IAssemblySource source,
        string outputRoot,
        IDocumentationEmitter emitter) =>
        RunAsync(source, outputRoot, emitter, null, CancellationToken.None);

    /// <inheritdoc />
    public Task<ExtractionResult> RunAsync(
        IAssemblySource source,
        string outputRoot,
        IDocumentationEmitter emitter,
        ILogger? logger) =>
        RunAsync(source, outputRoot, emitter, logger, CancellationToken.None);

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">When <paramref name="source"/> or <paramref name="emitter"/> is null.</exception>
    /// <exception cref="ArgumentException">When <paramref name="outputRoot"/> is null, empty, or whitespace.</exception>
    /// <exception cref="InvalidOperationException">When the source produced no TFM groups.</exception>
    public Task<ExtractionResult> RunAsync(
        IAssemblySource source,
        string outputRoot,
        IDocumentationEmitter emitter,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        ArgumentNullException.ThrowIfNull(emitter);

        return RunInternalAsync(source, outputRoot, emitter, logger ?? NullLogger.Instance, cancellationToken);
    }

    /// <summary>
    /// Internal implementation of the documentation pipeline.
    /// </summary>
    /// <param name="source">The assembly source.</param>
    /// <param name="outputRoot">The output root path.</param>
    /// <param name="emitter">The documentation emitter.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that represents the asynchronous extraction operation.</returns>
    private async Task<ExtractionResult> RunInternalAsync(
        IAssemblySource source,
        string outputRoot,
        IDocumentationEmitter emitter,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        MetadataIoHelper.PrepareOutputDirectory(outputRoot);

        using var loaderRegistry = new LoaderRegistry();
        var groups = await MetadataDiscoveryHelper.DiscoverTfmGroupsAsync(source, _loaderFactory, loaderRegistry, logger, cancellationToken).ConfigureAwait(false);

        var merger = new StreamingTypeMerger();
        var loadFailureBox = new StrongBox<int>();
        var catalogCount = new StrongBox<int>();
        var context = new WalkContext(
            _symbolWalker,
            _sourceLinkResolverFactory,
            logger,
            merger,
            catalogCount,
            loadFailureBox);

        var workItems = MetadataWalkerHelper.BuildAssemblyWorkItems(groups, context);
        MetadataWalkerHelper.LogWalking(logger, workItems.Count, groups.Count, MaxParallelCompilations);

        await MetadataWalkerHelper.WalkAssembliesAsync(workItems, MaxParallelCompilations, cancellationToken).ConfigureAwait(false);

        var loadFailures = loadFailureBox.Value;
        MetadataWalkerHelper.LogWalkComplete(logger, catalogCount.Value);
        var merged = merger.Build();

        MetadataLoggingHelper.LogEmitting(logger, merged.Length, outputRoot, emitter.GetType().Name);
        var pagesEmitted = await emitter.EmitAsync(merged, outputRoot, cancellationToken).ConfigureAwait(false);

        var sourceLinks = MetadataSourceLinkHelper.CollectSourceLinks(merged);
        MetadataLoggingHelper.LogEmitComplete(logger, merged.Length, pagesEmitted, sourceLinks.Length, loadFailures);

        return new(
            CanonicalTypes: merged.Length,
            PagesEmitted: pagesEmitted,
            LoadFailures: loadFailures,
            SourceLinks: sourceLinks);
    }
}
