// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text;

namespace SourceDocParser;

/// <summary>
/// Receives one rendered page at a time from a documentation emitter. Decouples the emit
/// loop from the destination so callers can choose between writing pages to disk
/// (<see cref="FilePageSink"/>) or piping them straight into another async pipeline
/// (<see cref="CallbackPageSink"/>) without the emitter needing to know the difference.
/// </summary>
public interface IPageSink
{
    /// <summary>Writes one page synchronously.</summary>
    /// <param name="relativePath">Forward-slashed relative path identifying the page (e.g. <c>api/Foo.Bar.md</c>).</param>
    /// <param name="builder">Page contents as a <see cref="StringBuilder"/>; the sink reads but does not retain it.</param>
    void WritePage(string relativePath, StringBuilder builder);

    /// <summary>Writes one page asynchronously.</summary>
    /// <param name="relativePath">Forward-slashed relative path identifying the page.</param>
    /// <param name="builder">Page contents.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the page has been accepted.</returns>
    Task WritePageAsync(string relativePath, StringBuilder builder, CancellationToken cancellationToken);
}
