// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SourceDocParser.Model;
using SourceDocParser.XmlDoc;

namespace SourceDocParser.Tests;

/// <summary>
/// Tests for <see cref="DocResolver"/> driven against an in-memory
/// <see cref="CSharpCompilation"/> with XML doc parsing turned on.
/// Exercises the public seam (<see cref="IDocResolver.Resolve"/>),
/// the per-instance cache, the explicit <c>inheritdoc/</c>
/// path, and the <see cref="IXmlDocToMarkdownConverter"/> injection
/// point.
/// </summary>
public class DocResolverTests
{
    /// <summary>Fixture value for FooBar.</summary>
    private const string FooBar = "Foo.Bar";

    /// <summary>Expected fixture value used by Resolve_DeepInheritance_MemoisesEverySymbol.</summary>
    private const int Resolve_DeepInheritance_MemoisesEverySymbolValue = 8;

    /// <summary>A symbol with a plain <c>summary</c> resolves to a non-empty <see cref="ApiDocumentation"/>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ResolveReturnsParsedSummary()
    {
        var compilation = BuildCompilation(
            """
            namespace Foo
            {
                /// <summary>The bar.</summary>
                public class Bar { }
            }
            """);
        var resolver = new DocResolver(compilation);
        var symbol = compilation.GetTypeByMetadataName(FooBar)!;

        var doc = resolver.Resolve(symbol);

        await Assert.That(doc.Summary).Contains("The bar.");
    }

    /// <summary>A symbol with no XML doc resolves to <see cref="ApiDocumentation.Empty"/>.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ResolveReturnsEmptyForUndocumentedSymbol()
    {
        // Use a plain method on a plain class -- types auto-inherit
        // from their base type (which would pull in System.Object's
        // XML docs from the BCL), but a non-override method has no
        // natural source so the resolver legitimately returns Empty.
        var compilation = BuildCompilation("namespace Foo { public class Bare { public void Op() { } } }");
        var resolver = new DocResolver(compilation);
        var symbol = ((IMethodSymbol)(await Assert.That(compilation.GetTypeByMetadataName("Foo.Bare")!.GetMembers("Op")).HasSingleItem(static item => item is IMethodSymbol)));

        var doc = resolver.Resolve(symbol);

        // ApiDocumentation.IsEmpty is reference-equality to the static
        // Empty sentinel; ToApiDocumentation builds a fresh record per
        // call so we assert on field content instead.
        await Assert.That(doc.Summary).IsEqualTo(string.Empty);
        await Assert.That(doc.Remarks).IsEqualTo(string.Empty);
        await Assert.That(doc.InheritedFrom).IsNull();
    }

    /// <summary>Resolving the same symbol twice returns the cached instance reference -- proves the per-resolver memoisation works.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ResolveMemoisesPerSymbol()
    {
        var compilation = BuildCompilation(
            """
            namespace Foo
            {
                /// <summary>Cached.</summary>
                public class Bar { }
            }
            """);
        var resolver = new DocResolver(compilation);
        var symbol = compilation.GetTypeByMetadataName(FooBar)!;

        var first = resolver.Resolve(symbol);
        var second = resolver.Resolve(symbol);

        await Assert.That(ReferenceEquals(first, second)).IsTrue();
    }

    /// <summary>Recursive inheritance preserves cached results across dictionary growth.</summary>
    /// <param name="inheritDoc">Documentation on each override.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments("")]
    [Arguments("/// <inheritdoc/>")]
    public async Task Resolve_DeepInheritance_MemoisesEverySymbol(string inheritDoc)
    {
        var declarations = new List<string>
        {
            """
            public class Level0
            {
                /// <summary>Root documentation.</summary>
                public virtual void Run() { }
            }
            """,
        };
        for (var level = 1; level <= Resolve_DeepInheritance_MemoisesEverySymbolValue; level++)
        {
            declarations.Add($$"""
                public class Level{{level}} : Level{{level - 1}}
                {
                    {{inheritDoc}}
                    public override void Run() { }
                }
                """);
        }

        var compilation = BuildCompilation(string.Join(Environment.NewLine, declarations));
        var resolver = new DocResolver(compilation);
        var leaf = (await Assert.That(compilation.GetTypeByMetadataName("Level8")!.GetMembers("Run")).HasSingleItem());
        var first = resolver.Resolve(leaf);

        await Assert.That(first.Summary).Contains("Root documentation.");
        await Assert.That(ReferenceEquals(first, resolver.Resolve(leaf))).IsTrue();
        for (var level = 0; level <= Resolve_DeepInheritance_MemoisesEverySymbolValue; level++)
        {
            var symbol = (await Assert.That(compilation.GetTypeByMetadataName($"Level{level}")!.GetMembers("Run")).HasSingleItem());
            var doc = resolver.Resolve(symbol);
            await Assert.That(doc).IsNotNull();
            await Assert.That(doc.Summary).Contains("Root documentation.");
            await Assert.That(ReferenceEquals(doc, resolver.Resolve(symbol))).IsTrue();
        }
    }

    /// <summary>Cyclic inheritance terminates and retains each member's own documentation.</summary>
    /// <param name="target">The inheritdoc target that closes the cycle.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Test]
    [Arguments("First")]
    [Arguments("Second")]
    public async Task Resolve_CyclicInheritDoc_PreservesOwnDocumentation(string target)
    {
        var compilation = BuildCompilation($$"""
            public class Example
            {
                /// <summary>First documentation.</summary>
                /// <inheritdoc cref="{{target}}"/>
                public void First() { }

                /// <summary>Second documentation.</summary>
                /// <inheritdoc cref="First"/>
                public void Second() { }
            }
            """);
        var resolver = new DocResolver(compilation);
        var type = compilation.GetTypeByMetadataName("Example")!;
        var first = (await Assert.That(type.GetMembers("First")).HasSingleItem());
        var second = (await Assert.That(type.GetMembers("Second")).HasSingleItem());

        var firstDoc = resolver.Resolve(first);
        var secondDoc = resolver.Resolve(second);

        await Assert.That(firstDoc.Summary).Contains("First documentation.");
        await Assert.That(secondDoc.Summary).Contains("Second documentation.");
        await Assert.That(ReferenceEquals(firstDoc, resolver.Resolve(first))).IsTrue();
        await Assert.That(ReferenceEquals(secondDoc, resolver.Resolve(second))).IsTrue();
    }

    /// <summary>An override with no docs of its own auto-inherits from the base.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ResolveAutoInheritsFromBase()
    {
        var compilation = BuildCompilation(
            """
            namespace Foo
            {
                public abstract class BaseType
                {
                    /// <summary>Base summary.</summary>
                    public abstract void Run();
                }
                public class Derived : BaseType
                {
                    public override void Run() { }
                }
            }
            """);
        var resolver = new DocResolver(compilation);
        var derived = compilation.GetTypeByMetadataName("Foo.Derived")!;
        var run = ((IMethodSymbol)(await Assert.That(derived.GetMembers("Run")).HasSingleItem(static item => item is IMethodSymbol)));

        var doc = resolver.Resolve(run);

        await Assert.That(doc.Summary).Contains("Base summary.");
        await Assert.That(doc.InheritedFrom).IsNotNull();
    }

    /// <summary>Explicit <c>inheritdoc/</c> walks to the base.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ResolveHonoursExplicitInheritDoc()
    {
        var compilation = BuildCompilation(
            """
            namespace Foo
            {
                public abstract class BaseType
                {
                    /// <summary>Base summary.</summary>
                    public abstract void Run();
                }
                public class Derived : BaseType
                {
                    /// <inheritdoc/>
                    public override void Run() { }
                }
            }
            """);
        var resolver = new DocResolver(compilation);
        var derived = compilation.GetTypeByMetadataName("Foo.Derived")!;
        var run = ((IMethodSymbol)(await Assert.That(derived.GetMembers("Run")).HasSingleItem(static item => item is IMethodSymbol)));

        var doc = resolver.Resolve(run);

        await Assert.That(doc.Summary).Contains("Base summary.");
    }

    /// <summary>
    /// The resolver surfaces the raw inner XML of the source
    /// <c>summary</c> tag -- it no longer renders Markdown.
    /// Emitters perform the conversion at render time via
    /// <see cref="XmlDocToMarkdown"/>.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ResolveSurfacesRawInnerXml()
    {
        var compilation = BuildCompilation(
            """
            namespace Foo
            {
                /// <summary>The <see cref="T:System.String"/> bar.</summary>
                public class Bar { }
            }
            """);
        var resolver = new DocResolver(compilation);
        var symbol = compilation.GetTypeByMetadataName(FooBar)!;

        var doc = resolver.Resolve(symbol);

        // Raw inner XML keeps the <see/> element intact; emitters render it later.
        await Assert.That(doc.Summary).Contains("<see cref=\"T:System.String\"/>");
    }

    /// <summary>Null compilation throws on construction.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConstructorValidatesCompilation() => await Assert.That(static () => new DocResolver(null!)).Throws<ArgumentNullException>();

    /// <summary>Builds an in-memory <see cref="CSharpCompilation"/> from <paramref name="source"/> with XML doc parsing on so symbols carry their associated <c>summary</c> etc.</summary>
    /// <param name="source">C# source text to compile.</param>
    /// <returns>The compiled (but not emitted) compilation.</returns>
    private static CSharpCompilation BuildCompilation(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(
            source,
            new(documentationMode: DocumentationMode.Parse));
        List<MetadataReference> references =
        [
            .. WalkerTestFixtures.GetRuntimeReferences(),
        ];
        return CSharpCompilation.Create(
            "DocTest",
            [tree],
            references,
            new(
                outputKind: OutputKind.DynamicallyLinkedLibrary,
                xmlReferenceResolver: XmlFileResolver.Default));
    }
}
