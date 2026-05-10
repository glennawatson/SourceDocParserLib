// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.NuGet.Infrastructure;

namespace SourceDocParser.NuGet.Tests;

/// <summary>
/// Pins <see cref="NuGetFetcher"/>'s constructor / <see cref="IDisposable"/>
/// contract: the parameterless overload owns the shared <see cref="HttpClient"/>
/// and disposes it when the fetcher is disposed; the caller-supplied overload
/// keeps the client alive past the fetcher's lifetime; null is rejected.
/// </summary>
public class NuGetFetcherHttpLifecycleTests
{
    /// <summary>The HttpClient-accepting constructor rejects <see langword="null"/>.</summary>
    /// <returns>Async test.</returns>
    [Test]
    public async Task ConstructorThrowsWhenInjectedHttpClientIsNull() =>
        await Assert.That(static () => new NuGetFetcher(httpClient: null!)).Throws<ArgumentNullException>();

    /// <summary>A caller-supplied <see cref="HttpClient"/> survives <see cref="NuGetFetcher.Dispose"/> — the fetcher only owns the client it built itself.</summary>
    /// <returns>Async test.</returns>
    [Test]
    public async Task DisposeLeavesInjectedHttpClientUsable()
    {
        using var handler = new TrackingHandler();
        using var client = new HttpClient(handler, disposeHandler: false);

        var fetcher = new NuGetFetcher(client);
        fetcher.Dispose();

        // If the fetcher had disposed the injected client, BaseAddress mutation would throw.
        client.BaseAddress = new("https://example.invalid/");
        await Assert.That(handler.DisposeCount).IsEqualTo(0);
    }

    /// <summary>The default-built fetcher disposes its owned <see cref="HttpClient"/> when the fetcher is disposed.</summary>
    /// <returns>Async test.</returns>
    [Test]
    public async Task DisposeDisposesOwnedHttpClient()
    {
        var fetcher = new NuGetFetcher();

        // Dispose twice to confirm the second call is a safe no-op.
        await Assert.That(() =>
        {
            fetcher.Dispose();
            fetcher.Dispose();
        }).ThrowsNothing();
    }

    /// <summary>HTTP handler that records its dispose count so the test can assert it stays untouched.</summary>
    private sealed class TrackingHandler : HttpMessageHandler
    {
        /// <summary>Gets the number of times <see cref="Dispose(bool)"/> has fired with <c>disposing: true</c>.</summary>
        public int DisposeCount { get; private set; }

        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCount++;
            }

            base.Dispose(disposing);
        }
    }
}
