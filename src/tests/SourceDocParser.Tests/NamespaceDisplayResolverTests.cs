// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using SourceDocParser.Walk;

namespace SourceDocParser.Tests;

/// <summary>
/// Pins <see cref="NamespaceDisplayResolver.Resolve"/> on the
/// global-namespace short-circuit and the per-walk cache that keeps
/// repeated lookups allocation-free.
/// </summary>
public class NamespaceDisplayResolverTests
{
    /// <summary>Global namespace folds to the empty string.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GlobalNamespaceReturnsEmpty()
    {
        var compilation = WalkerTestFixtures.Compile("public class Foo { }");
        var context = WalkerTestFixtures.NewContext(compilation);

        var result = NamespaceDisplayResolver.Resolve(context, compilation.Assembly.GlobalNamespace);

        await Assert.That(result).IsEqualTo(string.Empty);
    }

    /// <summary>Null namespace input returns the empty string (treated as global).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task NullNamespaceReturnsEmpty()
    {
        var compilation = WalkerTestFixtures.Compile("public class Foo { }");
        var context = WalkerTestFixtures.NewContext(compilation);

        var result = NamespaceDisplayResolver.Resolve(context, null);

        await Assert.That(result).IsEqualTo(string.Empty);
    }

    /// <summary>Non-global namespace returns its display string and cache hits return the same instance.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task NonGlobalNamespaceCachesDisplayName()
    {
        var compilation = WalkerTestFixtures.Compile("namespace My.Lib { public class Foo { } }");
        var fooSymbol = (INamedTypeSymbol)compilation.GetSymbolsWithName("Foo").Single();
        var context = WalkerTestFixtures.NewContext(compilation);

        var first = NamespaceDisplayResolver.Resolve(context, fooSymbol.ContainingNamespace);
        var second = NamespaceDisplayResolver.Resolve(context, fooSymbol.ContainingNamespace);

        await Assert.That(first).IsEqualTo("My.Lib");
        await Assert.That(ReferenceEquals(first, second)).IsTrue();
    }
}
