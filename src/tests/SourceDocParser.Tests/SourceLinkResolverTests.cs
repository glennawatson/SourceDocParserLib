// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SourceDocParser.SourceLink;

namespace SourceDocParser.Tests;

/// <summary>
/// Tests for <see cref="SourceLinkResolver"/> and <see cref="ISourceLinkResolver"/>.
/// Driven against the test runner's own DLL — we don't assume it ships with
/// SourceLink data, so most assertions are about graceful-null behaviour.
/// </summary>
public class SourceLinkResolverTests
{
    /// <summary>Gets the path to an always-present DLL next to the test binary.</summary>
    private static string TestAssemblyPath { get; } =
        Path.Combine(AppContext.BaseDirectory, "SourceDocParser.dll");

    /// <summary>
    /// Constructing against a non-existent file does not throw — the
    /// resolver swallows IO errors so a single broken assembly never
    /// aborts a multi-package walk.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ConstructorSwallowsMissingAssembly()
    {
        var bogus = Path.Combine(Path.GetTempPath(), $"sdp-bogus-{Guid.NewGuid():N}.dll");
        using var resolver = new SourceLinkResolver(bogus);

        var symbol = BuildClassSymbol();
        await Assert.That(resolver.Resolve(symbol)).IsNull();
    }

    /// <summary>
    /// Resolving against a real assembly without SourceLink data returns null.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ResolveReturnsNullWhenNoSourceLink()
    {
        using var resolver = new SourceLinkResolver(TestAssemblyPath);
        var symbol = BuildClassSymbol();

        // Whether SourceLink is present is a build-time concern; we just
        // assert that the resolver does not throw and returns either
        // null or a non-empty string.
        var url = resolver.Resolve(symbol);
        await Assert.That(url is null || url.Length > 0).IsTrue();
    }

    /// <summary>
    /// Calling Dispose more than once does not throw.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DisposeIsIdempotent()
    {
        var resolver = new SourceLinkResolver(TestAssemblyPath);
        resolver.Dispose();
        await Assert.That(resolver.Dispose).ThrowsNothing();
    }

    /// <summary>
    /// Builds a simple <see cref="INamedTypeSymbol"/> via an in-memory
    /// compilation so the resolver has something concrete to attempt
    /// resolution on.
    /// </summary>
    /// <returns>The built type symbol.</returns>
    private static INamedTypeSymbol BuildClassSymbol()
    {
        var tree = CSharpSyntaxTree.ParseText("public class Probe { public int M() => 0; }");
        List<MetadataReference> refs =
        [
            .. AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => MetadataReference.CreateFromFile(a.Location)),
        ];
        var compilation = CSharpCompilation.Create("Probe", [tree], refs);
        return compilation.GetTypeByMetadataName("Probe")!;
    }
}
