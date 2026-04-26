// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;

namespace SourceDocParser.Tests;

/// <summary>
/// Drives <see cref="MemberBuilder.Build"/> and
/// <see cref="MemberBuilder.BuildOne"/> against synthesised
/// Roslyn types so the per-symbol conversion can be pinned without
/// running the full <see cref="SymbolWalker"/>. Verifies the
/// implicit / unsupported / non-public skip rules as well as the
/// happy-path field population.
/// </summary>
public class MemberBuilderTests
{
    /// <summary>Build returns one member per externally-visible classifiable symbol.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildSurfacesPublicMethods()
    {
        var compilation = WalkerTestFixtures.Compile(
            """
            public class Foo
            {
                public void Run() { }
                public string Name => "x";
                private void Hidden() { }
            }
            """);
        var fooSymbol = (INamedTypeSymbol)compilation.GetSymbolsWithName("Foo").Single();
        var context = WalkerTestFixtures.NewContext(compilation);

        var members = MemberBuilder.Build(fooSymbol, fooSymbol.Name, fooSymbol.GetDocumentationCommentId() ?? string.Empty, context);
        var names = members.Select(m => m.Name).ToHashSet();

        await Assert.That(names).Contains("Run");
        await Assert.That(names).Contains("Name");
        await Assert.That(names).DoesNotContain("Hidden");
    }

    /// <summary>Build skips compiler-implicit accessors like property getters that show up alongside the property.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildSkipsImplicitAccessors()
    {
        var compilation = WalkerTestFixtures.Compile(
            """
            public class Foo
            {
                public string Name { get; }
            }
            """);
        var fooSymbol = (INamedTypeSymbol)compilation.GetSymbolsWithName("Foo").Single();
        var context = WalkerTestFixtures.NewContext(compilation);

        var members = MemberBuilder.Build(fooSymbol, fooSymbol.Name, "T:Foo", context);
        var names = members.Select(m => m.Name).ToList();

        await Assert.That(names).Contains("Name");

        // The synthesised get_Name accessor is implicitly declared and
        // must be filtered before reaching the conversion path.
        await Assert.That(names).DoesNotContain("get_Name");
    }

    /// <summary>BuildOne propagates the supplied containing type identity onto the constructed member.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task BuildOnePropagatesContainingTypeFields()
    {
        var compilation = WalkerTestFixtures.Compile(
            """
            public class Foo { public void Run() { } }
            """);
        var fooSymbol = (INamedTypeSymbol)compilation.GetSymbolsWithName("Foo").Single();
        var run = fooSymbol.GetMembers("Run").OfType<IMethodSymbol>().Single();
        var context = WalkerTestFixtures.NewContext(compilation);

        var member = MemberBuilder.BuildOne(run, ApiMemberKind.Method, "MyType", "T:My.MyType", context);

        await Assert.That(member.Name).IsEqualTo("Run");
        await Assert.That(member.Kind).IsEqualTo(ApiMemberKind.Method);
        await Assert.That(member.ContainingTypeName).IsEqualTo("MyType");
        await Assert.That(member.ContainingTypeUid).IsEqualTo("T:My.MyType");
    }
}
