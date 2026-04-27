// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.LibCompilation;
using SourceDocParser.Model;
using SourceDocParser.SourceLink;
using SourceDocParser.Walk;

namespace SourceDocParser;

/// <summary>
/// Helper methods for the <see cref="MetadataExtractor"/>.
/// </summary>
internal static partial class MetadataExtractorHelper
{
    /// <summary>
    /// Prepares the output directory by deleting it if it exists and creating it.
    /// </summary>
    /// <param name="outputRoot">The output root path.</param>
    public static void PrepareOutputDirectory(string outputRoot)
    {
        if (Directory.Exists(outputRoot))
        {
            Directory.Delete(outputRoot, recursive: true);
        }

        Directory.CreateDirectory(outputRoot);
    }

    /// <summary>
    /// Discovers TFM groups from the source.
    /// </summary>
    /// <param name="source">The assembly source.</param>
    /// <param name="loaderFactory">Factory to create compilation loaders.</param>
    /// <param name="loaderRegistry">Registry to track loaders for disposal.</param>
    /// <param name="logger">Target logger.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of discovered TFM groups.</returns>
    public static async Task<List<TfmGroup>> DiscoverTfmGroupsAsync(
        IAssemblySource source,
        Func<ILogger, ICompilationLoader> loaderFactory,
        LoaderRegistry loaderRegistry,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var groups = new List<TfmGroup>();
        await foreach (var group in source.DiscoverAsync(cancellationToken).ConfigureAwait(false))
        {
            var loader = loaderRegistry.Track(loaderFactory(logger));
            groups.Add(new(group, loader, group.AssemblyPaths.Length));
        }

        if (groups.Count is 0)
        {
            throw new InvalidOperationException($"Assembly {nameof(source)} produced no TFM groups.");
        }

        LogDiscoveredGroups(logger, groups.Count, source.GetType().Name);
        return groups;
    }

    /// <summary>
    /// Executes the parallel walk phase.
    /// </summary>
    /// <param name="workItems">One work item per assembly across every group.</param>
    /// <param name="maxParallel">Concurrency budget for the walk.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that represents the asynchronous walk operation.</returns>
    public static async Task WalkAssembliesAsync(
        List<AssemblyWorkItem> workItems,
        int maxParallel,
        CancellationToken cancellationToken)
    {
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxParallel,
            CancellationToken = cancellationToken,
        };

        await Parallel.ForEachAsync(
            workItems,
            parallelOptions,
            static (work, _) =>
            {
                var ctx = work.Context;
                if (LoadAndWalkAssembly(work, ctx.SymbolWalker, ctx.SourceLinkResolverFactory, ctx.Logger) is { } catalog)
                {
                    ctx.Merger.Add(catalog);
                    Interlocked.Increment(ref ctx.CatalogCount.Value);
                }
                else
                {
                    Interlocked.Increment(ref ctx.LoadFailures.Value);
                }

                work.Owner.TryRetire();
                return ValueTask.CompletedTask;
            }).ConfigureAwait(false);
    }

    /// <summary>
    /// Pulls every non-null source URL (type and member level) out of
    /// the merged catalog so the optional validator can be handed a
    /// flat list without re-walking the catalog.
    /// </summary>
    /// <param name="merged">Merged canonical types.</param>
    /// <returns>One entry per documented source URL.</returns>
    public static SourceLinkEntry[] CollectSourceLinks(ApiType[] merged)
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
    public static List<AssemblyWorkItem> BuildAssemblyWorkItems(List<TfmGroup> groups, WalkContext context)
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
    public static ApiCatalog? LoadAndWalkAssembly(
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
    public static partial void LogDiscoveredGroups(ILogger logger, int groupCount, string sourceType);

    /// <summary>Logs the start of the parallel walk phase.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="assemblyCount">Total assemblies to walk.</param>
    /// <param name="tfmCount">Number of TFM groups they span.</param>
    /// <param name="maxParallel">Concurrency budget for the walk.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Walking {AssemblyCount} package assembly/ies across {TfmCount} TFM(s) (max {MaxParallel} concurrent)")]
    public static partial void LogWalking(ILogger logger, int assemblyCount, int tfmCount, int maxParallel);

    /// <summary>Logs completion of the walk phase before merging.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="catalogCount">Number of per-assembly catalogs collected.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Walked {CatalogCount} catalog(s); merging across TFMs")]
    public static partial void LogWalkComplete(ILogger logger, int catalogCount);

    /// <summary>Logs the start of the emit phase.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="typeCount">Canonical types being emitted.</param>
    /// <param name="outputRoot">Destination directory.</param>
    /// <param name="emitterType">Concrete emitter type name.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Emitting {TypeCount} canonical type page(s) to {OutputRoot} via {EmitterType}")]
    public static partial void LogEmitting(ILogger logger, int typeCount, string outputRoot, string emitterType);

    /// <summary>Logs the end-of-run summary.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="typeCount">Canonical types emitted.</param>
    /// <param name="pageCount">Total pages written.</param>
    /// <param name="sourceLinkCount">Source-link entries collected.</param>
    /// <param name="loadFailures">Assembly load/walk failures.</param>
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Markdown emission complete: {TypeCount} canonical type(s), {PageCount} total page(s) emitted, {SourceLinkCount} source link(s) collected, {LoadFailures} load failure(s)")]
    public static partial void LogEmitComplete(ILogger logger, int typeCount, int pageCount, int sourceLinkCount, int loadFailures);

    /// <summary>Logs successful walk of a single assembly.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="tfm">TFM the assembly belongs to.</param>
    /// <param name="assembly">Assembly name (no extension).</param>
    /// <param name="typeCount">Public types discovered.</param>
    [LoggerMessage(Level = LogLevel.Trace, Message = "    {Tfm}/{Assembly}: {TypeCount} public type(s)")]
    public static partial void LogAssemblyWalked(ILogger logger, string tfm, string assembly, int typeCount);

    /// <summary>Logs failure to load or walk a single assembly.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="exception">Exception thrown during load or walk.</param>
    /// <param name="tfm">TFM the assembly belongs to.</param>
    /// <param name="assembly">Assembly name (no extension).</param>
    [LoggerMessage(Level = LogLevel.Error, Message = "    {Tfm}/{Assembly}: load failed")]
    public static partial void LogAssemblyLoadFailed(ILogger logger, Exception exception, string tfm, string assembly);
}
