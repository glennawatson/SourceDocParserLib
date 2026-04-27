// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.Zensical.Routing;

namespace SourceDocParser.Zensical.Options;

/// <summary>
/// Tunables for <see cref="ZensicalDocumentationEmitter"/>.
/// Defaults match the legacy flat-namespace layout so existing
/// callers don't need to thread options through.
/// </summary>
/// <param name="PackageRouting">Ordered routing rules — first match wins. When empty, types are emitted under <c>api/&lt;namespace&gt;/&lt;Type&gt;.md</c> (legacy layout).</param>
/// <param name="MicrosoftLearnBaseUrl">Base URL used to link <c>System.*</c> / <c>Microsoft.*</c> type references that we don't walk ourselves. Defaults to the canonical Microsoft Learn API root.</param>
public sealed record ZensicalEmitterOptions(
    PackageRoutingRule[] PackageRouting,
    string MicrosoftLearnBaseUrl = ZensicalEmitterOptions.DefaultMicrosoftLearnBaseUrl)
{
    /// <summary>Canonical Microsoft Learn .NET API root.</summary>
    public const string DefaultMicrosoftLearnBaseUrl = "https://learn.microsoft.com/dotnet/api/";

    /// <summary>Gets the legacy default: no per-package routing, no override URLs.</summary>
    public static ZensicalEmitterOptions Default { get; } = new([]);
}
