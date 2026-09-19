// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;

namespace SourceDocParser;

/// <summary>Renders API documentation with the package versions used to resolve each root.</summary>
public interface IPackageDocumentationEmitter : IDocumentationEmitter
{
    /// <summary>Emits API pages with package and dependency version metadata.</summary>
    /// <param name="types">Merged documented types.</param>
    /// <param name="packages">Independent package graphs for the documented roots.</param>
    /// <param name="sink">Page destination.</param>
    /// <param name="cancellationToken">Cancellation for emission.</param>
    /// <returns>The number of emitted pages.</returns>
    Task<int> EmitAsync(ApiType[] types, ApiPackageGraph[] packages, IPageSink sink, CancellationToken cancellationToken);
}
