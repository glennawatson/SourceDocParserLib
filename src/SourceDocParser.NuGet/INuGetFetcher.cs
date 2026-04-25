// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.NuGet;

/// <summary>
/// Fetches the NuGet packages described by a configuration file and
/// extracts their managed assemblies into TFM-bucketed directories so
/// the parser can walk them.
/// </summary>
public interface INuGetFetcher
{
    /// <summary>
    /// Reads <c>nuget-packages.json</c> at <paramref name="rootDirectory"/>
    /// and orchestrates the full fetch + extraction into <paramref name="apiPath"/>.
    /// </summary>
    /// <param name="rootDirectory">Repository root containing <c>nuget-packages.json</c>.</param>
    /// <param name="apiPath">Destination root for extracted package assemblies and the local cache.</param>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    /// <param name="cancellationToken">Cancellation token honoured by every HTTP and parallel-walk leg.</param>
    /// <returns>A task representing the asynchronous fetch.</returns>
    Task FetchPackagesAsync(string rootDirectory, string apiPath, ILogger? logger = null, CancellationToken cancellationToken = default);
}
