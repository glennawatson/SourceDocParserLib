// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.SourceLink;

/// <summary>
/// Per-host SourceLink raw-URL → blob-URL rewriters. Each provider
/// recognises the raw form a single Git host publishes and returns
/// the corresponding human-friendly blob URL with a line anchor (or
/// <see langword="null"/> when the raw URL doesn't match its shape so
/// the dispatcher can move on to the next provider). Lifted out of
/// <see cref="SourceUrlRewriter"/> so each host's rewrite shape is
/// isolated and testable.
/// </summary>
internal static class BlobUrlProviders
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

    /// <summary>The query string separator character.</summary>
    private const char QuerySeparator = '?';

    /// <summary>The path segment separator character.</summary>
    private const char PathSeparator = '/';

    /// <summary>The query parameter pair separator character.</summary>
    private const char PairSeparator = '&';

    /// <summary>The query parameter value separator character.</summary>
    private const char ValueSeparator = '=';

    /// <summary>
    /// Attempts to rewrite a GitHub raw URL into a github.com blob URL.
    /// Returns <see langword="null"/> when the URL doesn't match the
    /// raw.githubusercontent.com shape.
    /// </summary>
    /// <param name="rawUrl">Raw URL from SourceLinkMap.</param>
    /// <param name="line">First executable source line for the symbol; 0 omits the anchor.</param>
    /// <returns>The blob URL, or <see langword="null"/> when the URL doesn't match this provider.</returns>
    public static string? TryRewriteGitHub(string rawUrl, int line)
    {
        var urlSpan = rawUrl.AsSpan();
        if (!urlSpan.StartsWith(GitHubRawUrlPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var afterHost = urlSpan[GitHubRawUrlPrefix.Length..];
        var slashAfterRepo = SkipPathSegments(afterHost, 3);
        if (slashAfterRepo <= 0)
        {
            return null;
        }

        var ownerRepoSha = afterHost[..slashAfterRepo];
        var firstSlash = ownerRepoSha.IndexOf('/');
        var lastSlash = ownerRepoSha.LastIndexOf('/');
        if (firstSlash <= 0 || lastSlash <= firstSlash)
        {
            return null;
        }

        var owner = ownerRepoSha[..firstSlash];
        var repo = ownerRepoSha[(firstSlash + 1)..lastSlash];
        var sha = ownerRepoSha[(lastSlash + 1)..];
        var path = afterHost[(slashAfterRepo + 1)..];

        return line > 0
            ? $"{GitHubBlobUrlPrefix}{owner}/{repo}{GitHubBlobSegment}{sha}/{path}{LineAnchorPrefix}{line}"
            : $"{GitHubBlobUrlPrefix}{owner}/{repo}{GitHubBlobSegment}{sha}/{path}";
    }

    /// <summary>
    /// Attempts to rewrite a GitLab <c>/-/raw/</c> URL into the
    /// matching <c>/-/blob/</c> URL. Returns <see langword="null"/>
    /// when the raw segment is absent.
    /// </summary>
    /// <param name="rawUrl">Raw URL.</param>
    /// <param name="line">Source line; 0 omits the anchor.</param>
    /// <returns>The blob URL, or <see langword="null"/> when the URL doesn't match this provider.</returns>
    public static string? TryRewriteGitLab(string rawUrl, int line)
    {
        if (!rawUrl.Contains(GitLabRawSegment, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var rewritten = rawUrl.Replace(GitLabRawSegment, GitLabBlobSegment, StringComparison.OrdinalIgnoreCase);
        return line > 0 ? $"{rewritten}{LineAnchorPrefix}{line}" : rewritten;
    }

    /// <summary>
    /// Attempts to rewrite a Bitbucket API URL into a bitbucket.org
    /// blob URL with the special <c>#lines-</c> anchor. Returns
    /// <see langword="null"/> when the URL isn't a Bitbucket API URL.
    /// </summary>
    /// <param name="rawUrl">Raw URL.</param>
    /// <param name="line">Source line; 0 omits the anchor.</param>
    /// <returns>The blob URL, or <see langword="null"/> when the URL doesn't match this provider.</returns>
    public static string? TryRewriteBitbucket(string rawUrl, int line)
    {
        var urlSpan = rawUrl.AsSpan();
        if (!urlSpan.StartsWith(BitbucketApiUrlPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var afterHost = urlSpan[BitbucketApiUrlPrefix.Length..];
        return line > 0
            ? $"{BitbucketBlobUrlPrefix}{afterHost}{BitbucketLinesAnchorPrefix}{line}"
            : $"{BitbucketBlobUrlPrefix}{afterHost}";
    }

    /// <summary>
    /// Attempts to rewrite an Azure DevOps API URL into a <c>/_git/</c>
    /// URL with <c>path</c> + <c>version=GC{sha}</c> + <c>line=</c>
    /// query. Returns <see langword="null"/> when the URL isn't an
    /// Azure DevOps API URL or when the required query parameters are
    /// missing.
    /// </summary>
    /// <param name="rawUrl">Raw URL.</param>
    /// <param name="line">Source line; 0 omits the line query.</param>
    /// <returns>The blob URL, or <see langword="null"/> when the URL doesn't match this provider.</returns>
    public static string? TryRewriteAzureDevOps(string rawUrl, int line)
    {
        if (!rawUrl.AsSpan().StartsWith(AzureDevOpsUrlPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

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
        var path = QueryParameterParser.Extract(query, AzureDevOpsPathQueryParam);
        var version = QueryParameterParser.Extract(query, AzureDevOpsVersionQueryParam);
        if (path is null || version is null)
        {
            return null;
        }

        var built = $"{orgProject}{AzureDevOpsGitSegment}{repo}" +
                    $"{QuerySeparator}{AzureDevOpsPathQueryParam}{ValueSeparator}{path}" +
                    $"{PairSeparator}{AzureDevOpsVersionQueryParam}{ValueSeparator}GC{version}";
        return line > 0 ? $"{built}{PairSeparator}line{ValueSeparator}{line}" : built;
    }

    /// <summary>
    /// Returns <paramref name="rawUrl"/> with a default <c>#L{line}</c>
    /// anchor when <paramref name="line"/> is positive; otherwise
    /// returns the URL unchanged. The catch-all fallback when no
    /// host-specific provider matches.
    /// </summary>
    /// <param name="rawUrl">URL to anchor.</param>
    /// <param name="line">Source line; 0 returns the URL unchanged.</param>
    /// <returns>The anchored URL.</returns>
    public static string AppendDefaultAnchor(string rawUrl, int line) =>
        line > 0 ? $"{rawUrl}{LineAnchorPrefix}{line}" : rawUrl;

    /// <summary>Skips a specific number of path segments in a span.</summary>
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
}
