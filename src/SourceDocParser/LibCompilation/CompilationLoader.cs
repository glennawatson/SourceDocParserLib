// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using ICSharpCode.Decompiler.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging.Abstractions;
using static Microsoft.CodeAnalysis.MetadataImportOptions;
using static Microsoft.CodeAnalysis.OutputKind;

namespace SourceDocParser.LibCompilation;

/// <summary>Loads a compiled .NET assembly into a Roslyn <see cref="CSharpCompilation"/>.</summary>
[System.Diagnostics.DebuggerDisplay("CompilationLoader: {_logger}")]
public sealed partial class CompilationLoader : ICompilationLoader
{
    /// <summary>Gets a bootstrap syntax tree included in every compilation.</summary>
    /// <remarks>
    /// Ensures Roslyn has a primary source to anchor the assembly identity,
    /// allowing it to bind core types like <c>System.Object</c>.
    /// </remarks>
    private static readonly SyntaxTree[] _bootstrap =
    [
        CSharpSyntaxTree.ParseText(
            """
            class Bootstrap
            {
                public static void Main(string[] args) { }
            }
            """),
    ];

    /// <summary>Logger for resolver progress and reference-resolution warnings.</summary>
    private readonly ILogger _logger;

    /// <summary>Cache of <see cref="MetadataReference"/> instances by absolute path. Owned by this loader.</summary>
    private readonly MetadataReferenceCache _referenceCache;

    /// <summary>Initializes a new instance of the <see cref="CompilationLoader"/> class using a no-op logger.</summary>
    public CompilationLoader()
        : this(null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="CompilationLoader"/> class.</summary>
    /// <param name="logger">Logger for resolver progress and reference-resolution warnings; <see cref="NullLogger.Instance"/> when null.</param>
    public CompilationLoader(ILogger? logger)
    {
        _logger = logger ?? NullLogger.Instance;
        _referenceCache = new(_logger);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public (CSharpCompilation Compilation, IAssemblySymbol Assembly) Load(
        string assemblyPath,
        Dictionary<string, string> fallbackReferences) =>
        Load(assemblyPath, fallbackReferences, false);

    /// <inheritdoc />
    public (CSharpCompilation Compilation, IAssemblySymbol Assembly) Load(
        string assemblyPath,
        Dictionary<string, string> fallbackReferences,
        bool includePrivateMembers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        ArgumentNullException.ThrowIfNull(fallbackReferences);

        var resolved = ResolveTransitiveReferences(assemblyPath, fallbackReferences, _logger);

        var references = new List<MetadataReference>(resolved.Count + 1);
        for (var i = 0; i < resolved.Count; i++)
        {
            references.Add(_referenceCache.Get(resolved[i]));
        }

        var primary = _referenceCache.Get(assemblyPath);
        references.Add(primary);

        var compilation = CSharpCompilation.Create(
            assemblyName: null,
            syntaxTrees: _bootstrap,
            references: references,
            options: new(
                outputKind: DynamicallyLinkedLibrary,
                metadataImportOptions: includePrivateMembers ? All : Public));

        var assembly = (IAssemblySymbol)compilation.GetAssemblyOrModuleSymbol(primary)!;
        return (compilation, assembly);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose() => _referenceCache.Dispose();

    /// <summary>Includes selected compile assets and resolves any remaining assembly references for a DLL.</summary>
    /// <param name="assemblyPath">The absolute path to the primary DLL.</param>
    /// <param name="fallbackIndex">The selected compile assets, which take precedence over automatic resolution.</param>
    /// <param name="logger">Logger for resolver progress and unresolved-reference warnings.</param>
    /// <returns>A list of absolute paths to resolved transitive references.</returns>
    internal static List<string> ResolveTransitiveReferences(
        string assemblyPath,
        Dictionary<string, string> fallbackIndex,
        ILogger logger)
    {
        using var primary = new PEFile(assemblyPath);
        var targetFramework = primary.DetectTargetFrameworkId();
        var primaryName = primary.Metadata.GetString(primary.Metadata.GetAssemblyDefinition().Name);
        var context = new ResolutionContext
        {
            Resolver = new(assemblyPath, throwOnError: false, targetFramework),
            SelectedReferences = [with(fallbackIndex.Count, StringComparer.OrdinalIgnoreCase)],
            VisitedNames = [with(StringComparer.OrdinalIgnoreCase), primaryName],
            ResolvedPaths = [with(fallbackIndex.Count)],
            Pending = new(),
            Logger = logger,
            AssemblyPath = assemblyPath,
            TargetFramework = targetFramework,
        };

        foreach (var file in fallbackIndex.Values)
        {
            using var selected = new PEFile(file);
            var name = selected.Metadata.GetString(selected.Metadata.GetAssemblyDefinition().Name);
            if (name.Equals(primaryName, StringComparison.OrdinalIgnoreCase) || !context.SelectedReferences.TryAdd(name, file))
            {
                continue;
            }

            LogResolvedReference(logger, name, file);
            context.ResolvedPaths.Add(file);
        }

        context.Pending.Push(assemblyPath);
        while (context.Pending.TryPop(out var currentPath))
        {
            using var current = new PEFile(currentPath);
            ProcessReferences(current, ref context);
        }

        return context.ResolvedPaths;
    }

    /// <summary>Processes all assembly references of a single PE file.</summary>
    /// <param name="current">The PE file to process.</param>
    /// <param name="context">The resolution context.</param>
    private static void ProcessReferences(PEFile current, ref ResolutionContext context)
    {
        foreach (var reference in current.AssemblyReferences)
        {
            if (!context.VisitedNames.Add(reference.Name))
            {
                continue;
            }

            if (!context.SelectedReferences.TryGetValue(reference.Name, out var file))
            {
                file = context.Resolver.FindAssemblyFile(reference);
                if (file is null)
                {
                    LogUnresolvedReference(context.Logger, reference.ToString(), current.FileName, context.AssemblyPath, context.TargetFramework);
                    continue;
                }

                LogResolvedReference(context.Logger, reference.Name, file);
                context.ResolvedPaths.Add(file);
            }

            context.Pending.Push(file);
        }
    }

    /// <summary>Logs a successful assembly reference resolution.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="reference">Resolved assembly simple name.</param>
    /// <param name="file">Absolute path the reference resolved to.</param>
    [LoggerMessage(Level = LogLevel.Trace, Message = "  resolved {Reference} -> {File}")]
    private static partial void LogResolvedReference(ILogger logger, string reference, string file);

    /// <summary>Logs an assembly reference missing from both the selected assets and automatic resolution.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="reference">Reference identity (name + version).</param>
    /// <param name="assembly">Path of the assembly that requested it.</param>
    /// <param name="root">Path of the documentation root assembly.</param>
    /// <param name="targetFramework">The root assembly's target framework.</param>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Unable to resolve assembly reference '{Reference}' required by '{Assembly}' while documenting '{Root}' ({TargetFramework}). " +
                  "Check the restored compile assets and installed framework reference packs.")]
    private static partial void LogUnresolvedReference(ILogger logger, string reference, string assembly, string root, string targetFramework);

    /// <summary>Context for transitive assembly reference resolution.</summary>
    private readonly ref struct ResolutionContext
    {
        /// <summary>Gets the assembly resolver.</summary>
        public required UniversalAssemblyResolver Resolver { get; init; }

        /// <summary>Gets the selected compile assets by simple assembly name.</summary>
        public required Dictionary<string, string> SelectedReferences { get; init; }

        /// <summary>Gets the assembly names visited through the root's dependency closure.</summary>
        public required HashSet<string> VisitedNames { get; init; }

        /// <summary>Gets the list of resolved assembly file paths.</summary>
        public required List<string> ResolvedPaths { get; init; }

        /// <summary>Gets the assembly paths pending reference resolution.</summary>
        public required Stack<string> Pending { get; init; }

        /// <summary>Gets the logger for resolution progress.</summary>
        public required ILogger Logger { get; init; }

        /// <summary>Gets the path of the primary assembly being documented.</summary>
        public required string AssemblyPath { get; init; }

        /// <summary>Gets the primary assembly's target framework.</summary>
        public required string TargetFramework { get; init; }
    }
}
