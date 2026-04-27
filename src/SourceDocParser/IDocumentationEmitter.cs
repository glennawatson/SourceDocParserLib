// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;

namespace SourceDocParser;

/// <summary>
/// Renders the merged <see cref="ApiCatalog"/> into documentation
/// pages. Each implementation targets one output format.
/// </summary>
public interface IDocumentationEmitter
{
    /// <summary>
    /// Writes pages for every type in <paramref name="types"/> into
    /// <paramref name="outputRoot"/>.
    /// </summary>
    /// <param name="types">Merged canonical types.</param>
    /// <param name="outputRoot">Destination directory, already cleaned.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Total pages written.</returns>
    Task<int> EmitAsync(ApiType[] types, string outputRoot, CancellationToken cancellationToken = default);
}
