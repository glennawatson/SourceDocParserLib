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
public sealed partial class MetadataExtractor : IMetadataExtractor
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
    /// Initializes a new instance of the <see cref="MetadataExtractor"/> class.
    /// All collaborators are optional; <c>null</c> falls back to the default
    /// production implementation. Tests substitute fakes by passing in
    /// alternative implementations.
    /// </summary>
    /// <param name="symbolWalker">Walker invoked for each loaded assembly. Defaults to <see cref="SymbolWalker"/>.</param>
    /// <param name="loaderFactory">Factory invoked once per TFM group to create the loader for that group; receives the per-run logger. Defaults to <c>logger =&gt; new CompilationLoader(logger)</c>.</param>
    /// <param name="sourceLinkResolverFactory">Factory invoked once per assembly to create its scoped source-link resolver; receives the absolute assembly path. Defaults to <c>path =&gt; new SourceLinkResolver(path)</c>.</param>
    public MetadataExtractor(
        ISymbolWalker? symbolWalker = null,
        Func<ILogger, ICompilationLoader>? loaderFactory = null,
        Func<string, ISourceLinkResolver>? sourceLinkResolverFactory = null)
    {
        _symbolWalker = symbolWalker ?? new SymbolWalker();
        _loaderFactory = loaderFactory ?? (static logger => new CompilationLoader(logger));
        _sourceLinkResolverFactory = sourceLinkResolverFactory ?? (static path => new SourceLinkResolver(path));
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">When <paramref name="source"/> or <paramref name="emitter"/> is null.</exception>
    /// <exception cref="ArgumentException">When <paramref name="outputRoot"/> is null, empty, or whitespace.</exception>
    /// <exception cref="InvalidOperationException">When the source produced no TFM groups.</exception>
    public async Task<ExtractionResult> RunAsync(
        IAssemblySource source,
        string outputRoot,
        IDocumentationEmitter emitter,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        ArgumentNullException.ThrowIfNull(emitter);
        logger ??= NullLogger.Instance;

        if (Directory.Exists(outputRoot))
        {
            Directory.Delete(outputRoot, recursive: true);
        }

        Directory.CreateDirectory(outputRoot);

        using var loaderRegistry = new LoaderRegistry();
        var groups = new List<TfmGroup>();
        await foreach (var group in source.DiscoverAsync(cancellationToken).ConfigureAwait(false))
        {
            var loader = loaderRegistry.Track(_loaderFactory(logger));
            groups.Add(new(group, loader, group.AssemblyPaths.Length));
        }

        if (groups.Count is 0)
        {
            throw new InvalidOperationException("Assembly source produced no TFM groups.");
        }

        LogDiscoveredGroups(logger, groups.Count, source.GetType().Name);

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

        var workItems = BuildAssemblyWorkItems(groups, context);
        LogWalking(logger, workItems.Count, groups.Count, MaxParallelCompilations);

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = MaxParallelCompilations,
            CancellationToken = cancellationToken,
        };

        // Static lambda — every dependency the body needs is reachable
        // through the enriched AssemblyWorkItem, so the lambda captures
        // nothing and Parallel.ForEachAsync doesn't allocate a closure
        // per dispatch.
        await Parallel.ForEachAsync(
            workItems,
            parallelOptions,
            static (work, _) =>
            {
                var ctx = work.Context;
                if (LoadAndWalkAssembly(work, ctx.SymbolWalker, ctx.SourceLinkResolverFactory, ctx.Logger) is { } catalog)
                {
                    // Stream into the merger and drop the catalog reference
                    // immediately — no ConcurrentBag holding every catalog
                    // alive until the walk phase finishes.
                    ctx.Merger.Add(catalog);
                    Interlocked.Increment(ref ctx.CatalogCount.Value);
                }
                else
                {
                    Interlocked.Increment(ref ctx.LoadFailures.Value);
                }

                // Eager loader disposal: once this group's last assembly
                // completes its walk, dispose its CompilationLoader so the
                // BCL ref-pack memory-mapped views are released
                // immediately rather than at RunAsync exit.
                work.Owner.TryRetire();

                return ValueTask.CompletedTask;
            }).ConfigureAwait(false);

        var loadFailures = loadFailureBox.Value;
        LogWalkComplete(logger, catalogCount.Value);
        var merged = merger.Build();

        LogEmitting(logger, merged.Length, outputRoot, emitter.GetType().Name);
        var pagesEmitted = await emitter.EmitAsync(merged, outputRoot, cancellationToken).ConfigureAwait(false);

        var sourceLinks = CollectSourceLinks(merged);
        LogEmitComplete(logger, merged.Length, pagesEmitted, sourceLinks.Length, loadFailures);

        return new(
            CanonicalTypes: merged.Length,
            PagesEmitted: pagesEmitted,
            LoadFailures: loadFailures,
            SourceLinks: sourceLinks);
    }

    /// <summary>
    /// Pulls every non-null source URL (type and member level) out of
    /// the merged catalog so the optional validator can be handed a
    /// flat list without re-walking the catalog.
    /// </summary>
    /// <param name="merged">Merged canonical types.</param>
    /// <returns>One entry per documented source URL.</returns>
    private static SourceLinkEntry[] CollectSourceLinks(ApiType[] merged)
    {
        // Most types contribute 0–1 source URLs (the type-level URL,
        // occasionally a member URL on top), so the type count is the
        // right capacity hint — leaves room for the dominant case
        // without front-loading dead capacity on a large catalog.
        var entries = new List<SourceLinkEntry>(merged.Length);

        for (var t = 0; t < merged.Length; t++)
        {
            var type = merged[t];
            if (type.SourceUrl is { Length: > 0 } typeUrl)
            {
                entries.Add(new(type.Uid, typeUrl));
            }

            var members = type switch
            {
                ApiObjectType o => o.Members,
                ApiUnionType u => u.Members,
                _ => null,
            };

            if (members is null)
            {
                continue;
            }

            for (var m = 0; m < members.Length; m++)
            {
                var member = members[m];
                if (member.SourceUrl is { Length: > 0 } memberUrl)
                {
                    entries.Add(new(member.Uid, memberUrl));
                }
            }
        }

        return [.. entries];
    }

    /// <summary>
    /// Flattens (TFM, assembly) tuples into a single work-item list so
    /// the parallel walker runs every assembly under one shared
    /// concurrency budget instead of nesting per-TFM loops.
    /// </summary>
    /// <param name="groups">Per-TFM groups produced by the source.</param>
    /// <param name="context">Shared walk context attached to every work item so the parallel lambda can stay capture-free.</param>
    /// <returns>One work item per assembly across every group.</returns>
    private static List<AssemblyWorkItem> BuildAssemblyWorkItems(List<TfmGroup> groups, WalkContext context)
    {
        var total = 0;
        for (var i = 0; i < groups.Count; i++)
        {
            total += groups[i].Group.AssemblyPaths.Length;
        }

        var items = new List<AssemblyWorkItem>(total);
        for (var i = 0; i < groups.Count; i++)
        {
            var owner = groups[i];
            var paths = owner.Group.AssemblyPaths;
            for (var j = 0; j < paths.Length; j++)
            {
                items.Add(new(owner, paths[j], context));
            }
        }

        return items;
    }

    /// <summary>
    /// Loads one assembly into a Roslyn compilation and walks its
    /// public surface into an <see cref="ApiCatalog"/>. Returns null on
    /// failure so the caller can count failures without aborting.
    /// </summary>
    /// <param name="work">Work item to process.</param>
    /// <param name="symbolWalker">Walker invoked for the loaded assembly.</param>
    /// <param name="sourceLinkResolverFactory">Factory that produces the per-assembly source-link resolver.</param>
    /// <param name="logger">Logger for progress and failure messages.</param>
    /// <returns>The walked catalog, or null on load/walk failure.</returns>
    private static ApiCatalog? LoadAndWalkAssembly(
        AssemblyWorkItem work,
        ISymbolWalker symbolWalker,
        Func<string, ISourceLinkResolver> sourceLinkResolverFactory,
        ILogger logger)
    {
        var tfm = work.Owner.Group.Tfm;
        try
        {
            var (compilation, assembly) = work.Owner.Loader.Load(work.AssemblyPath, work.Owner.Group.FallbackIndex);
            using var sourceLinks = sourceLinkResolverFactory(work.AssemblyPath);
            var catalog = symbolWalker.Walk(work.Owner.Group.Tfm, assembly, compilation, sourceLinks);
            LogInvokerHelper.Invoke(
                logger,
                LogLevel.Trace,
                tfm,
                catalog.Types.Length,
                work.AssemblyPath,
                static assemblyPath => Path.GetFileNameWithoutExtension(assemblyPath),
                static (l, walkTfm, typeCount, assemblyName) => LogAssemblyWalked(l, walkTfm, assemblyName, typeCount));

            return catalog;
        }
        catch (Exception ex)
        {
            LogInvokerHelper.Invoke(
                logger,
                LogLevel.Error,
                ex,
                tfm,
                work.AssemblyPath,
                static assemblyPath => Path.GetFileNameWithoutExtension(assemblyPath),
                static (l, error, walkTfm, assemblyName) => LogAssemblyLoadFailed(l, error, walkTfm, assemblyName));
            return null;
        }
    }

    /// <summary>Logs the count of TFM groups discovered from the source.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="groupCount">Number of TFM groups.</param>
    /// <param name="sourceType">Concrete source type name.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Discovered {GroupCount} TFM group(s) from {SourceType}")]
    private static partial void LogDiscoveredGroups(ILogger logger, int groupCount, string sourceType);

    /// <summary>Logs the start of the parallel walk phase.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="assemblyCount">Total assemblies to walk.</param>
    /// <param name="tfmCount">Number of TFM groups they span.</param>
    /// <param name="maxParallel">Concurrency budget for the walk.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Walking {AssemblyCount} package assembly/ies across {TfmCount} TFM(s) (max {MaxParallel} concurrent)")]
    private static partial void LogWalking(ILogger logger, int assemblyCount, int tfmCount, int maxParallel);

    /// <summary>Logs completion of the walk phase before merging.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="catalogCount">Number of per-assembly catalogs collected.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Walked {CatalogCount} catalog(s); merging across TFMs")]
    private static partial void LogWalkComplete(ILogger logger, int catalogCount);

    /// <summary>Logs the start of the emit phase.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="typeCount">Canonical types being emitted.</param>
    /// <param name="outputRoot">Destination directory.</param>
    /// <param name="emitterType">Concrete emitter type name.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Emitting {TypeCount} canonical type page(s) to {OutputRoot} via {EmitterType}")]
    private static partial void LogEmitting(ILogger logger, int typeCount, string outputRoot, string emitterType);

    /// <summary>Logs the end-of-run summary.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="typeCount">Canonical types emitted.</param>
    /// <param name="pageCount">Total pages written.</param>
    /// <param name="sourceLinkCount">Source-link entries collected.</param>
    /// <param name="loadFailures">Assembly load/walk failures.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Markdown emission complete: {TypeCount} canonical type(s), {PageCount} total page(s) emitted, {SourceLinkCount} source link(s) collected, {LoadFailures} load failure(s)")]
    private static partial void LogEmitComplete(ILogger logger, int typeCount, int pageCount, int sourceLinkCount, int loadFailures);

    /// <summary>Logs successful walk of a single assembly.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="tfm">TFM the assembly belongs to.</param>
    /// <param name="assembly">Assembly name (no extension).</param>
    /// <param name="typeCount">Public types discovered.</param>
    [LoggerMessage(Level = LogLevel.Trace, Message = "    {Tfm}/{Assembly}: {TypeCount} public type(s)")]
    private static partial void LogAssemblyWalked(ILogger logger, string tfm, string assembly, int typeCount);

    /// <summary>Logs failure to load or walk a single assembly.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="exception">Exception thrown during load or walk.</param>
    /// <param name="tfm">TFM the assembly belongs to.</param>
    /// <param name="assembly">Assembly name (no extension).</param>
    [LoggerMessage(Level = LogLevel.Error, Message = "    {Tfm}/{Assembly}: load failed")]
    private static partial void LogAssemblyLoadFailed(ILogger logger, Exception exception, string tfm, string assembly);

    /// <summary>
    /// Pairs an <see cref="AssemblyGroup"/> with the per-TFM compilation
    /// loader and an outstanding-work counter. <see cref="TryRetire"/> is
    /// called by every parallel worker as it finishes an assembly; the
    /// last caller (when the counter hits zero) disposes the loader so
    /// the group's memory-mapped BCL ref pack views are released as soon
    /// as that group has no more pending walks.
    /// </summary>
    private sealed class TfmGroup
    {
        /// <summary>Outstanding-walk counter; written via <see cref="Interlocked.Decrement(ref int)"/>.</summary>
        private int _remaining;

        /// <summary>Initializes a new instance of the <see cref="TfmGroup"/> class.</summary>
        /// <param name="group">Source-supplied assembly group.</param>
        /// <param name="loader">Per-TFM compilation loader (owns the metadata reference cache for the group).</param>
        /// <param name="totalWalks">Number of assemblies the parallel walker will process for this group.</param>
        public TfmGroup(AssemblyGroup group, ICompilationLoader loader, int totalWalks)
        {
            Group = group;
            Loader = loader;
            _remaining = totalWalks;
        }

        /// <summary>Gets the source-supplied assembly group.</summary>
        public AssemblyGroup Group { get; }

        /// <summary>Gets the compilation loader scoped to this group.</summary>
        public ICompilationLoader Loader { get; }

        /// <summary>
        /// Decrements the outstanding-walk counter. When the counter hits
        /// zero this is the last walk in the group, so the loader is
        /// disposed immediately. Subsequent loader-dispose calls (e.g.
        /// from <see cref="LoaderRegistry"/>) are idempotent so the
        /// safety-net path stays correct.
        /// </summary>
        public void TryRetire()
        {
            if (Interlocked.Decrement(ref _remaining) != 0)
            {
                return;
            }

            Loader.Dispose();
        }
    }

    /// <summary>
    /// One assembly to walk along with the TFM group it belongs to and
    /// the shared <see cref="WalkContext"/> the parallel lambda needs.
    /// Bundling the context onto the item lets the lambda be <c>static</c>
    /// — no captures, no per-dispatch closure allocation.
    /// </summary>
    /// <param name="Owner">Owning TFM group (provides fallback index and loader).</param>
    /// <param name="AssemblyPath">Absolute path to the assembly to walk.</param>
    /// <param name="Context">Shared dependencies + result accumulators.</param>
    private sealed record AssemblyWorkItem(TfmGroup Owner, string AssemblyPath, WalkContext Context);

    /// <summary>
    /// Bundle of every dependency and accumulator the parallel walk
    /// lambda needs. One instance is built per <see cref="RunAsync"/>
    /// invocation and attached to every <see cref="AssemblyWorkItem"/>
    /// so the lambda can stay <c>static</c>.
    /// </summary>
    /// <param name="SymbolWalker">Walker invoked for each loaded assembly.</param>
    /// <param name="SourceLinkResolverFactory">Factory producing the per-assembly source-link resolver.</param>
    /// <param name="Logger">Logger for progress and failure messages.</param>
    /// <param name="Merger">Streaming merger that absorbs each catalog as soon as it lands.</param>
    /// <param name="CatalogCount">Box-wrapped counter of successfully-walked catalogs (used for the post-walk log line).</param>
    /// <param name="LoadFailures">Box-wrapped counter so workers can <see cref="Interlocked.Increment(ref int)"/> without capturing a local.</param>
    private sealed record WalkContext(
        ISymbolWalker SymbolWalker,
        Func<string, ISourceLinkResolver> SourceLinkResolverFactory,
        ILogger Logger,
        StreamingTypeMerger Merger,
        StrongBox<int> CatalogCount,
        StrongBox<int> LoadFailures);

    /// <summary>
    /// Holds every per-TFM <see cref="ICompilationLoader"/> created during
    /// a single <see cref="RunAsync"/> invocation and disposes them on
    /// scope exit so the memory-mapped DLL views the BCL ref pack pins
    /// are released as soon as the run finishes. Wrapped in a single
    /// <c>using var</c> at the top of <see cref="RunAsync"/>.
    /// </summary>
    private sealed class LoaderRegistry : IDisposable
    {
        /// <summary>Tracked loaders in registration order.</summary>
        private readonly List<ICompilationLoader> _loaders = [];

        /// <summary>
        /// Registers <paramref name="loader"/> for disposal and returns it
        /// for fluent assignment at the call site.
        /// </summary>
        /// <param name="loader">Loader to track.</param>
        /// <returns>The same loader instance.</returns>
        public ICompilationLoader Track(ICompilationLoader loader)
        {
            _loaders.Add(loader);
            return loader;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            for (var i = 0; i < _loaders.Count; i++)
            {
                _loaders[i].Dispose();
            }

            _loaders.Clear();
        }
    }
}
