// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.LibCompilation;

namespace SourceDocParser;

/// <summary>
/// Helper methods for Metadata Extractor discovery phase.
/// </summary>
internal static partial class MetadataDiscoveryHelper
{
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
        List<TfmGroup> groups = [];
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

    /// <summary>Logs the count of TFM groups discovered from the source.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="groupCount">Number of TFM groups.</param>
    /// <param name="sourceType">Concrete source type name.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Discovered {GroupCount} TFM group(s) from {SourceType}")]
    public static partial void LogDiscoveredGroups(ILogger logger, int groupCount, string sourceType);
}
