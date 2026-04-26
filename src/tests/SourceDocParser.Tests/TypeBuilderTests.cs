// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;

namespace SourceDocParser.Tests;

/// <summary>
/// Drives <see cref="TypeBuilder.TryBuild"/> against synthesised
/// types so each kind dispatch (object, enum, delegate, union)
/// is pinned without running the full <see cref="SymbolWalker"/>.
/// </summary>
public class TypeBuilderTests
{
    /// <summary>A class is dispatched to the <see cref="ApiObjectType"/> branch with the right kind.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ClassDispatchesToObjectKind()
    {
        var compilation = WalkerTestFixtures.Compile("public class Foo { }");
        var fooSymbol = (INamedTypeSymbol)compilation.GetSymbolsWithName("Foo").Single();
        var context = WalkerTestFixtures.NewContext(compilation);

        var built = TypeBuilder.TryBuild(fooSymbol, context);

        await Assert.That(built).IsTypeOf<ApiObjectType>();
        await Assert.That(((ApiObjectType)built!).Kind).IsEqualTo(ApiObjectKind.Class);
    }

    /// <summary>An enum is dispatched to the <see cref="ApiEnumType"/> branch.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EnumDispatchesToEnumBranch()
    {
        var compilation = WalkerTestFixtures.Compile("public enum Day { Mon, Tue }");
        var symbol = (INamedTypeSymbol)compilation.GetSymbolsWithName("Day").Single();
        var context = WalkerTestFixtures.NewContext(compilation);

        var built = TypeBuilder.TryBuild(symbol, context);

        await Assert.That(built).IsTypeOf<ApiEnumType>();
    }

    /// <summary>A delegate is dispatched to the <see cref="ApiDelegateType"/> branch.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DelegateDispatchesToDelegateBranch()
    {
        var compilation = WalkerTestFixtures.Compile("public delegate void Handler(int x);");
        var symbol = (INamedTypeSymbol)compilation.GetSymbolsWithName("Handler").Single();
        var context = WalkerTestFixtures.NewContext(compilation);

        var built = TypeBuilder.TryBuild(symbol, context);

        await Assert.That(built).IsTypeOf<ApiDelegateType>();
    }

    /// <summary>An interface is dispatched to <see cref="ApiObjectType"/> with kind <see cref="ApiObjectKind.Interface"/>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task InterfaceDispatchesToInterfaceKind()
    {
        var compilation = WalkerTestFixtures.Compile("public interface IFoo { }");
        var symbol = (INamedTypeSymbol)compilation.GetSymbolsWithName("IFoo").Single();
        var context = WalkerTestFixtures.NewContext(compilation);

        var built = TypeBuilder.TryBuild(symbol, context);

        await Assert.That(built).IsTypeOf<ApiObjectType>();
        await Assert.That(((ApiObjectType)built!).Kind).IsEqualTo(ApiObjectKind.Interface);
    }

    /// <summary>A static class with an extension block populates <see cref="ApiObjectType.ExtensionBlocks"/>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task StaticHostPopulatesExtensionBlocks()
    {
        var compilation = WalkerTestFixtures.Compile(
            """
            public static class Helpers
            {
                extension(string source) { public int Length => source.Length; }
            }
            """);
        var symbol = (INamedTypeSymbol)compilation.GetSymbolsWithName("Helpers").Single();
        var context = WalkerTestFixtures.NewContext(compilation);

        var built = (ApiObjectType?)TypeBuilder.TryBuild(symbol, context);

        await Assert.That(built).IsNotNull();
        await Assert.That(built!.ExtensionBlocks).HasSingleItem();
        await Assert.That(built.ExtensionBlocks[0].ReceiverName).IsEqualTo("source");
    }
}
