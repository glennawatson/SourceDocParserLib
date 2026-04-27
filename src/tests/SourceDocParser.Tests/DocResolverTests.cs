// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Xml;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SourceDocParser.Model;
using SourceDocParser.XmlDoc;

namespace SourceDocParser.Tests;

/// <summary>
/// Tests for <see cref="DocResolver"/> driven against an in-memory
/// <see cref="CSharpCompilation"/> with XML doc parsing turned on.
/// Exercises the public seam (<see cref="IDocResolver.Resolve"/>),
/// the per-instance cache, the explicit <c>&lt;inheritdoc/&gt;</c>
/// path, and the <see cref="IXmlDocToMarkdownConverter"/> injection
/// point.
/// </summary>
public class DocResolverTests
{
    /// <summary>
    /// A symbol with a plain <c>&lt;summary&gt;</c> resolves to a
    /// non-empty <see cref="ApiDocumentation"/>.
    /// </summary>
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
        var symbol = compilation.GetTypeByMetadataName("Foo.Bar")!;

        var doc = resolver.Resolve(symbol);

        await Assert.That(doc.Summary).Contains("The bar.");
    }

    /// <summary>
    /// A symbol with no XML doc resolves to <see cref="ApiDocumentation.Empty"/>.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ResolveReturnsEmptyForUndocumentedSymbol()
    {
        // Use a plain method on a plain class — types auto-inherit
        // from their base type (which would pull in System.Object's
        // XML docs from the BCL), but a non-override method has no
        // natural source so the resolver legitimately returns Empty.
        var compilation = BuildCompilation("namespace Foo { public class Bare { public void Op() { } } }");
        var resolver = new DocResolver(compilation);
        var symbol = compilation.GetTypeByMetadataName("Foo.Bare")!.GetMembers("Op").OfType<IMethodSymbol>().Single();

        var doc = resolver.Resolve(symbol);

        // ApiDocumentation.IsEmpty is reference-equality to the static
        // Empty sentinel; ToApiDocumentation builds a fresh record per
        // call so we assert on field content instead.
        await Assert.That(doc.Summary).IsEqualTo(string.Empty);
        await Assert.That(doc.Remarks).IsEqualTo(string.Empty);
        await Assert.That(doc.InheritedFrom).IsNull();
    }

    /// <summary>
    /// Resolving the same symbol twice returns the cached instance
    /// reference — proves the per-resolver memoisation works.
    /// </summary>
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
        var symbol = compilation.GetTypeByMetadataName("Foo.Bar")!;

        var first = resolver.Resolve(symbol);
        var second = resolver.Resolve(symbol);

        await Assert.That(ReferenceEquals(first, second)).IsTrue();
    }

    /// <summary>
    /// An override with no docs of its own auto-inherits from the base.
    /// </summary>
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
        var run = derived.GetMembers("Run").OfType<IMethodSymbol>().Single();

        var doc = resolver.Resolve(run);

        await Assert.That(doc.Summary).Contains("Base summary.");
        await Assert.That(doc.InheritedFrom).IsNotNull();
    }

    /// <summary>
    /// Explicit <c>&lt;inheritdoc/&gt;</c> walks to the base.
    /// </summary>
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
        var run = derived.GetMembers("Run").OfType<IMethodSymbol>().Single();

        var doc = resolver.Resolve(run);

        await Assert.That(doc.Summary).Contains("Base summary.");
    }

    /// <summary>
    /// The injected <see cref="IXmlDocToMarkdownConverter"/> is the one
    /// the resolver calls — verified via a recording fake.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ResolveUsesInjectedConverter()
    {
        var compilation = BuildCompilation(
            """
            namespace Foo
            {
                /// <summary>The bar.</summary>
                public class Bar { }
            }
            """);
        var converter = new RecordingConverter();
        var resolver = new DocResolver(compilation, converter);
        var symbol = compilation.GetTypeByMetadataName("Foo.Bar")!;

        resolver.Resolve(symbol);

        // Span overload is what DocResolver.Parse routes through after the scanner refactor.
        await Assert.That(converter.SpanCalls).IsGreaterThanOrEqualTo(1);
    }

    /// <summary>Null compilation throws on construction.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConstructorValidatesCompilation() => await Assert.That(static () => new DocResolver(null!)).Throws<ArgumentNullException>();

    /// <summary>
    /// Builds an in-memory <see cref="CSharpCompilation"/> from
    /// <paramref name="source"/> with XML doc parsing on so symbols
    /// carry their associated <c>&lt;summary&gt;</c> etc.
    /// </summary>
    /// <param name="source">C# source text to compile.</param>
    /// <returns>The compiled (but not emitted) compilation.</returns>
    private static CSharpCompilation BuildCompilation(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(
            source,
            new(documentationMode: DocumentationMode.Parse));
        List<MetadataReference> references =
        [
            .. AppDomain.CurrentDomain.GetAssemblies()
                .Where(static a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(static a => MetadataReference.CreateFromFile(a.Location)),
        ];
        return CSharpCompilation.Create(
            "DocTest",
            [tree],
            references,
            new(
                outputKind: OutputKind.DynamicallyLinkedLibrary,
                xmlReferenceResolver: XmlFileResolver.Default));
    }

    /// <summary>
    /// Recording <see cref="IXmlDocToMarkdownConverter"/> that counts
    /// invocations of each overload — used to verify
    /// <see cref="DocResolver"/> routes through the injected converter.
    /// </summary>
    private sealed class RecordingConverter : IXmlDocToMarkdownConverter
    {
        /// <summary>Gets the count of string-overload Convert calls.</summary>
        public int StringCalls { get; private set; }

        /// <summary>Gets the count of async XmlReader-overload Convert calls.</summary>
        public int ReaderAsyncCalls { get; private set; }

        /// <summary>Gets the count of span-overload Convert calls.</summary>
        public int SpanCalls { get; private set; }

        /// <inheritdoc />
        public string Convert(string xmlFragment)
        {
            StringCalls++;
            return xmlFragment;
        }

        /// <inheritdoc />
        public Task<string> ConvertAsync(XmlReader reader) => ConvertAsync(reader, CancellationToken.None);

        /// <inheritdoc />
        public async Task<string> ConvertAsync(XmlReader reader, CancellationToken cancellationToken)
        {
            ReaderAsyncCalls++;
            return reader.IsEmptyElement ? string.Empty : await reader.ReadInnerXmlAsync().ConfigureAwait(false);
        }

        /// <inheritdoc />
        public string Convert(ReadOnlySpan<char> innerXml)
        {
            SpanCalls++;
            return innerXml.ToString();
        }
    }
}
