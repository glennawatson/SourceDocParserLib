// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Zensical.Navigation;

/// <summary>
/// Immutable, allocation-light graph describing the API navigation
/// tree the Zensical emitter would produce for a set of types. The
/// shape is package -&gt; namespace -&gt; type, sorted ordinally at
/// every level. Each node is a <c>readonly record struct</c> wrapping
/// arrays so consumers can iterate without enumerator allocation and
/// project the graph into any nav format Zensical accepts (YAML,
/// TOML, hand-rolled JSON for awesome-nav-style configs, etc).
/// </summary>
/// <param name="Packages">The package nodes in display order.</param>
public readonly record struct NavigationGraph(NavigationPackage[] Packages);
