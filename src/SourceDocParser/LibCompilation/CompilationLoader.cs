// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using ICSharpCode.Decompiler.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging.Abstractions;
using static Microsoft.CodeAnalysis.MetadataImportOptions;
using static Microsoft.CodeAnalysis.OutputKind;

namespace SourceDocParser.LibCompilation;

/// <summary>
/// Loads a compiled .NET assembly into a Roslyn <see cref="CSharpCompilation"/>.
/// </summary>
public sealed partial class CompilationLoader : ICompilationLoader
{
    /// <summary>Major version of a stub assembly.</summary>
    private const int StubMajorVersion = 0;

    /// <summary>Minor version of a stub assembly.</summary>
    private const int StubMinorVersion = 0;

    /// <summary>Build version of a stub assembly.</summary>
    private const int StubBuildVersion = 0;

    /// <summary>Revision version of a stub assembly.</summary>
    private const int StubRevisionVersion = 0;

    /// <summary>
    /// Gets a bootstrap syntax tree included in every compilation.
    /// </summary>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="CompilationLoader"/> class
    /// using a no-op logger.
    /// </summary>
    public CompilationLoader()
        : this(null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CompilationLoader"/> class.
    /// </summary>
    /// <param name="logger">Logger for resolver progress and reference-resolution warnings; <see cref="NullLogger.Instance"/> when null.</param>
    public CompilationLoader(ILogger? logger)
    {
        _logger = logger ?? NullLogger.Instance;
        _referenceCache = new(_logger);
    }

    /// <inheritdoc />
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
    public void Dispose() => _referenceCache.Dispose();

    /// <summary>
    /// Resolves the closure of assembly references for a DLL.
    /// </summary>
    /// <param name="assemblyPath">The absolute path to the primary DLL.</param>
    /// <param name="fallbackIndex">The fallback map for resolver misses.</param>
    /// <param name="logger">Logger for resolver progress and unresolved-reference warnings.</param>
    /// <returns>A list of absolute paths to resolved transitive references.</returns>
    /// <remarks>
    /// Uses an iterative stack-based walk to avoid recursion overhead. PEFile
    /// instances are disposed immediately after processing. Skips 0.0.0.0
    /// version references as they typically represent compiler stubs.
    /// </remarks>
    internal static List<string> ResolveTransitiveReferences(
        string assemblyPath,
        Dictionary<string, string> fallbackIndex,
        ILogger logger)
    {
        using var primary = new PEFile(assemblyPath);
        var context = new ResolutionContext
        {
            Resolver = new(assemblyPath, throwOnError: false, primary.DetectTargetFrameworkId()),
            FallbackIndex = fallbackIndex,
            ResolvedNames = new(StringComparer.Ordinal),
            ResolvedPaths = [],
            Pending = new(),
            Logger = logger,
            AssemblyName = Path.GetFileName(assemblyPath)
        };

        context.Pending.Push(new(assemblyPath));

        try
        {
            while (context.Pending.TryPop(out var current))
            {
                try
                {
                    ProcessReferences(current, ref context);
                }
                finally
                {
                    current.Dispose();
                }
            }
        }
        catch
        {
            DisposePending(context.Pending);
            throw;
        }

        return context.ResolvedPaths;
    }

    /// <summary>
    /// Processes all assembly references of a single PE file.
    /// </summary>
    /// <param name="current">The PE file to process.</param>
    /// <param name="context">The resolution context.</param>
    private static void ProcessReferences(PEFile current, ref ResolutionContext context)
    {
        foreach (var reference in current.AssemblyReferences)
        {
            if (reference.Version is { Major: StubMajorVersion, Minor: StubMinorVersion, Build: StubBuildVersion, Revision: StubRevisionVersion })
            {
                continue;
            }

            var file = context.Resolver.FindAssemblyFile(reference);
            if (file is null && !context.FallbackIndex.TryGetValue(reference.Name, out file))
            {
                LogUnresolvedReference(context.Logger, reference.ToString(), context.AssemblyName);
                continue;
            }

            if (!context.ResolvedNames.Add(reference.Name))
            {
                continue;
            }

            LogResolvedReference(context.Logger, reference.Name, file);
            context.ResolvedPaths.Add(file);
            context.Pending.Push(new(file));
        }
    }

    /// <summary>
    /// Disposes all PE files remaining in the pending stack.
    /// </summary>
    /// <param name="pending">The stack of pending PE files.</param>
    private static void DisposePending(Stack<PEFile> pending)
    {
        while (pending.TryPop(out var p))
        {
            p.Dispose();
        }
    }

    /// <summary>Logs a successful assembly reference resolution.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="reference">Resolved assembly simple name.</param>
    /// <param name="file">Absolute path the reference resolved to.</param>
    [LoggerMessage(Level = LogLevel.Trace, Message = "  resolved {Reference} -> {File}")]
    private static partial void LogResolvedReference(ILogger logger, string reference, string file);

    /// <summary>Logs an assembly reference the resolver and fallback index could not locate.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="reference">Reference identity (name + version).</param>
    /// <param name="assembly">Filename of the assembly that requested it.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Unable to resolve assembly reference '{Reference}' for {Assembly}")]
    private static partial void LogUnresolvedReference(ILogger logger, string reference, string assembly);

    /// <summary>
    /// Context for transitive assembly reference resolution.
    /// </summary>
    private readonly ref struct ResolutionContext
    {
        /// <summary>Gets the assembly resolver.</summary>
        public required UniversalAssemblyResolver Resolver { get; init; }

        /// <summary>Gets the fallback index for assembly resolution.</summary>
        public required Dictionary<string, string> FallbackIndex { get; init; }

        /// <summary>Gets the set of already resolved assembly names.</summary>
        public required HashSet<string> ResolvedNames { get; init; }

        /// <summary>Gets the list of resolved assembly file paths.</summary>
        public required List<string> ResolvedPaths { get; init; }

        /// <summary>Gets the stack of PE files pending processing.</summary>
        public required Stack<PEFile> Pending { get; init; }

        /// <summary>Gets the logger for resolution progress.</summary>
        public required ILogger Logger { get; init; }

        /// <summary>Gets the name of the primary assembly being processed.</summary>
        public required string AssemblyName { get; init; }
    }
}
