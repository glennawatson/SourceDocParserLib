// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Zensical.Navigation;

/// <summary>
/// One package in the nav graph -- the top-level grouping the
/// Zensical emitter writes types under (typically the assembly name
/// or a routed override).
/// </summary>
/// <param name="Name">Display title for the package.</param>
/// <param name="Namespaces">The namespace nodes that live in this package, in display order.</param>
public readonly record struct NavigationPackage(string Name, NavigationNamespace[] Namespaces);
