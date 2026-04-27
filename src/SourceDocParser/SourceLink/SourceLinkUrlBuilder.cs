// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.SourceLink;

/// <summary>
/// Shared builders for SourceLink-derived URLs.
/// </summary>
internal static class SourceLinkUrlBuilder
{
    /// <summary>
    /// Appends the wildcard suffix to a URL prefix while normalising path separators.
    /// </summary>
    /// <param name="urlPrefix">Remote URL prefix.</param>
    /// <param name="localPath">Matched local path.</param>
    /// <param name="suffixStart">Start index of the wildcard suffix.</param>
    /// <returns>The combined remote URL.</returns>
    public static string BuildWildcardUrl(string urlPrefix, string localPath, int suffixStart)
    {
        var suffixLength = localPath.Length - suffixStart;
        if (suffixLength is 0)
        {
            return urlPrefix;
        }

        return string.Create(
            urlPrefix.Length + suffixLength,
            (UrlPrefix: urlPrefix, LocalPath: localPath, SuffixStart: suffixStart),
            static (dest, state) =>
            {
                state.UrlPrefix.CopyTo(dest);
                var destIndex = state.UrlPrefix.Length;
                for (var i = state.SuffixStart; i < state.LocalPath.Length; i++)
                {
                    var current = state.LocalPath[i];
                    dest[destIndex++] = current is '/' or '\\'
                        ? '/'
                        : current;
                }
            });
    }
}
