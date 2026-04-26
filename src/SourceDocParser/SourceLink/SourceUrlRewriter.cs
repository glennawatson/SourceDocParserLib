// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.SourceLink;

/// <summary>
/// Rewrites raw SourceLink URLs into human-friendly blob URLs.
/// </summary>
internal static class SourceUrlRewriter
{
    /// <summary>The URL prefix for raw GitHub content.</summary>
    private const string GitHubRawUrlPrefix = "https://raw.githubusercontent.com/";

    /// <summary>The URL prefix for Bitbucket API repositories.</summary>
    private const string BitbucketApiUrlPrefix = "https://api.bitbucket.org/2.0/repositories/";

    /// <summary>The URL prefix for Azure DevOps.</summary>
    private const string AzureDevOpsUrlPrefix = "https://dev.azure.com/";

    /// <summary>The raw URL segment for GitLab.</summary>
    private const string GitLabRawSegment = "/-/raw/";

    /// <summary>The blob URL segment for GitLab.</summary>
    private const string GitLabBlobSegment = "/-/blob/";

    /// <summary>The API URL segment for Azure DevOps Git repositories.</summary>
    private const string AzureDevOpsGitApiSegment = "/_apis/git/repositories/";

    /// <summary>The URL prefix for GitHub blob URLs.</summary>
    private const string GitHubBlobUrlPrefix = "https://github.com/";

    /// <summary>The URL prefix for Bitbucket blob URLs.</summary>
    private const string BitbucketBlobUrlPrefix = "https://bitbucket.org/";

    /// <summary>The URL segment for Azure DevOps Git projects.</summary>
    private const string AzureDevOpsGitSegment = "/_git/";

    /// <summary>The path query parameter name for Azure DevOps.</summary>
    private const string AzureDevOpsPathQueryParam = "path";

    /// <summary>The version query parameter name for Azure DevOps.</summary>
    private const string AzureDevOpsVersionQueryParam = "version";

    /// <summary>The blob URL segment for GitHub.</summary>
    private const string GitHubBlobSegment = "/blob/";

    /// <summary>The line anchor prefix for Bitbucket.</summary>
    private const string BitbucketLinesAnchorPrefix = "#lines-";

    /// <summary>The default line anchor prefix.</summary>
    private const string LineAnchorPrefix = "#L";

    /// <summary>The anchor separator character.</summary>
    private const char AnchorSeparator = '#';

    /// <summary>The query string separator character.</summary>
    private const char QuerySeparator = '?';

    /// <summary>The path segment separator character.</summary>
    private const char PathSeparator = '/';

    /// <summary>The query parameter pair separator character.</summary>
    private const char PairSeparator = '&';

    /// <summary>The query parameter value separator character.</summary>
    private const char ValueSeparator = '=';

    /// <summary>
    /// Rewrites a raw SourceLink URL into a blob URL with a line anchor.
    /// </summary>
    /// <param name="rawUrl">Raw URL from SourceLinkMap.</param>
    /// <param name="line">First executable source line for the symbol.</param>
    /// <returns>A human-friendly URL.</returns>
    public static string ToBlobUrl(string rawUrl, int line)
    {
        var urlSpan = rawUrl.AsSpan();

        if (urlSpan.StartsWith(GitHubRawUrlPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var afterHost = urlSpan[GitHubRawUrlPrefix.Length..];
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

                    // Fuse base URL + anchor into a single interpolation
                    // so the interpolated-string handler appends the
                    // ReadOnlySpan parts directly to its internal buffer
                    // — one string allocation total instead of two.
                    return line > 0
                        ? $"{GitHubBlobUrlPrefix}{owner}/{repo}{GitHubBlobSegment}{sha}/{path}{LineAnchorPrefix}{line}"
                        : $"{GitHubBlobUrlPrefix}{owner}/{repo}{GitHubBlobSegment}{sha}/{path}";
                }
            }
        }

        if (rawUrl.Contains(GitLabRawSegment, StringComparison.OrdinalIgnoreCase))
        {
            var rewritten = rawUrl.Replace(GitLabRawSegment, GitLabBlobSegment, StringComparison.OrdinalIgnoreCase);
            return line > 0 ? $"{rewritten}{LineAnchorPrefix}{line}" : rewritten;
        }

        if (urlSpan.StartsWith(BitbucketApiUrlPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var afterHost = urlSpan[BitbucketApiUrlPrefix.Length..];
            return line > 0
                ? $"{BitbucketBlobUrlPrefix}{afterHost}{BitbucketLinesAnchorPrefix}{line}"
                : $"{BitbucketBlobUrlPrefix}{afterHost}";
        }

        if (urlSpan.StartsWith(AzureDevOpsUrlPrefix, StringComparison.OrdinalIgnoreCase)
            && TryRewriteAzureDevOps(rawUrl, line) is { } azureBlob)
        {
            return azureBlob;
        }

        return line > 0 ? $"{rawUrl}{LineAnchorPrefix}{line}" : rawUrl;
    }

    /// <summary>
    /// Strips the line anchor from a blob URL.
    /// </summary>
    /// <param name="blobUrl">Blob URL with optional line anchor.</param>
    /// <returns>The URL without the anchor.</returns>
    public static string StripAnchor(string blobUrl)
    {
        var hashIdx = blobUrl.IndexOf(AnchorSeparator, StringComparison.Ordinal);
        return hashIdx < 0 ? blobUrl : blobUrl[..hashIdx];
    }

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
            var next = span[(index + 1)..].IndexOf(PathSeparator);
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
        var apisIdx = rawUrl.IndexOf(AzureDevOpsGitApiSegment, StringComparison.OrdinalIgnoreCase);
        if (apisIdx < 0)
        {
            return null;
        }

        var orgProject = rawUrl.AsSpan(0, apisIdx);
        var afterRepos = rawUrl.AsSpan(apisIdx + AzureDevOpsGitApiSegment.Length);
        var slashAfterRepo = afterRepos.IndexOf('/');
        if (slashAfterRepo <= 0)
        {
            return null;
        }

        var repo = afterRepos[..slashAfterRepo];
        var queryStart = rawUrl.IndexOf(QuerySeparator, apisIdx);
        if (queryStart < 0)
        {
            return null;
        }

        var query = rawUrl.AsSpan(queryStart + 1);
        var path = ExtractQueryParameter(query, AzureDevOpsPathQueryParam);
        var version = ExtractQueryParameter(query, AzureDevOpsVersionQueryParam);
        if (path is null || version is null)
        {
            return null;
        }

        var built = $"{orgProject}{AzureDevOpsGitSegment}{repo}{QuerySeparator}{AzureDevOpsPathQueryParam}{ValueSeparator}{path}{PairSeparator}{AzureDevOpsVersionQueryParam}{ValueSeparator}GC{version}";
        return line > 0 ? $"{built}{PairSeparator}line{ValueSeparator}{line}" : built;
    }

    /// <summary>
    /// Extracts a named parameter value from a query string.
    /// </summary>
    /// <param name="query">Query string span.</param>
    /// <param name="name">Parameter name.</param>
    /// <returns>The parameter value, or null.</returns>
    private static string? ExtractQueryParameter(in ReadOnlySpan<char> query, string name)
    {
        var current = query;
        while (current.Length > 0)
        {
            var ampIdx = current.IndexOf(PairSeparator);
            var pair = ampIdx < 0 ? current : current[..ampIdx];
            var eqIdx = pair.IndexOf(ValueSeparator);
            if (eqIdx > 0 && pair[..eqIdx].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return pair[(eqIdx + 1)..].ToString();
            }

            if (ampIdx < 0)
            {
                break;
            }

            current = current[(ampIdx + 1)..];
        }

        return null;
    }
}
