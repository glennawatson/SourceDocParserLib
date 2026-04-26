// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SourceDocParser.SourceLink;

namespace SourceDocParser.Tests;

/// <summary>
/// Tests pin each helper in <see cref="TypeForwardingHelpers"/>
/// against synthetic two-assembly compilations: a "target" assembly
/// with the real type definition and an "umbrella" assembly that
/// only carries <c>[assembly: TypeForwardedTo]</c> attributes.
/// </summary>
public class TypeForwardingHelpersTests
{
    /// <summary>
    /// Walks the umbrella's forwards via the public helper. With the
    /// target assembly referenced, every forwarded entry resolves to
    /// a real <see cref="INamedTypeSymbol"/> the walker can build.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GetForwardedTypesReturnsEveryTypeForwardedTo()
    {
        var (umbrella, _) = BuildUmbrellaWithTarget();
        var forwarded = TypeForwardingHelpers.GetForwardedTypes(umbrella);

        await Assert.That(forwarded.Length).IsEqualTo(2);
        var names = forwarded.Select(static t => t.MetadataName).ToArray();
        await Assert.That(names).Contains("PublicForwarded");
        await Assert.That(names).Contains("OtherForwarded");
    }

    /// <summary>
    /// Resolvability: a forwarded target whose defining assembly is
    /// in the references list resolves to a non-error symbol.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task IsResolvableReturnsTrueWhenTargetAssemblyLoaded()
    {
        var (umbrella, _) = BuildUmbrellaWithTarget();
        var forwarded = TypeForwardingHelpers.GetForwardedTypes(umbrella);

        for (var i = 0; i < forwarded.Length; i++)
        {
            await Assert.That(TypeForwardingHelpers.IsResolvable(forwarded[i])).IsTrue();
        }
    }

    /// <summary>
    /// Without the target assembly in references, Roslyn returns the
    /// forward as an error symbol — IsResolvable filters it out so
    /// the walker doesn't try to build a stub-only page.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task IsResolvableReturnsFalseWhenTargetAssemblyMissing()
    {
        var umbrella = BuildUmbrellaWithoutTargetAssembly();
        var forwarded = TypeForwardingHelpers.GetForwardedTypes(umbrella);

        await Assert.That(forwarded).IsNotEmpty();
        for (var i = 0; i < forwarded.Length; i++)
        {
            await Assert.That(TypeForwardingHelpers.IsResolvable(forwarded[i])).IsFalse();
        }
    }

    /// <summary>
    /// IsAlreadyCollected hits the seenTypeUids hash for a forwarded
    /// type whose UID matches an entry the walker has already
    /// produced — keeps the umbrella + sibling combo from doubling
    /// up.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task IsAlreadyCollectedFlagsDuplicateUids()
    {
        var (umbrella, _) = BuildUmbrellaWithTarget();
        var forwarded = TypeForwardingHelpers.GetForwardedTypes(umbrella);
        var first = forwarded[0];
        var seen = new HashSet<string>(StringComparer.Ordinal) { first.GetDocumentationCommentId()! };

        await Assert.That(TypeForwardingHelpers.IsAlreadyCollected(first, seen)).IsTrue();
        await Assert.That(TypeForwardingHelpers.IsAlreadyCollected(forwarded[1], seen)).IsFalse();
    }

    /// <summary>
    /// SeedPending pushes every forwarded type onto the supplied
    /// stack — and only those — without allocating its own.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SeedPendingPushesForwardedRoots()
    {
        var (umbrella, _) = BuildUmbrellaWithTarget();
        var pending = new Stack<INamedTypeSymbol>();
        var pushed = TypeForwardingHelpers.SeedPending(umbrella, pending);

        await Assert.That(pushed).IsEqualTo(2);
        await Assert.That(pending.Count).IsEqualTo(2);
    }

    /// <summary>
    /// PushNested adds nested-type members of a parent onto the stack
    /// — matches the namespace-walk shape so the walker's drain loop
    /// surfaces them through the same filtering.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task PushNestedAddsNestedTypeMembers()
    {
        var (umbrella, _) = BuildUmbrellaWithTarget();
        var forwarded = TypeForwardingHelpers.GetForwardedTypes(umbrella);
        var parent = forwarded.First(static t => t.MetadataName == "PublicForwarded");
        var pending = new Stack<INamedTypeSymbol>();
        var pushed = TypeForwardingHelpers.PushNested(parent, pending);

        await Assert.That(pushed).IsEqualTo(1);
        await Assert.That(pending.Pop().MetadataName).IsEqualTo("Nested");
    }

    /// <summary>
    /// End-to-end: the walker produces type pages for forwarded
    /// targets when the destination assembly is referenced. Pins the
    /// composition: helpers + walker plumbing land the type in the
    /// catalog under the umbrella's assembly name.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task SymbolWalkerEmitsForwardedTypeUnderUmbrellaAssembly()
    {
        var (umbrella, umbrellaCompilation) = BuildUmbrellaWithTarget();
        var walker = new SymbolWalker();
        using ISourceLinkResolver resolver = new NullSourceLinkResolver();

        var catalog = walker.Walk("net10.0", umbrella, umbrellaCompilation, resolver);

        var forwardedType = catalog.Types.FirstOrDefault(static t => t.Name == "PublicForwarded");
        await Assert.That(forwardedType).IsNotNull();
        await Assert.That(forwardedType!.AssemblyName).IsEqualTo("Umbrella");
    }

    /// <summary>
    /// Builds a target assembly with two real type definitions and
    /// an umbrella assembly that forwards both to the target.
    /// Returns the umbrella assembly symbol + its compilation so
    /// caller tests can either inspect forwards or invoke the
    /// walker.
    /// </summary>
    /// <returns>The umbrella's assembly symbol and the compilation that produced it.</returns>
    private static (IAssemblySymbol UmbrellaAssembly, CSharpCompilation UmbrellaCompilation) BuildUmbrellaWithTarget()
    {
        const string targetSource = """
            namespace My.Pkg.Core;

            public class PublicForwarded
            {
                public class Nested { }
                public void Run() { }
            }

            public class OtherForwarded { }
            """;

        var target = CSharpCompilation.Create(
            "TargetCore",
            [CSharpSyntaxTree.ParseText(targetSource)],
            BclReferences(),
            new(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var emit = target.Emit(stream);
        if (!emit.Success)
        {
            throw new InvalidOperationException("Target compile failed: " + string.Join('\n', emit.Diagnostics));
        }

        stream.Position = 0;
        var targetReference = MetadataReference.CreateFromStream(stream);

        const string umbrellaSource = """
            using System.Runtime.CompilerServices;

            [assembly: TypeForwardedTo(typeof(My.Pkg.Core.PublicForwarded))]
            [assembly: TypeForwardedTo(typeof(My.Pkg.Core.OtherForwarded))]
            """;

        List<MetadataReference> umbrellaRefs = [.. BclReferences(), targetReference];
        var umbrellaCompilation = CSharpCompilation.Create(
            "Umbrella",
            [CSharpSyntaxTree.ParseText(umbrellaSource)],
            umbrellaRefs,
            new(OutputKind.DynamicallyLinkedLibrary));

        return (umbrellaCompilation.Assembly, umbrellaCompilation);
    }

    /// <summary>
    /// Builds an umbrella whose forwarded targets point at an
    /// assembly that is NOT in the compilation references — Roslyn
    /// resolves them to error symbols.
    /// </summary>
    /// <returns>The umbrella's assembly symbol whose forwards point at an unloaded assembly.</returns>
    private static IAssemblySymbol BuildUmbrellaWithoutTargetAssembly()
    {
        // Need to actually have a target metadata reference at compile
        // time so the source compiles — then we deliberately drop it
        // when building the umbrella so the resolver can't bind.
        const string targetSource = "namespace My.Pkg.Core { public class PublicForwarded { } }";
        var target = CSharpCompilation.Create(
            "TargetCore",
            [CSharpSyntaxTree.ParseText(targetSource)],
            BclReferences(),
            new(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        target.Emit(stream);
        stream.Position = 0;
        var targetReference = MetadataReference.CreateFromStream(stream);

        // First compile the umbrella WITH the target so the source
        // binds; emit it to bytes; then re-load the umbrella WITHOUT
        // the target reference so its forwards become error symbols.
        const string umbrellaSource = """
            using System.Runtime.CompilerServices;
            [assembly: TypeForwardedTo(typeof(My.Pkg.Core.PublicForwarded))]
            """;
        var umbrellaCompiled = CSharpCompilation.Create(
            "UmbrellaIso",
            [CSharpSyntaxTree.ParseText(umbrellaSource)],
            [.. BclReferences(), targetReference],
            new(OutputKind.DynamicallyLinkedLibrary));

        using var umbrellaStream = new MemoryStream();
        umbrellaCompiled.Emit(umbrellaStream);
        umbrellaStream.Position = 0;
        var umbrellaReference = MetadataReference.CreateFromStream(umbrellaStream);

        // Re-load the umbrella with NO targetReference — the forwards
        // can no longer resolve, so Roslyn surfaces them as error
        // symbols.
        var probe = CSharpCompilation.Create(
            "Probe",
            syntaxTrees: null,
            [.. BclReferences(), umbrellaReference],
            new(OutputKind.DynamicallyLinkedLibrary));

        return probe.GetAssemblyOrModuleSymbol(umbrellaReference) is IAssemblySymbol umbrellaAssembly
            ? umbrellaAssembly
            : throw new InvalidOperationException("Could not resolve umbrella assembly symbol from probe compilation.");
    }

    /// <summary>BCL refs — same shape SymbolWalkerTests uses.</summary>
    /// <returns>The list of BCL metadata references for in-memory compilations.</returns>
    private static List<MetadataReference> BclReferences() =>
    [
        .. AppDomain.CurrentDomain.GetAssemblies()
            .Where(static a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(static a => MetadataReference.CreateFromFile(a.Location)),
    ];

    /// <summary>No-op source link resolver — the walker never calls into it for this test fixture.</summary>
    private sealed class NullSourceLinkResolver : ISourceLinkResolver
    {
        /// <inheritdoc />
        public string? Resolve(ISymbol symbol) => null;

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }
}
