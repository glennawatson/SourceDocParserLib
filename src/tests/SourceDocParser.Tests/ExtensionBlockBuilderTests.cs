// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;

namespace SourceDocParser.Tests;

/// <summary>
/// Drives <see cref="ExtensionBlockBuilder.Build"/> and
/// <see cref="ExtensionBlockBuilder.TryBuildBlock"/> against
/// synthesised C# 14 extension declarations so the marker-type
/// detection + receiver mapping can be pinned independently of
/// <see cref="SymbolWalker"/>.
/// </summary>
public class ExtensionBlockBuilderTests
{
    /// <summary>Build returns one block per extension declaration on the host type.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildSurfacesEachExtensionBlock()
    {
        var compilation = WalkerTestFixtures.Compile(
            """
            public static class Helpers
            {
                extension(string source) { public bool IsEmpty => source.Length == 0; }
                extension(int value) { public int Doubled => value * 2; }
            }
            """);
        var helpers = (INamedTypeSymbol)compilation.GetSymbolsWithName("Helpers").Single();
        var context = WalkerTestFixtures.NewContext(compilation);

        var blocks = ExtensionBlockBuilder.Build(helpers, context);

        await Assert.That(blocks.Length).IsEqualTo(2);
        var receiverNames = blocks.Select(b => b.ReceiverName).ToHashSet();
        await Assert.That(receiverNames).Contains("source");
        await Assert.That(receiverNames).Contains("value");
    }

    /// <summary>Build returns the shared empty array when no nested types qualify.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildReturnsEmptyForRegularClass()
    {
        var compilation = WalkerTestFixtures.Compile(
            """
            public class Foo { public void Run() { } }
            """);
        var foo = (INamedTypeSymbol)compilation.GetSymbolsWithName("Foo").Single();
        var context = WalkerTestFixtures.NewContext(compilation);

        var blocks = ExtensionBlockBuilder.Build(foo, context);

        await Assert.That(blocks).IsEmpty();
    }

    /// <summary>TryBuildBlock returns null for nested types that aren't extension markers.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task TryBuildBlockReturnsNullForRegularNestedType()
    {
        var compilation = WalkerTestFixtures.Compile(
            """
            public class Outer { public class Inner { } }
            """);
        var outer = (INamedTypeSymbol)compilation.GetSymbolsWithName("Outer").Single();
        var inner = outer.GetTypeMembers("Inner").Single();
        var context = WalkerTestFixtures.NewContext(compilation);

        var block = ExtensionBlockBuilder.TryBuildBlock(inner, context);

        await Assert.That(block).IsNull();
    }
}
