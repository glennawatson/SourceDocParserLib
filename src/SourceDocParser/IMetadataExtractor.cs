// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;

namespace SourceDocParser;

/// <summary>
/// Drives the documentation pipeline: pulls assemblies from an
/// <see cref="IAssemblySource"/>, walks each into an
/// <see cref="ApiCatalog"/>, merges duplicates across TFMs, and hands
/// the merged catalog to an <see cref="IDocumentationEmitter"/>.
/// </summary>
public interface IMetadataExtractor
{
    /// <summary>
    /// Runs the extraction pipeline end-to-end using default logging and cancellation settings.
    /// </summary>
    /// <param name="source">Provides the per-TFM assemblies to walk.</param>
    /// <param name="outputRoot">Destination directory for emitted pages.</param>
    /// <param name="emitter">Format-specific page emitter.</param>
    /// <returns>Summary of what was extracted, emitted, and any failures.</returns>
    Task<ExtractionResult> RunAsync(
        IAssemblySource source,
        string outputRoot,
        IDocumentationEmitter emitter);

    /// <summary>
    /// Runs the extraction pipeline end-to-end using the supplied logger.
    /// </summary>
    /// <param name="source">Provides the per-TFM assemblies to walk.</param>
    /// <param name="outputRoot">Destination directory for emitted pages.</param>
    /// <param name="emitter">Format-specific page emitter.</param>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    /// <returns>Summary of what was extracted, emitted, and any failures.</returns>
    Task<ExtractionResult> RunAsync(
        IAssemblySource source,
        string outputRoot,
        IDocumentationEmitter emitter,
        ILogger? logger);

    /// <summary>
    /// Runs the extraction pipeline end-to-end.
    /// </summary>
    /// <param name="source">Provides the per-TFM assemblies to walk.</param>
    /// <param name="outputRoot">Destination directory for emitted pages.</param>
    /// <param name="emitter">Format-specific page emitter.</param>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Summary of what was extracted, emitted, and any failures.</returns>
    Task<ExtractionResult> RunAsync(
        IAssemblySource source,
        string outputRoot,
        IDocumentationEmitter emitter,
        ILogger? logger,
        CancellationToken cancellationToken);

    /// <summary>
    /// Runs the walk-and-merge phase only and returns the merged
    /// canonical types directly, without invoking an emitter or
    /// touching disk. Use when the caller renders pages itself
    /// (direct-mode integration) instead of round-tripping through
    /// generated Markdown.
    /// </summary>
    /// <param name="source">Provides the per-TFM assemblies to walk.</param>
    /// <returns>The merged catalog plus load-failure and SourceLink summaries.</returns>
    Task<DirectExtractionResult> ExtractAsync(IAssemblySource source);

    /// <summary>
    /// Runs the walk-and-merge phase only and returns the merged
    /// canonical types directly, without invoking an emitter or
    /// touching disk.
    /// </summary>
    /// <param name="source">Provides the per-TFM assemblies to walk.</param>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    /// <returns>The merged catalog plus load-failure and SourceLink summaries.</returns>
    Task<DirectExtractionResult> ExtractAsync(IAssemblySource source, ILogger? logger);

    /// <summary>
    /// Runs the walk-and-merge phase only and returns the merged
    /// canonical types directly, without invoking an emitter or
    /// touching disk.
    /// </summary>
    /// <param name="source">Provides the per-TFM assemblies to walk.</param>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The merged catalog plus load-failure and SourceLink summaries.</returns>
    Task<DirectExtractionResult> ExtractAsync(
        IAssemblySource source,
        ILogger? logger,
        CancellationToken cancellationToken);
}
