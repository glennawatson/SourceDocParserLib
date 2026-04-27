// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.NuGet.Infrastructure;

/// <summary>
/// Normalises mixed slash input for archive-style and platform-native paths.
/// </summary>
internal static class PathSeparatorHelpers
{
    /// <summary>
    /// Normalises a relative path to the current platform separator.
    /// </summary>
    /// <param name="value">Relative path that may contain either slash form.</param>
    /// <returns>The normalised path.</returns>
    public static string ToPlatformPath(string value) =>
        NormalizeSeparators(value, Path.DirectorySeparatorChar);

    /// <summary>
    /// Normalises an archive-style path prefix to forward slashes and ensures it ends with a slash.
    /// </summary>
    /// <param name="value">Archive path prefix that may contain either slash form.</param>
    /// <returns>The slash-normalised prefix ending with <c>/</c>.</returns>
    public static string EnsureTrailingForwardSlash(string value)
    {
        var normalized = NormalizeSeparators(value, '/');
        return normalized.EndsWith('/') ? normalized : normalized + "/";
    }

    /// <summary>
    /// Rewrites both slash forms to the requested separator.
    /// </summary>
    /// <param name="value">Path text that may contain either slash form.</param>
    /// <param name="separator">Separator to emit.</param>
    /// <returns>The normalised path.</returns>
    private static string NormalizeSeparators(string value, char separator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var firstSeparator = value.AsSpan().IndexOfAny(['/', '\\']);
        if (firstSeparator < 0)
        {
            return value;
        }

        var requiresRewrite = false;
        for (var i = firstSeparator; i < value.Length; i++)
        {
            if (value[i] is '/' or '\\')
            {
                requiresRewrite |= value[i] != separator;
            }
        }

        return !requiresRewrite
            ? value
            : string.Create(
                value.Length,
                (Value: value, FirstSeparator: firstSeparator, Separator: separator),
                static (dest, state) =>
                {
                    state.Value.AsSpan(0, state.FirstSeparator).CopyTo(dest);
                    for (var i = state.FirstSeparator; i < state.Value.Length; i++)
                    {
                        dest[i] = state.Value[i] is '/' or '\\'
                            ? state.Separator
                            : state.Value[i];
                    }
                });
    }
}
