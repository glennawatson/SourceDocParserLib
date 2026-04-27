// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.SourceLink;

/// <summary>
/// Rewrites raw SourceLink URLs into human-friendly blob URLs by
/// dispatching through <see cref="BlobUrlProviders"/> in priority
/// order (GitHub → GitLab → Bitbucket → Azure DevOps → fallback).
/// Each provider recognises a single host's raw shape; the first
/// match wins. Unknown hosts get the default <c>#L{line}</c> anchor.
/// </summary>
internal static class SourceUrlRewriter
{
    /// <summary>The anchor separator character.</summary>
    private const char AnchorSeparator = '#';

    /// <summary>
    /// Rewrites a raw SourceLink URL into a blob URL with a line anchor.
    /// </summary>
    /// <param name="rawUrl">Raw URL from SourceLinkMap.</param>
    /// <param name="line">First executable source line for the symbol.</param>
    /// <returns>A human-friendly URL.</returns>
    public static string ToBlobUrl(string rawUrl, int line) =>
        BlobUrlProviders.TryRewriteGitHub(rawUrl, line)
        ?? BlobUrlProviders.TryRewriteGitLab(rawUrl, line)
        ?? BlobUrlProviders.TryRewriteBitbucket(rawUrl, line)
        ?? BlobUrlProviders.TryRewriteAzureDevOps(rawUrl, line)
        ?? BlobUrlProviders.AppendDefaultAnchor(rawUrl, line);

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
}
