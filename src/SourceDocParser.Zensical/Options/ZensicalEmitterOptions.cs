// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using SourceDocParser.Zensical.Routing;

namespace SourceDocParser.Zensical.Options;

/// <summary>
/// Represents configuration options for the Zensical Emitter, which is responsible
/// for generating documentation pages based on specific routing and structural rules.
/// </summary>
/// <param name="PackageRouting">Per-package folder-routing rules (assembly prefix to folder name).</param>
/// <param name="MicrosoftLearnBaseUrl">Base URL used to mint Microsoft Learn cross-links for un-walked BCL types.</param>
/// <param name="IncludeInSearch">
/// When <see langword="true"/> (the default), emitted pages participate in
/// Zensical's client-side search index. When <see langword="false"/>, the
/// frontmatter of every type/member page carries a
/// <c>search.exclude: true</c> block so the API tree is omitted from the
/// search index -- useful when API docs are sprawling and would otherwise
/// drown out the hand-written guides.
/// </param>
public sealed record ZensicalEmitterOptions(
    PackageRoutingRule[] PackageRouting,
    string MicrosoftLearnBaseUrl = ZensicalEmitterOptions.DefaultMicrosoftLearnBaseUrl,
    bool IncludeInSearch = true)
{
    /// <summary>Canonical Microsoft Learn .NET API root.</summary>
    [SuppressMessage("Critical Code Smell", "S2339:Public constant members should not be used", Justification = "Default value is not secret.")]
    public const string DefaultMicrosoftLearnBaseUrl = "https://learn.microsoft.com/dotnet/api/";

    /// <summary>Gets the legacy default: no per-package routing, no override URLs.</summary>
    public static ZensicalEmitterOptions Default { get; } = new([]);

    /// <summary>
    /// Gets the cref resolver used by the cross-link router. Defaults
    /// to <see cref="DefaultCrefResolver.Instance"/>; the documentation
    /// emitter swaps in a <see cref="ZensicalCrefResolver"/> bound to
    /// the actual emitted UID set at the start of each emit run, so
    /// references to walked types resolve as autoref links and
    /// references to un-walked content fall through cleanly.
    /// </summary>
    internal ICrefResolver Resolver { get; init; } = DefaultCrefResolver.Instance;
}
