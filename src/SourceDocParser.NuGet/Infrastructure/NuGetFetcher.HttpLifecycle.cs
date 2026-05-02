// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.NuGet.Infrastructure;

/// <summary>HTTP-client lifecycle for <see cref="NuGetFetcher"/> — held in a partial split so the main file stays under the per-file line cap.</summary>
public sealed partial class NuGetFetcher
{
    /// <summary>Lifetime ceiling on a pooled connection; matches the long-lived-host default so DNS / TLS state can't stay warm past two minutes.</summary>
    private static readonly TimeSpan PooledConnectionLifetime = TimeSpan.FromMinutes(2);

    /// <summary>Idle ceiling on a pooled connection — drops sockets silent past this window so the pool doesn't grow unboundedly across a long fetch.</summary>
    private static readonly TimeSpan PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Shared <see cref="HttpClient"/> reused across every NuGet feed call this fetcher issues.</summary>
    /// <remarks>
    /// Backed by a tuned <see cref="SocketsHttpHandler"/> — pooled connections with bounded
    /// lifetime + idle timeout. Per-call <c>using var client = new HttpClient()</c> would
    /// re-handshake against <c>api.nuget.org</c> on every request and leave a fresh
    /// <see cref="SocketsHttpHandler"/> for the GC to finalize at process shutdown,
    /// blocking process exit while idle TLS connections drained.
    /// </remarks>
    private readonly HttpClient _httpClient;

    /// <summary>True when the fetcher built <see cref="_httpClient"/> itself and must therefore dispose it.</summary>
    private readonly bool _ownsHttpClient;

    /// <summary>Initializes a new instance of the <see cref="NuGetFetcher"/> class with a fetcher-owned shared <see cref="HttpClient"/>.</summary>
    public NuGetFetcher()
        : this(httpClient: null, ownsHttpClient: true)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="NuGetFetcher"/> class with a caller-supplied <see cref="HttpClient"/>.</summary>
    /// <param name="httpClient">Caller-supplied client; the fetcher does not dispose it.</param>
    /// <remarks>Test seam — production callers use the parameterless constructor.</remarks>
    public NuGetFetcher(HttpClient httpClient)
        : this(httpClient ?? throw new ArgumentNullException(nameof(httpClient)), ownsHttpClient: false)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="NuGetFetcher"/> class; private to keep the public surface to the two intent-named overloads.</summary>
    /// <param name="httpClient">Caller-supplied client (used as-is) or null (build one).</param>
    /// <param name="ownsHttpClient">True when <see cref="Dispose"/> should dispose <paramref name="httpClient"/>.</param>
    private NuGetFetcher(HttpClient? httpClient, bool ownsHttpClient)
    {
        _httpClient = httpClient ?? CreateSharedHttpClient();
        _ownsHttpClient = ownsHttpClient;
    }

    /// <summary>Disposes the owned <see cref="HttpClient"/> when this instance constructed it.</summary>
    public void Dispose()
    {
        if (!_ownsHttpClient)
        {
            return;
        }

        _httpClient.Dispose();
    }

    /// <summary>Builds a fresh <see cref="HttpClient"/> backed by a tuned <see cref="SocketsHttpHandler"/> — pooled connections with bounded lifetime + idle timeout.</summary>
    /// <returns>A shared client suitable for the lifetime of one fetcher instance.</returns>
    private static HttpClient CreateSharedHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = PooledConnectionLifetime,
            PooledConnectionIdleTimeout = PooledConnectionIdleTimeout,
        };
        return new HttpClient(handler, disposeHandler: true);
    }
}
