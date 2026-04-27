// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.LibCompilation;
using SourceDocParser.Model;

namespace SourceDocParser;

/// <summary>
/// Pairs an <see cref="AssemblyGroup"/> with the per-TFM compilation
/// loader and an outstanding-work counter. <see cref="TryRetire"/> is
/// called by every parallel worker as it finishes an assembly; the
/// last caller (when the counter hits zero) disposes the loader so
/// the group's memory-mapped BCL ref pack views are released as soon
/// as that group has no more pending walks.
/// </summary>
internal sealed class TfmGroup
{
    /// <summary>Outstanding-walk counter; written via <see cref="Interlocked.Decrement(ref int)"/>.</summary>
    private int _remaining;

    /// <summary>Initializes a new instance of the <see cref="TfmGroup"/> class.</summary>
    /// <param name="group">Source-supplied assembly group.</param>
    /// <param name="loader">Per-TFM compilation loader.</param>
    /// <param name="totalWalks">Number of assemblies to process.</param>
    public TfmGroup(AssemblyGroup group, ICompilationLoader loader, int totalWalks)
    {
        Group = group;
        Loader = loader;
        _remaining = totalWalks;
    }

    /// <summary>Gets the source-supplied assembly group.</summary>
    public AssemblyGroup Group { get; }

    /// <summary>Gets the compilation loader scoped to this group.</summary>
    public ICompilationLoader Loader { get; }

    /// <summary>
    /// Decrements the outstanding-walk counter. When the counter hits
    /// zero this is the last walk in the group, so the loader is
    /// disposed immediately. Subsequent loader-dispose calls (e.g.
    /// from <see cref="LoaderRegistry"/>) are idempotent so the
    /// safety-net path stays correct.
    /// </summary>
    public void TryRetire()
    {
        if (Interlocked.Decrement(ref _remaining) != 0)
        {
            return;
        }

        Loader.Dispose();
    }
}