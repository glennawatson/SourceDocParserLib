// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.LibCompilation;
using SourceDocParser.Model;
using SourceDocParser.SourceLink;
using SourceDocParser.Walk;

namespace SourceDocParser;

/// <summary>
/// Helper methods for Metadata Extractor walk phase.
/// </summary>
internal static partial class MetadataWalkerHelper
{
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
                    ctx.TypesByTfm.GetOrAdd(catalog.Tfm, static _ => []).Add(catalog.Types);
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
