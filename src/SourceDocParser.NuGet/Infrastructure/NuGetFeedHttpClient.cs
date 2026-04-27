// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Net;
using System.Text;
using SourceDocParser.NuGet.Models;

namespace SourceDocParser.NuGet.Infrastructure;

/// <summary>
/// Default <see cref="INuGetFeedHttpClient"/> over a real
/// <see cref="HttpClient"/>. Owns Basic-auth header construction
/// and the 404-vs-throw distinction so the installer never touches
/// HTTP types directly. Tests substitute the fake handler at the
/// HttpClient seam to drive every status-code branch.
/// </summary>
internal sealed class NuGetFeedHttpClient : INuGetFeedHttpClient
{
    /// <summary>Underlying HTTP client used to send every request.</summary>
    private readonly HttpClient _http;

    /// <summary>True when this instance is responsible for disposing <see cref="_http"/>.</summary>
    private readonly bool _ownsHttp;

    /// <summary>Initializes a new instance of the <see cref="NuGetFeedHttpClient"/> class.</summary>
    /// <param name="http">Underlying HTTP client.</param>
    /// <param name="ownsHttp">When true, <see cref="Dispose"/> disposes <paramref name="http"/>.</param>
    public NuGetFeedHttpClient(HttpClient http, bool ownsHttp = false)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
        _ownsHttp = ownsHttp;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_ownsHttp)
        {
            return;
        }

        _http.Dispose();
    }

    /// <inheritdoc />
    public async Task<Stream> ReadServiceIndexAsync(string url, PackageSourceCredential? credential, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyCredentials(request, credential);

        var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        try
        {
            response.EnsureSuccessStatusCode();
            var inner = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return new OwningStream(inner, response);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Stream?> TryDownloadNupkgAsync(string url, PackageSourceCredential? credential, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyCredentials(request, credential);

        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        try
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                response.Dispose();
                return null;
            }

            response.EnsureSuccessStatusCode();
            var inner = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return new OwningStream(inner, response);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    /// <summary>Adds a Basic-auth header derived from <paramref name="credential"/> when present.</summary>
    /// <param name="request">Outgoing request.</param>
    /// <param name="credential">Optional credential to encode.</param>
    internal static void ApplyCredentials(HttpRequestMessage request, PackageSourceCredential? credential)
    {
        if (credential is not { } cred)
        {
            return;
        }

        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{cred.Username}:{cred.ClearTextPassword}"));
        request.Headers.Authorization = new("Basic", token);
    }
}
