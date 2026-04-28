// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Model;
using SourceDocParser.Zensical.Options;

namespace SourceDocParser.Zensical.Routing;

/// <summary>
/// Thin adapter that turns an <see cref="ApiTypeReference"/> into a
/// Markdown fragment by delegating to a cref <see cref="ICrefResolver"/>.
/// Centralising the dispatch here keeps the cross-link routing rules --
/// "is this UID in our emitted set / a BCL type / unknown?" -- in
/// exactly one place: <see cref="ZensicalCrefResolver"/>. Both type
/// references in member signatures and <c>see cref</c> tags in
/// XML doc comments resolve through the same resolver instance.
/// </summary>
internal static class CrossLinkRouter
{
    /// <summary>
    /// Renders <paramref name="reference"/> as Markdown via the
    /// resolver attached to <paramref name="options"/>. Empty UIDs
    /// fall back to inline code so display-only references -- generic
    /// constraints, receivers without a bound symbol -- never produce a
    /// broken link.
    /// </summary>
    /// <param name="reference">Type reference to render.</param>
    /// <param name="options">Emitter options whose <see cref="ZensicalEmitterOptions.Resolver"/> drives the dispatch.</param>
    /// <returns>The Markdown fragment for the reference.</returns>
    public static string Format(ApiTypeReference reference, ZensicalEmitterOptions options)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(options);

        return Format(reference, options.Resolver);
    }

    /// <summary>
    /// Renders <paramref name="reference"/> as Markdown via the
    /// supplied <paramref name="resolver"/>. Empty UIDs fall back to
    /// inline code.
    /// </summary>
    /// <param name="reference">Type reference to render.</param>
    /// <param name="resolver">Cref resolver supplied by the emitter.</param>
    /// <returns>The Markdown fragment for the reference.</returns>
    public static string Format(ApiTypeReference reference, ICrefResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(resolver);

        return resolver.Render(reference.Uid ?? string.Empty, reference.DisplayName.AsSpan());
    }
}
