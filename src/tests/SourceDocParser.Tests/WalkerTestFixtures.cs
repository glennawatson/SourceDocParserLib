// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SourceDocParser.SourceLink;
using SourceDocParser.Walk;
using SourceDocParser.XmlDoc;

namespace SourceDocParser.Tests;

/// <summary>
/// Shared helpers for the walker / builder unit tests -- compile a
/// snippet to a <see cref="CSharpCompilation"/> against the
/// runtime's BCL references and produce a default
/// <see cref="SymbolWalkContext"/> ready for the focused builders
/// to consume. Centralised so the test files stay focused on
/// behaviour rather than scaffolding.
/// </summary>
internal static class WalkerTestFixtures
{
    /// <summary>Creates metadata references for the runtime assemblies available to an in-memory fixture.</summary>
    /// <returns>References to loaded assemblies with physical locations.</returns>
    internal static MetadataReference[] GetRuntimeReferences()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        var references = new List<MetadataReference>(assemblies.Length);
        foreach (var assembly in assemblies)
        {
            if (assembly.IsDynamic || assembly.Location is [])
            {
                continue;
            }

            references.Add(MetadataReference.CreateFromFile(assembly.Location));
        }

        return references.ToArray();
    }

    /// <summary>
    /// Builds an in-memory compilation against the runtime's BCL
    /// references with C# preview features enabled (so the C# 14
    /// extension-block syntax parses).
    /// </summary>
    /// <param name="source">C# source text to compile.</param>
    /// <returns>The compiled (but not emitted) compilation.</returns>
    internal static CSharpCompilation Compile(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new(LanguageVersion.Preview));
        List<MetadataReference> references =
        [
            .. WalkerTestFixtures.GetRuntimeReferences(),
        ];
        return CSharpCompilation.Create(
            "WalkerTest",
            [tree],
            references,
            new(OutputKind.DynamicallyLinkedLibrary));
    }

    /// <summary>
    /// Builds a default <see cref="SymbolWalkContext"/> for
    /// <paramref name="compilation"/>, wired with a real
    /// <see cref="DocResolver"/>, an empty type-ref cache, and a
    /// no-op SourceLink resolver. Suitable for any builder that
    /// needs the per-walk plumbing.
    /// </summary>
    /// <param name="compilation">Compilation the context should wrap.</param>
    /// <returns>The constructed context.</returns>
    internal static SymbolWalkContext NewContext(CSharpCompilation compilation) => new(
        AssemblyName: compilation.AssemblyName ?? "WalkerTest",
        Tfm: "net10.0",
        Docs: new DocResolver(compilation),
        TypeRefs: new(),
        SourceLinks: new NullSourceLinkResolver(),
        NamespaceDisplayNames: new(),
        AppliesTo: ["net10.0"]);

    /// <summary>SourceLink resolver that returns null for every symbol -- keeps the builder tests free of a real PDB dependency.</summary>
    private sealed class NullSourceLinkResolver : ISourceLinkResolver
    {
        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string? Resolve(ISymbol symbol) => null;

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }
}
