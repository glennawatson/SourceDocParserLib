// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using SourceDocParser.SourceLink;

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

        var groups = new List<TfmGroup>();
        await foreach (var group in source.DiscoverAsync(cancellationToken).ConfigureAwait(false))
        {
            groups.Add(new(group, new(logger)));
        }

        if (groups.Count == 0)
        {
            throw new InvalidOperationException("Assembly source produced no TFM groups.");
        }

        LogDiscoveredGroups(logger, groups.Count, source.GetType().Name);

        var workItems = BuildAssemblyWorkItems(groups);
        LogWalking(logger, workItems.Count, groups.Count, MaxParallelCompilations);

        var catalogs = new ConcurrentBag<ApiCatalog>();
        var loadFailures = 0;
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = MaxParallelCompilations,
            CancellationToken = cancellationToken,
        };
        await Parallel.ForEachAsync(
            workItems,
            parallelOptions,
            (work, _) =>
            {
                if (LoadAndWalkAssembly(work, logger) is { } catalog)
                {
                    catalogs.Add(catalog);
                }
                else
                {
                    Interlocked.Increment(ref loadFailures);
                }

                return ValueTask.CompletedTask;
            }).ConfigureAwait(false);

        LogWalkComplete(logger, catalogs.Count);
        var merged = TypeMerger.Merge(catalogs);

        LogEmitting(logger, merged.Count, outputRoot, emitter.GetType().Name);
        var pagesEmitted = await emitter.EmitAsync(merged, outputRoot, cancellationToken).ConfigureAwait(false);

        var sourceLinks = CollectSourceLinks(merged);
        LogEmitComplete(logger, merged.Count, pagesEmitted, sourceLinks.Count, loadFailures);

        return new(
            CanonicalTypes: merged.Count,
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
    private static List<SourceLinkEntry> CollectSourceLinks(List<ApiType> merged)
    {
        var entries = new List<SourceLinkEntry>(merged.Count * 5);

        for (var t = 0; t < merged.Count; t++)
        {
            var type = merged[t];
            if (type.SourceUrl is { Length: > 0 } typeUrl)
            {
                entries.Add(new(type.Uid, typeUrl));
            }

            for (var m = 0; m < type.Members.Count; m++)
            {
                var member = type.Members[m];
                if (member.SourceUrl is { Length: > 0 } memberUrl)
                {
                    entries.Add(new(member.Uid, memberUrl));
                }
            }
        }

        return entries;
    }

    /// <summary>
    /// Flattens (TFM, assembly) tuples into a single work-item list so
    /// the parallel walker runs every assembly under one shared
    /// concurrency budget instead of nesting per-TFM loops.
    /// </summary>
    /// <param name="groups">Per-TFM groups produced by the source.</param>
    /// <returns>One work item per assembly across every group.</returns>
    private static List<AssemblyWorkItem> BuildAssemblyWorkItems(List<TfmGroup> groups)
    {
        var total = 0;
        for (var i = 0; i < groups.Count; i++)
        {
            total += groups[i].Group.AssemblyPaths.Count;
        }

        var items = new List<AssemblyWorkItem>(total);
        for (var i = 0; i < groups.Count; i++)
        {
            var ctx = groups[i];
            var paths = ctx.Group.AssemblyPaths;
            for (var j = 0; j < paths.Count; j++)
            {
                items.Add(new(ctx, paths[j]));
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
    /// <param name="logger">Logger for progress and failure messages.</param>
    /// <returns>The walked catalog, or null on load/walk failure.</returns>
    private static ApiCatalog? LoadAndWalkAssembly(AssemblyWorkItem work, ILogger logger)
    {
        var name = Path.GetFileNameWithoutExtension(work.AssemblyPath.AsSpan()).ToString();
        try
        {
            var (compilation, assembly) = CompilationLoader.Load(work.AssemblyPath, work.Owner.Group.FallbackIndex, work.Owner.ReferenceCache, logger);
            using var sourceLinks = new SourceLinkResolver(work.AssemblyPath);
            var catalog = SymbolWalker.Walk(work.Owner.Group.Tfm, assembly, compilation, sourceLinks);
            LogAssemblyWalked(logger, work.Owner.Group.Tfm, name, catalog.Types.Count);
            return catalog;
        }
        catch (Exception ex)
        {
            LogAssemblyLoadFailed(logger, ex, work.Owner.Group.Tfm, name);
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
    /// Pairs an <see cref="AssemblyGroup"/> from the source with the
    /// per-TFM metadata reference cache so every compilation in the
    /// group reuses the same loaded BCL ref pack.
    /// </summary>
    /// <param name="Group">Source-supplied assembly group.</param>
    /// <param name="ReferenceCache">Per-TFM metadata reference cache.</param>
    private sealed record TfmGroup(AssemblyGroup Group, MetadataReferenceCache ReferenceCache);

    /// <summary>
    /// One assembly to walk along with the TFM group it belongs to.
    /// </summary>
    /// <param name="Owner">Owning TFM group (provides fallback index and reference cache).</param>
    /// <param name="AssemblyPath">Absolute path to the assembly to walk.</param>
    private sealed record AssemblyWorkItem(TfmGroup Owner, string AssemblyPath);
}
