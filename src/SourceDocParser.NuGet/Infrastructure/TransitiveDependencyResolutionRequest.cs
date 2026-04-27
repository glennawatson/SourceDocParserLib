// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.NuGet.Infrastructure;

/// <summary>
/// Bundles the inputs required for one transitive dependency resolution pass.
/// </summary>
/// <param name="LibDir">Per-TFM lib output root.</param>
/// <param name="CacheDir">Cache directory containing downloaded packages and nuspec sidecars.</param>
/// <param name="SeenIds">Already-resolved package identifiers, mutated as new IDs are discovered.</param>
/// <param name="ExcludeIds">Exact package excludes.</param>
/// <param name="ExcludePrefixes">Prefix package excludes.</param>
/// <param name="TfmOverrides">Per-package TFM overrides.</param>
/// <param name="TfmPreference">Global TFM preference order.</param>
/// <param name="Logger">Logger for progress and failure messages.</param>
/// <param name="CancellationToken">Cancellation token observed across the walk.</param>
internal readonly record struct TransitiveDependencyResolutionRequest(
    string LibDir,
    string CacheDir,
    HashSet<string> SeenIds,
    string[] ExcludeIds,
    string[] ExcludePrefixes,
    Dictionary<string, string> TfmOverrides,
    string[] TfmPreference,
    ILogger Logger,
    CancellationToken CancellationToken);
