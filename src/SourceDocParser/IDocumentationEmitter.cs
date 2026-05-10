// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;

namespace SourceDocParser;

/// <summary>
/// Renders the merged <see cref="ApiCatalog"/> into documentation pages. Each implementation
/// targets one output format. Pages are streamed through an <see cref="IPageSink"/> so the
/// emitter never has to know whether the destination is a directory, an in-memory queue, or
/// some other consumer.
/// </summary>
public interface IDocumentationEmitter
{
    /// <summary>Streams pages for every type in <paramref name="types"/> through <paramref name="sink"/>.</summary>
    /// <param name="types">Merged canonical types.</param>
    /// <param name="sink">Destination sink.</param>
    /// <returns>Total pages emitted.</returns>
    Task<int> EmitAsync(ApiType[] types, IPageSink sink);

    /// <summary>Streams pages for every type in <paramref name="types"/> through <paramref name="sink"/>.</summary>
    /// <param name="types">Merged canonical types.</param>
    /// <param name="sink">Destination sink.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Total pages emitted.</returns>
    Task<int> EmitAsync(ApiType[] types, IPageSink sink, CancellationToken cancellationToken);
}
