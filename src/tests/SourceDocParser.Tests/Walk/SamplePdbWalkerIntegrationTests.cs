// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SamplePdb;
using SourceDocParser.Model;
using SourceDocParser.SourceLink;
using SourceDocParser.Walk;

namespace SourceDocParser.Tests.Walk;

/// <summary>
/// End-to-end coverage of <see cref="SymbolWalker"/> against the
/// SamplePdb fixture assembly — proves the walker handles a real
/// portable PE+PDB load (not just the in-memory CSharpCompilation
/// path the per-builder unit tests use). Each test pins one of the
/// shapes SamplePdb deliberately carries so a regression in any
/// single capture path surfaces with a focused failure.
/// </summary>
public class SamplePdbWalkerIntegrationTests
{
    /// <summary>Walking the fixture surfaces every expected public type.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task WalkSurfacesAllExpectedTypes()
    {
        var catalog = WalkSamplePdb();

        var fullNames = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < catalog.Types.Length; i++)
        {
            fullNames.Add(catalog.Types[i].FullName);
        }

        // FullName captures the bare type name + namespace; generic
        // arity is exposed via Arity, not decorated into FullName.
        // Nested types likewise surface with their own simple name
        // under the containing namespace, not the outer-type-prefixed
        // form.
        await Assert.That(fullNames).Contains("SamplePdb.SamplePdbAnchor");
        await Assert.That(fullNames).Contains("SamplePdb.PlainInstance");
        await Assert.That(fullNames).Contains("SamplePdb.Overloads");
        await Assert.That(fullNames).Contains("SamplePdb.GenericContainer");
        await Assert.That(fullNames).Contains("SamplePdb.Asynchronous");
        await Assert.That(fullNames).Contains("SamplePdb.LocalFunctions");
        await Assert.That(fullNames).Contains("SamplePdb.Lambdas");
        await Assert.That(fullNames).Contains("SamplePdb.Player");
        await Assert.That(fullNames).Contains("SamplePdb.Point");
        await Assert.That(fullNames).Contains("SamplePdb.WithProperties");
        await Assert.That(fullNames).Contains("SamplePdb.WithOperator");
        await Assert.That(fullNames).Contains("SamplePdb.Extensions");
        await Assert.That(fullNames).Contains("SamplePdb.Outer");
        await Assert.That(fullNames).Contains("SamplePdb.Outer.Nested");
    }

    /// <summary>Compiler-generated display classes (lambdas, async state machines, local functions) never appear in the catalog.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task WalkFiltersCompilerGeneratedTypes()
    {
        var catalog = WalkSamplePdb();

        for (var i = 0; i < catalog.Types.Length; i++)
        {
            var name = catalog.Types[i].Name;
            await Assert.That(name.Contains('<') || name.Contains('>'))
                .IsFalse()
                .Because($"Compiler-generated type leaked into catalog: {catalog.Types[i].FullName}");
        }
    }

    /// <summary>The Overloads type carries all three Run overloads as distinct members.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task OverloadedMethodsAreCapturedSeparately()
    {
        var overloads = FindObjectType(WalkSamplePdb(), "SamplePdb.Overloads");

        var runOverloads = new List<ApiMember>();
        for (var i = 0; i < overloads.Members.Length; i++)
        {
            if (overloads.Members[i].Name == "Run")
            {
                runOverloads.Add(overloads.Members[i]);
            }
        }

        await Assert.That(runOverloads.Count).IsEqualTo(3);

        // Each overload has a distinct uid (M:...Run, M:...Run(System.Int32), M:...Run(System.Int32,System.Int32)).
        var uids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in runOverloads)
        {
            await Assert.That(uids.Add(member.Uid)).IsTrue();
        }
    }

    /// <summary>GenericContainer&lt;T&gt; carries its open-generic arity and the Wrap method's own type parameter.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GenericTypeAndMethodParametersAreCaptured()
    {
        var generic = FindObjectType(WalkSamplePdb(), "SamplePdb.GenericContainer");

        await Assert.That(generic.Arity).IsEqualTo(1);

        var wrap = FindMember(generic, "Wrap");
        await Assert.That(wrap.TypeParameters.Length).IsEqualTo(1);
        await Assert.That(wrap.TypeParameters[0]).IsEqualTo("TOther");
    }

    /// <summary>Async methods land in the catalog with their declared name (state machine doesn't leak).</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AsyncMethodsLandWithDeclaredName()
    {
        var asynchronous = FindObjectType(WalkSamplePdb(), "SamplePdb.Asynchronous");

        var runAsync = FindMember(asynchronous, "RunAsync");
        await Assert.That(runAsync.Kind).IsEqualTo(ApiMemberKind.Method);
    }

    /// <summary>Records expose their primary constructor parameters as members on the type.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task RecordPrimaryConstructorParametersAreVisible()
    {
        var player = FindObjectType(WalkSamplePdb(), "SamplePdb.Player");

        var memberNames = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < player.Members.Length; i++)
        {
            memberNames.Add(player.Members[i].Name);
        }

        await Assert.That(memberNames).Contains("Name");
        await Assert.That(memberNames).Contains("Score");
    }

    /// <summary>Classic <c>this T</c> extension methods are flagged on the static host.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ExtensionMethodIsFlagged()
    {
        var extensions = FindObjectType(WalkSamplePdb(), "SamplePdb.Extensions");

        var doubled = FindMember(extensions, "Doubled");
        await Assert.That(doubled.IsExtension).IsTrue();
        await Assert.That(extensions.IsStatic).IsTrue();
    }

    /// <summary>Operator overloads are captured with the operator-method kind.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task OperatorOverloadIsCaptured()
    {
        var withOp = FindObjectType(WalkSamplePdb(), "SamplePdb.WithOperator");

        var addition = FindMember(withOp, "op_Addition");
        await Assert.That(addition.Kind).IsEqualTo(ApiMemberKind.Operator);
    }

    /// <summary>Properties surface with the property kind, including read-only and indexer shapes.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task PropertiesAndIndexerAreCaptured()
    {
        var props = FindObjectType(WalkSamplePdb(), "SamplePdb.WithProperties");

        var name = FindMember(props, "Name");
        await Assert.That(name.Kind).IsEqualTo(ApiMemberKind.Property);

        var length = FindMember(props, "Length");
        await Assert.That(length.Kind).IsEqualTo(ApiMemberKind.Property);

        // The indexer surfaces as its CLR metadata name "this[]".
        var indexer = FindMember(props, "this[]");
        await Assert.That(indexer.Kind).IsEqualTo(ApiMemberKind.Property);
    }

    /// <summary>Nested types are captured under their containing type's namespace + outer-name path.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task NestedTypeIsCaptured()
    {
        var catalog = WalkSamplePdb();

        // Nested types pick up the outer-type chain in FullName so
        // they don't collide with a sibling top-level type of the
        // same simple name; Name itself stays the unqualified form.
        var nested = FindObjectType(catalog, "SamplePdb.Outer.Nested");
        await Assert.That(nested.Name).IsEqualTo("Nested");
    }

    /// <summary>Delegate types are captured with the dedicated delegate kind and surface their signature.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task DelegateTypeCapturesSignature()
    {
        var catalog = WalkSamplePdb();

        var binaryOp = FindDelegateType(catalog, "SamplePdb.SampleBinaryOp");
        await Assert.That(binaryOp.Invoke.Parameters.Length).IsEqualTo(2);
        await Assert.That(binaryOp.Invoke.Parameters[0].Name).IsEqualTo("left");
        await Assert.That(binaryOp.Invoke.Parameters[1].Name).IsEqualTo("right");
        await Assert.That(binaryOp.Invoke.ReturnType).IsNotNull();
        await Assert.That(binaryOp.Invoke.ReturnType!.DisplayName).Contains("int");
    }

    /// <summary>Generic delegates carry their arity and type-parameter list on the signature.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task GenericDelegateCapturesTypeParameters()
    {
        var predicate = FindDelegateType(WalkSamplePdb(), "SamplePdb.SamplePredicate");

        await Assert.That(predicate.Arity).IsEqualTo(1);
        await Assert.That(predicate.Invoke.TypeParameters.Length).IsEqualTo(1);
        await Assert.That(predicate.Invoke.TypeParameters[0]).IsEqualTo("T");
    }

    /// <summary>Plain enums are captured with their members as <see cref="ApiEnumValue"/>s in declaration order.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EnumCapturesMembers()
    {
        var severity = FindEnumType(WalkSamplePdb(), "SamplePdb.SampleSeverity");

        await Assert.That(severity.Values.Length).IsEqualTo(3);
        await Assert.That(severity.Values[0].Name).IsEqualTo("Info");
        await Assert.That(severity.Values[1].Name).IsEqualTo("Warning");
        await Assert.That(severity.Values[2].Name).IsEqualTo("Error");
    }

    /// <summary>Flags enums carry the explicit underlying-type display name and every declared member.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task FlagsEnumCapturesUnderlyingTypeAndMembers()
    {
        var flags = FindEnumType(WalkSamplePdb(), "SamplePdb.SampleFlags");

        await Assert.That(flags.UnderlyingType).IsNotNull();
        await Assert.That(flags.UnderlyingType!.DisplayName).Contains("byte");

        var memberNames = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < flags.Values.Length; i++)
        {
            memberNames.Add(flags.Values[i].Name);
        }

        await Assert.That(memberNames).Contains("None");
        await Assert.That(memberNames).Contains("Read");
        await Assert.That(memberNames).Contains("Write");
        await Assert.That(memberNames).Contains("All");
    }

    /// <summary>
    /// Attributes with a positional argument plus named arguments
    /// preserve every value in source order (positional first, named
    /// after) and render every TypedConstant shape — primitive,
    /// typeof, enum, and array — through the walker's FormatConstant
    /// switch arms.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task AttributeArgumentsAreCaptured()
    {
        var target = FindObjectType(WalkSamplePdb(), "SamplePdb.AttributedTarget");

        ApiAttribute? marker = null;
        for (var i = 0; i < target.Attributes.Length; i++)
        {
            if (target.Attributes[i].DisplayName == "Marker")
            {
                marker = target.Attributes[i];
                break;
            }
        }

        await Assert.That(marker).IsNotNull();
        await Assert.That(marker!.Arguments.Length).IsEqualTo(6);

        // Positional first — the constructor's string parameter.
        await Assert.That(marker.Arguments[0].Name).IsNull();
        await Assert.That(marker.Arguments[0].Value).IsEqualTo("\"primary\"");

        // Named arguments retain declaration order.
        var byName = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 1; i < marker.Arguments.Length; i++)
        {
            byName[marker.Arguments[i].Name!] = marker.Arguments[i].Value;
        }

        await Assert.That(byName["Priority"]).IsEqualTo("7");
        await Assert.That(byName["Tag"]).IsEqualTo("\"fixture\"");

        // typeof(...) renders through the Type-kind branch.
        await Assert.That(byName["TargetType"]).IsEqualTo("typeof(SampleShape)");

        // Enums render as TypeName.MemberName via the Enum-kind branch
        // — but TypedConstant carries the underlying integer value,
        // so the walker formats with the numeric literal rather than
        // the symbolic member name. Pinned here so any improvement
        // (resolving the symbolic name) lands as a deliberate change.
        await Assert.That(byName["Severity"]).IsEqualTo("SampleSeverity.1");

        // Arrays render as [v, v, v] through the Array-kind branch.
        await Assert.That(byName["Tags"]).IsEqualTo("[\"alpha\", \"beta\"]");
    }

    /// <summary>Closed-hierarchy unions (any type implementing <c>System.Runtime.CompilerServices.IUnion</c>) surface as <see cref="ApiUnionType"/> with their case classes captured.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task UnionTypeCapturesCases()
    {
        var shape = FindUnionType(WalkSamplePdb(), "SamplePdb.SampleShape");

        var caseNames = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < shape.Cases.Length; i++)
        {
            caseNames.Add(shape.Cases[i].DisplayName);
        }

        await Assert.That(caseNames).Contains("SampleCircle");
        await Assert.That(caseNames).Contains("SampleSquare");
    }

    /// <summary>Assembly metadata + TFM are stamped on every emitted type.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task EveryTypeCarriesAssemblyAndTfm()
    {
        var catalog = WalkSamplePdb();

        await Assert.That(catalog.Tfm).IsEqualTo("net10.0");
        await Assert.That(catalog.Types.Length).IsGreaterThan(0);
        for (var i = 0; i < catalog.Types.Length; i++)
        {
            await Assert.That(catalog.Types[i].AssemblyName).IsEqualTo("SamplePdb");
        }
    }

    /// <summary>
    /// Compiles a tiny consumer that references the SamplePdb fixture
    /// DLL on disk, then runs the walker over the SamplePdb assembly
    /// symbol the resulting Compilation surfaces. Mirrors the
    /// production path that loads a real package's assembly via
    /// MetadataReference.CreateFromFile.
    /// </summary>
    /// <returns>The walked catalog.</returns>
    private static ApiCatalog WalkSamplePdb()
    {
        var samplePath = typeof(SamplePdbAnchor).Assembly.Location;

        // Pull in the runtime BCL alongside the fixture so symbol
        // resolution sees System.Object, IAsyncStateMachine, etc.
        List<MetadataReference> references = [MetadataReference.CreateFromFile(samplePath)];
        foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!loaded.IsDynamic && loaded.Location is { Length: > 0 } location)
            {
                references.Add(MetadataReference.CreateFromFile(location));
            }
        }

        var compilation = CSharpCompilation.Create(
            "SamplePdbConsumer",
            syntaxTrees: [],
            references,
            new(OutputKind.DynamicallyLinkedLibrary));

        var samplePdbAssembly = FindAssemblySymbol(compilation, "SamplePdb");

        var walker = new SymbolWalker();
        return walker.Walk("net10.0", samplePdbAssembly, compilation, new NullSourceLinkResolver());
    }

    /// <summary>Returns the assembly symbol with the given short name from the compilation's referenced assemblies.</summary>
    /// <param name="compilation">The compilation under inspection.</param>
    /// <param name="shortName">The short assembly name.</param>
    /// <returns>The matching assembly symbol.</returns>
    private static IAssemblySymbol FindAssemblySymbol(Microsoft.CodeAnalysis.Compilation compilation, string shortName)
    {
        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly && assembly.Name == shortName)
            {
                return assembly;
            }
        }

        throw new InvalidOperationException($"Assembly '{shortName}' not found in compilation references.");
    }

    /// <summary>Returns the catalog's <see cref="ApiObjectType"/> with the given full name.</summary>
    /// <param name="catalog">The walked catalog.</param>
    /// <param name="fullName">Full name to look up.</param>
    /// <returns>The matching type.</returns>
    private static ApiObjectType FindObjectType(ApiCatalog catalog, string fullName) =>
        FindType(catalog, fullName, static type => type is ApiObjectType, nameof(ApiObjectType)) as ApiObjectType
        ?? throw new InvalidOperationException($"ApiObjectType '{fullName}' not in catalog.");

    /// <summary>Returns the catalog's <see cref="ApiDelegateType"/> with the given full name.</summary>
    /// <param name="catalog">The walked catalog.</param>
    /// <param name="fullName">Full name to look up.</param>
    /// <returns>The matching type.</returns>
    private static ApiDelegateType FindDelegateType(ApiCatalog catalog, string fullName) =>
        FindType(catalog, fullName, static type => type is ApiDelegateType, nameof(ApiDelegateType)) as ApiDelegateType
        ?? throw new InvalidOperationException($"ApiDelegateType '{fullName}' not in catalog.");

    /// <summary>Returns the catalog's <see cref="ApiEnumType"/> with the given full name.</summary>
    /// <param name="catalog">The walked catalog.</param>
    /// <param name="fullName">Full name to look up.</param>
    /// <returns>The matching type.</returns>
    private static ApiEnumType FindEnumType(ApiCatalog catalog, string fullName) =>
        FindType(catalog, fullName, static type => type is ApiEnumType, nameof(ApiEnumType)) as ApiEnumType
        ?? throw new InvalidOperationException($"ApiEnumType '{fullName}' not in catalog.");

    /// <summary>Returns the catalog's <see cref="ApiUnionType"/> with the given full name.</summary>
    /// <param name="catalog">The walked catalog.</param>
    /// <param name="fullName">Full name to look up.</param>
    /// <returns>The matching type.</returns>
    private static ApiUnionType FindUnionType(ApiCatalog catalog, string fullName) =>
        FindType(catalog, fullName, static type => type is ApiUnionType, nameof(ApiUnionType)) as ApiUnionType
        ?? throw new InvalidOperationException($"ApiUnionType '{fullName}' not in catalog.");

    /// <summary>Returns the catalog entry of the requested concrete type with the given full name.</summary>
    /// <param name="catalog">The walked catalog.</param>
    /// <param name="fullName">Full name to look up.</param>
    /// <param name="matchesType">Predicate selecting the desired concrete type.</param>
    /// <param name="typeName">Friendly type name for the failure message.</param>
    /// <returns>The matching type.</returns>
    private static ApiType FindType(ApiCatalog catalog, string fullName, Predicate<ApiType> matchesType, string typeName)
    {
        for (var i = 0; i < catalog.Types.Length; i++)
        {
            if (catalog.Types[i].FullName == fullName && matchesType(catalog.Types[i]))
            {
                return catalog.Types[i];
            }
        }

        throw new InvalidOperationException($"{typeName} '{fullName}' not in catalog.");
    }

    /// <summary>Returns the first member with the given metadata name.</summary>
    /// <param name="type">Owning type.</param>
    /// <param name="name">Metadata name.</param>
    /// <returns>The matching member.</returns>
    private static ApiMember FindMember(ApiObjectType type, string name)
    {
        for (var i = 0; i < type.Members.Length; i++)
        {
            if (type.Members[i].Name == name)
            {
                return type.Members[i];
            }
        }

        throw new InvalidOperationException($"Member '{name}' not on type '{type.FullName}'.");
    }

    /// <summary>SourceLink resolver that returns null for every symbol — keeps the integration test scoped to the walker, not the SourceLink chain.</summary>
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
