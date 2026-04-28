// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Zensical.Navigation;

/// <summary>
/// One namespace under a package. The display name is either the
/// dotted CLR namespace or <c>(global)</c> for the unnamed namespace.
/// </summary>
/// <param name="Name">Display title for the namespace.</param>
/// <param name="Folder">
/// On-disk folder name relative to the owning package folder (dots become
/// forward slashes; the global namespace folds to <c>_global</c>).
/// </param>
/// <param name="LandingPagePath">
/// POSIX-relative path to the namespace's <c>index.md</c> landing page, or
/// <see langword="null"/> when no landing page was emitted.
/// </param>
/// <param name="Types">The type entries in this namespace, in display order.</param>
public readonly record struct NavigationNamespace(string Name, string Folder, string? LandingPagePath, NavigationEntry[] Types);
