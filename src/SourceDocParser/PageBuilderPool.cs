// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text;

namespace SourceDocParser;

/// <summary>
/// Thread-static cache of <see cref="StringBuilder"/> instances reused
/// across page composition calls so each emit thread allocates a
/// builder once and clears it between pages.
/// </summary>
internal static class PageBuilderPool
{
    /// <summary>Cap above which a returned builder is dropped instead of cached so an oversized page doesn't pin a large array.</summary>
    private const int MaxCachedCapacity = 64 * 1024;

    /// <summary>Per-thread parked builder ready for the next <see cref="Rent"/>.</summary>
    [ThreadStatic]
    private static StringBuilder? _scratch;

    /// <summary>
    /// Returns a cleared builder wrapped in an <see cref="IDisposable"/>
    /// rental; the builder lives until the caller's <c>using</c> scope
    /// exits, at which point the rental returns it to the cache.
    /// </summary>
    /// <param name="hintCapacity">Initial capacity hint for the page body.</param>
    /// <returns>A rental scope holding the builder.</returns>
    public static PageBuilderRental Rent(int hintCapacity)
    {
        var sb = _scratch;
        _scratch = null;
        if (sb is null)
        {
            sb = new StringBuilder(hintCapacity);
        }
        else
        {
            sb.Clear();
            if (sb.Capacity < hintCapacity)
            {
                sb.EnsureCapacity(hintCapacity);
            }
        }

        return new PageBuilderRental(sb);
    }

    /// <summary>Returns <paramref name="sb"/> to the cache when nothing else is parked there.</summary>
    /// <param name="sb">Builder to return.</param>
    internal static void Return(StringBuilder sb)
    {
        if (sb.Capacity > MaxCachedCapacity)
        {
            return;
        }

        _scratch ??= sb;
    }
}
