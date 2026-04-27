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
public sealed record ZensicalEmitterOptions(
    PackageRoutingRule[] PackageRouting,
    string MicrosoftLearnBaseUrl = ZensicalEmitterOptions.DefaultMicrosoftLearnBaseUrl)
{
    /// <summary>Canonical Microsoft Learn .NET API root.</summary>
    [SuppressMessage("Critical Code Smell", "S2339:Public constant members should not be used", Justification = "Default value is not secret.")]
    public const string DefaultMicrosoftLearnBaseUrl = "https://learn.microsoft.com/dotnet/api/";

    /// <summary>Gets the legacy default: no per-package routing, no override URLs.</summary>
    public static ZensicalEmitterOptions Default { get; } = new([]);
}
