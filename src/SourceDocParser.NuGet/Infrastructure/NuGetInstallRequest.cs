// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using SourceDocParser.NuGet.Models;

namespace SourceDocParser.NuGet.Infrastructure;

/// <summary>
/// Bundles the shared inputs required to install one package from one or more NuGet sources.
/// </summary>
/// <param name="EnabledSources">Resolved enabled sources to probe.</param>
/// <param name="Credentials">Per-source credentials.</param>
/// <param name="FeedHttp">HTTP surface used for feed metadata and package downloads.</param>
/// <param name="Logger">Logger for install progress.</param>
/// <param name="FlatContainerByFeed">Per-source flat-container endpoint cache.</param>
/// <param name="PackageId">NuGet package identifier.</param>
/// <param name="PackageVersion">Normalized package version.</param>
/// <param name="InstallPath">Per-package destination directory.</param>
/// <param name="CancellationToken">Cancellation token observed across the install.</param>
internal readonly record struct NuGetInstallRequest(
    PackageSource[] EnabledSources,
    Dictionary<string, PackageSourceCredential> Credentials,
    INuGetFeedHttpClient FeedHttp,
    ILogger Logger,
    Dictionary<string, string?> FlatContainerByFeed,
    string PackageId,
    string PackageVersion,
    string InstallPath,
    CancellationToken CancellationToken);
