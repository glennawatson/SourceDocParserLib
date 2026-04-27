// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;

namespace SourceDocParser;

/// <summary>
/// Provides the assemblies the parser will walk, grouped by TFM. Each
/// implementation decides where the assemblies come from and how they
/// are bucketed; the parser is otherwise unaware of the source.
/// </summary>
public interface IAssemblySource
{
    /// <summary>
    /// Streams the discovered TFM groups using default cancellation settings.
    /// </summary>
    /// <returns>One <see cref="AssemblyGroup"/> per TFM.</returns>
    IAsyncEnumerable<AssemblyGroup> DiscoverAsync();

    /// <summary>
    /// Streams the discovered TFM groups. Async-streamed so the
    /// parser can start walking earlier groups while later ones are
    /// still being prepared.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One <see cref="AssemblyGroup"/> per TFM.</returns>
    IAsyncEnumerable<AssemblyGroup> DiscoverAsync(CancellationToken cancellationToken);
}
