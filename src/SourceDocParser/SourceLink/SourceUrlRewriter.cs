// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.SourceLink;

/// <summary>
/// Rewrites raw SourceLink URLs into human-friendly blob URLs.
/// </summary>
internal static class SourceUrlRewriter
{
    /// <summary>
    /// Rewrites a raw SourceLink URL into a blob URL with a line anchor.
    /// </summary>
    /// <param name="rawUrl">Raw URL from SourceLinkMap.</param>
    /// <param name="line">First executable source line for the symbol.</param>
    /// <returns>A human-friendly URL.</returns>
    public static string ToBlobUrl(string rawUrl, int line)
    {
        if (rawUrl.StartsWith("https://raw.githubusercontent.com/", StringComparison.OrdinalIgnoreCase))
        {
            var afterHost = rawUrl.AsSpan("https://raw.githubusercontent.com/".Length);
            var slashAfterRepo = SkipPathSegments(afterHost, 3);
            if (slashAfterRepo > 0)
            {
                var ownerRepoSha = afterHost[..slashAfterRepo];
                var firstSlash = ownerRepoSha.IndexOf('/');
                var lastSlash = ownerRepoSha.LastIndexOf('/');

                if (firstSlash > 0 && lastSlash > firstSlash)
                {
                    var owner = ownerRepoSha[..firstSlash];
                    var repo = ownerRepoSha[(firstSlash + 1)..lastSlash];
                    var sha = ownerRepoSha[(lastSlash + 1)..];
                    var path = afterHost[(slashAfterRepo + 1)..];

                    return AppendAnchor($"https://github.com/{owner}/{repo}/blob/{sha}/{path}", line, "#L");
                }
            }
        }

        if (rawUrl.Contains("/-/raw/", StringComparison.OrdinalIgnoreCase))
        {
            var rewritten = rawUrl.Replace("/-/raw/", "/-/blob/", StringComparison.OrdinalIgnoreCase);
            return AppendAnchor(rewritten, line, "#L");
        }

        if (rawUrl.StartsWith("https://api.bitbucket.org/2.0/repositories/", StringComparison.OrdinalIgnoreCase))
        {
            var afterHost = rawUrl.AsSpan("https://api.bitbucket.org/2.0/repositories/".Length);
            return AppendAnchor($"https://bitbucket.org/{afterHost}", line, "#lines-");
        }

        if (rawUrl.StartsWith("https://dev.azure.com/", StringComparison.OrdinalIgnoreCase)
            && TryRewriteAzureDevOps(rawUrl, line) is { } azureBlob)
        {
            return azureBlob;
        }

        return AppendAnchor(rawUrl, line, "#L");
    }

    /// <summary>
    /// Strips the line anchor from a blob URL.
    /// </summary>
    /// <param name="blobUrl">Blob URL with optional line anchor.</param>
    /// <returns>The URL without the anchor.</returns>
    public static string StripAnchor(string blobUrl)
    {
        var hashIdx = blobUrl.IndexOf('#', StringComparison.Ordinal);
        return hashIdx < 0 ? blobUrl : blobUrl[..hashIdx];
    }

    /// <summary>
    /// Appends a line anchor to a URL.
    /// </summary>
    /// <param name="url">URL to append to.</param>
    /// <param name="line">Source line number.</param>
    /// <param name="anchorPrefix">Provider-specific anchor prefix.</param>
    /// <returns>The URL with the anchor appended if applicable.</returns>
    private static string AppendAnchor(string url, int line, string anchorPrefix) =>
        line > 0 ? $"{url}{anchorPrefix}{line}" : url;

    /// <summary>
    /// Skips a specific number of path segments in a span.
    /// </summary>
    /// <param name="span">The path span.</param>
    /// <param name="segments">Number of segments to skip.</param>
    /// <returns>The index of the slash after the segments, or -1.</returns>
    private static int SkipPathSegments(in ReadOnlySpan<char> span, int segments)
    {
        var index = -1;
        for (var i = 0; i < segments; i++)
        {
            var next = span[(index + 1)..].IndexOf('/');
            if (next < 0)
            {
                return -1;
            }

            index = index + 1 + next;
        }

        return index;
    }

    /// <summary>
    /// Rewrites Azure DevOps API URLs into blob URLs.
    /// </summary>
    /// <param name="rawUrl">Azure DevOps API URL.</param>
    /// <param name="line">Source line number.</param>
    /// <returns>The rewritten URL, or null.</returns>
    private static string? TryRewriteAzureDevOps(string rawUrl, int line)
    {
        var apisIdx = rawUrl.IndexOf("/_apis/git/repositories/", StringComparison.OrdinalIgnoreCase);
        if (apisIdx < 0)
        {
            return null;
        }

        var orgProject = rawUrl[..apisIdx];
        var afterRepos = rawUrl.AsSpan(apisIdx + "/_apis/git/repositories/".Length);
        var slashAfterRepo = afterRepos.IndexOf('/');
        if (slashAfterRepo <= 0)
        {
            return null;
        }

        var repo = afterRepos[..slashAfterRepo];
        var queryStart = rawUrl.IndexOf('?', apisIdx);
        if (queryStart < 0)
        {
            return null;
        }

        var query = rawUrl.AsSpan(queryStart + 1);
        var path = ExtractQueryParameter(query, "path");
        var version = ExtractQueryParameter(query, "version");
        if (path is null || version is null)
        {
            return null;
        }

        var built = $"{orgProject}/_git/{repo}?path={path}&version=GC{version}";
        return line > 0 ? $"{built}&line={line}" : built;
    }

    /// <summary>
    /// Extracts a named parameter value from a query string.
    /// </summary>
    /// <param name="query">Query string span.</param>
    /// <param name="name">Parameter name.</param>
    /// <returns>The parameter value, or null.</returns>
    private static string? ExtractQueryParameter(ReadOnlySpan<char> query, string name)
    {
        while (query.Length > 0)
        {
            var ampIdx = query.IndexOf('&');
            var pair = ampIdx < 0 ? query : query[..ampIdx];
            var eqIdx = pair.IndexOf('=');
            if (eqIdx > 0 && pair[..eqIdx].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return pair[(eqIdx + 1)..].ToString();
            }

            if (ampIdx < 0)
            {
                break;
            }

            query = query[(ampIdx + 1)..];
        }

        return null;
    }
}
