// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.NuGet.Models;

namespace SourceDocParser.NuGet.Infrastructure;

/// <summary>
/// HTTP surface the <see cref="GlobalCacheInstaller"/> drives -- one
/// method per logical NuGet feed operation. Lifted into an interface
/// so the installer can be unit-tested against a fake without a real
/// <c>HttpClient</c>; the concrete <see cref="NuGetFeedHttpClient"/>
/// implementation handles credentials, status-code translation, and
/// response disposal.
/// </summary>
internal interface INuGetFeedHttpClient : IDisposable
{
    /// <summary>
    /// Reads a NuGet v3 service-index document from <paramref name="url"/>.
    /// Throws when the request fails (no 404 special case -- a service
    /// index is expected to exist for any configured source).
    /// </summary>
    /// <param name="url">Service-index URL (the <c>@id</c> of the source).</param>
    /// <param name="credential">Optional Basic-auth credential.</param>
    /// <param name="cancellationToken">Token observed across the request.</param>
    /// <returns>The response stream; the caller disposes it.</returns>
    Task<Stream> ReadServiceIndexAsync(string url, PackageSourceCredential? credential, CancellationToken cancellationToken);

    /// <summary>
    /// Attempts to download a <c>.nupkg</c> from <paramref name="url"/>.
    /// Returns null when the source returns 404 so the caller can try
    /// the next source; throws on any other non-success status.
    /// </summary>
    /// <param name="url">Fully-formed flat-container <c>.nupkg</c> URL.</param>
    /// <param name="credential">Optional Basic-auth credential.</param>
    /// <param name="cancellationToken">Token observed across the request.</param>
    /// <returns>The response stream when the package was found; null on 404.</returns>
    Task<Stream?> TryDownloadNupkgAsync(string url, PackageSourceCredential? credential, CancellationToken cancellationToken);
}
