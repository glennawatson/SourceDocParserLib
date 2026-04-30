// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser;

/// <summary>
/// Helper methods for Metadata Extractor logging.
/// </summary>
internal static partial class MetadataLoggingHelper
{
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

    /// <summary>Logs the end of a direct-mode extract — no emitter ran, the merged catalog is being returned to the caller.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="typeCount">Canonical types in the returned catalog.</param>
    /// <param name="sourceLinkCount">Source-link entries collected.</param>
    /// <param name="loadFailures">Assembly load/walk failures.</param>
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Direct-mode extract complete: {TypeCount} canonical type(s) returned, {SourceLinkCount} source link(s) collected, {LoadFailures} load failure(s)")]
    public static partial void LogDirectExtractComplete(ILogger logger, int typeCount, int sourceLinkCount, int loadFailures);
}
