// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.NuGet.Models;

/// <summary>
/// Describes a NuGet package that should be fetched in addition to the
/// packages discovered automatically through owner queries. Used for
/// dependencies that are not owned by any of the configured owners but
/// are still required for documentation generation.
/// </summary>
/// <param name="Id">The NuGet package identifier (for example <c>System.Reactive</c>).</param>
/// <param name="Version">Optional pinned package version. When <see langword="null"/> the latest stable version is resolved from the NuGet feed at fetch time.</param>
internal sealed record AdditionalPackage(string Id, string? Version = null);
