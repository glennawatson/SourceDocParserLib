// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using SourceDocParser.NuGet.Infrastructure;
using SourceDocParser.NuGet.Models;

namespace SourceDocParser.NuGet.Tests;

/// <summary>
/// Pins <see cref="NuGetFeedHttpClient"/> via a fake
/// <see cref="HttpMessageHandler"/> — every status-code branch,
/// the credential-encoded Authorization header, and the
/// owns-vs-borrows disposal contract.
/// </summary>
public class NuGetFeedHttpClientTests
{
    /// <summary>Constructor rejects null HttpClient.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConstructorRejectsNullHttp() =>
        await Assert.That(() => new NuGetFeedHttpClient(null!)).Throws<ArgumentNullException>();

    /// <summary>ReadServiceIndexAsync returns the success-body stream.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadServiceIndexAsyncReturnsContentOnSuccess()
    {
        var handler = new FakeHandler((_, _) =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("INDEX-OK"),
            };
        });
        using var http = new HttpClient(handler);
        var sut = new NuGetFeedHttpClient(http);

        await using var stream = await sut.ReadServiceIndexAsync("https://feed/index.json", credential: null, CancellationToken.None);
        using var reader = new StreamReader(stream);
        var body = await reader.ReadToEndAsync();
        await Assert.That(body).IsEqualTo("INDEX-OK");
    }

    /// <summary>ReadServiceIndexAsync throws on non-success status.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadServiceIndexAsyncThrowsOnError()
    {
        var handler = new FakeHandler((_, _) => new(HttpStatusCode.InternalServerError));
        using var http = new HttpClient(handler);
        var sut = new NuGetFeedHttpClient(http);

        Task Act() => sut.ReadServiceIndexAsync("https://feed/index.json", credential: null, CancellationToken.None);

        await Assert.That(Act).Throws<HttpRequestException>();
    }

    /// <summary>ReadServiceIndexAsync forwards Basic-auth credentials.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReadServiceIndexAsyncSendsBasicAuth()
    {
        AuthenticationHeaderValue? sent = null;
        var handler = new FakeHandler((req, _) =>
        {
            sent = req.Headers.Authorization;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(string.Empty),
            };
        });
        using var http = new HttpClient(handler);
        var sut = new NuGetFeedHttpClient(http);
        var cred = new PackageSourceCredential("nuget.org", "user", "pw", null);

        await using var stream = await sut.ReadServiceIndexAsync("https://feed/index.json", cred, CancellationToken.None);

        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("user:pw"));
        await Assert.That(sent?.Scheme).IsEqualTo("Basic");
        await Assert.That(sent?.Parameter).IsEqualTo(expected);
    }

    /// <summary>TryDownloadNupkgAsync returns the body stream on success.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryDownloadNupkgAsyncReturnsStreamOnSuccess()
    {
        var handler = new FakeHandler((_, _) => new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1, 2, 3]),
        });
        using var http = new HttpClient(handler);
        var sut = new NuGetFeedHttpClient(http);

        await using var stream = await sut.TryDownloadNupkgAsync("https://feed/x.nupkg", credential: null, CancellationToken.None);
        await Assert.That(stream).IsNotNull();

        await using var ms = new MemoryStream();
        await stream!.CopyToAsync(ms);
        await Assert.That(ms.ToArray()).IsEquivalentTo(new byte[] { 1, 2, 3 });
    }

    /// <summary>TryDownloadNupkgAsync returns null on 404.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryDownloadNupkgAsyncReturnsNullOn404()
    {
        var handler = new FakeHandler((_, _) => new(HttpStatusCode.NotFound));
        using var http = new HttpClient(handler);
        var sut = new NuGetFeedHttpClient(http);

        var stream = await sut.TryDownloadNupkgAsync("https://feed/x.nupkg", credential: null, CancellationToken.None);

        await Assert.That(stream).IsNull();
    }

    /// <summary>TryDownloadNupkgAsync throws on non-404 error.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryDownloadNupkgAsyncThrowsOnError()
    {
        var handler = new FakeHandler((_, _) => new(HttpStatusCode.Unauthorized));
        using var http = new HttpClient(handler);
        var sut = new NuGetFeedHttpClient(http);

        async Task Act() => await sut.TryDownloadNupkgAsync("https://feed/x.nupkg", credential: null, CancellationToken.None);

        await Assert.That(Act).Throws<HttpRequestException>();
    }

    /// <summary>TryDownloadNupkgAsync forwards Basic-auth credentials.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryDownloadNupkgAsyncSendsBasicAuth()
    {
        AuthenticationHeaderValue? sent = null;
        var handler = new FakeHandler((req, _) =>
        {
            sent = req.Headers.Authorization;
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
        });
        using var http = new HttpClient(handler);
        var sut = new NuGetFeedHttpClient(http);
        var cred = new PackageSourceCredential("nuget.org", "u", "p", null);

        await using var stream = await sut.TryDownloadNupkgAsync("https://feed/x.nupkg", cred, CancellationToken.None);

        await Assert.That(sent?.Scheme).IsEqualTo("Basic");
    }

    /// <summary>Dispose with ownsHttp=true disposes the underlying HttpClient.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DisposeWithOwnsHttpDisposesHttpClient()
    {
        var handler = new TrackingHandler();
        var http = new HttpClient(handler);
        var sut = new NuGetFeedHttpClient(http, ownsHttp: true);

        sut.Dispose();

        await Assert.That(handler.Disposed).IsTrue();
    }

    /// <summary>Dispose with ownsHttp=false leaves the HttpClient alive.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DisposeWithoutOwnsHttpLeavesHttpClient()
    {
        var handler = new TrackingHandler();
        using var http = new HttpClient(handler);
        var sut = new NuGetFeedHttpClient(http, ownsHttp: false);

        sut.Dispose();

        await Assert.That(handler.Disposed).IsFalse();
    }

    /// <summary>ApplyCredentials no-ops when credential is null.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ApplyCredentialsNoOpForNull()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example/");

        NuGetFeedHttpClient.ApplyCredentials(request, credential: null);

        await Assert.That(request.Headers.Authorization).IsNull();
    }

    /// <summary>ApplyCredentials sets the Authorization header from the credential.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ApplyCredentialsSetsHeader()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example/");

        NuGetFeedHttpClient.ApplyCredentials(request, new PackageSourceCredential("nuget.org", "alice", "secret", null));

        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("alice:secret"));
        await Assert.That(request.Headers.Authorization?.Scheme).IsEqualTo("Basic");
        await Assert.That(request.Headers.Authorization?.Parameter).IsEqualTo(expected);
    }

    /// <summary>Test helper — minimal HttpMessageHandler driven by a delegate.</summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        /// <summary>Per-request response synthesizer.</summary>
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _responder;

        /// <summary>Initializes a new instance of the <see cref="FakeHandler"/> class.</summary>
        /// <param name="responder">Function that synthesizes a response per request.</param>
        public FakeHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder) => _responder = responder;

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_responder(request, cancellationToken));
    }

    /// <summary>Test helper — handler that records when its parent HttpClient is disposed.</summary>
    private sealed class TrackingHandler : HttpMessageHandler
    {
        /// <summary>Gets a value indicating whether the handler has been disposed.</summary>
        public bool Disposed { get; private set; }

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
