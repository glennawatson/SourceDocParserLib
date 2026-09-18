// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.NuGet.Infrastructure;

/// <summary>Acquires the packages and compile references required by the documentation manifest.</summary>
public interface INuGetFetcher
{
    /// <summary>Restores the documentation roots declared in <c>nuget-packages.json</c>.</summary>
    /// <param name="rootDirectory">Repository root containing <c>nuget-packages.json</c>.</param>
    /// <param name="apiPath">Destination for per-root restore graphs; packages use NuGet's configured global cache.</param>
    /// <returns>A task representing the asynchronous fetch.</returns>
    Task FetchPackagesAsync(string rootDirectory, string apiPath);

    /// <summary>Restores the documentation roots and reports NuGet diagnostics to the supplied logger.</summary>
    /// <param name="rootDirectory">Repository root containing <c>nuget-packages.json</c>.</param>
    /// <param name="apiPath">Destination for per-root restore graphs; packages use NuGet's configured global cache.</param>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    /// <returns>A task representing the asynchronous fetch.</returns>
    Task FetchPackagesAsync(string rootDirectory, string apiPath, ILogger? logger);

    /// <summary>Restores the documentation roots with cancellation support.</summary>
    /// <param name="rootDirectory">Repository root containing <c>nuget-packages.json</c>.</param>
    /// <param name="apiPath">Destination for per-root restore graphs; packages use NuGet's configured global cache.</param>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    /// <param name="cancellationToken">Cancellation token honoured by every HTTP and parallel-walk leg.</param>
    /// <returns>A task representing the asynchronous fetch.</returns>
    Task FetchPackagesAsync(string rootDirectory, string apiPath, ILogger? logger, CancellationToken cancellationToken);
}
