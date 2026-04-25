// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using ICSharpCode.Decompiler.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using static Microsoft.CodeAnalysis.MetadataImportOptions;
using static Microsoft.CodeAnalysis.OutputKind;

namespace SourceDocParser;

/// <summary>
/// Loads a compiled .NET assembly into a Roslyn Compilation.
/// </summary>
/// <remarks>
/// <para>
/// This class handles transitive assembly reference resolution using ICSharpCode.Decompiler's
/// <c>UniversalAssemblyResolver</c> and attaches XML documentation via a custom provider.
/// It is optimized for batch processing many assemblies per TFM by allowing callers to share
/// fallback indexes and metadata reference caches.
/// </para>
/// <para>
/// <b>Thread Safety:</b> This class is static and stateless, making it thread-safe for
/// concurrent calls from multiple threads. It is designed to be used within parallel
/// processing pipelines like <c>MetadataExtractor</c>.
/// </para>
/// </remarks>
internal static partial class CompilationLoader
{
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

    /// <summary>
    /// Loads an assembly and its transitive references into a compilation.
    /// </summary>
    /// <param name="assemblyPath">The absolute path to the DLL.</param>
    /// <param name="fallbackReferences">The fallback lookup for unresolved references.</param>
    /// <param name="referenceCache">The cache for metadata references.</param>
    /// <param name="logger">Logger for resolver progress and reference-resolution warnings.</param>
    /// <param name="includePrivateMembers">Whether to include non-public members.</param>
    /// <returns>A tuple containing the compilation and the primary assembly symbol.</returns>
    public static (CSharpCompilation Compilation, IAssemblySymbol Assembly) Load(
        string assemblyPath,
        Dictionary<string, string> fallbackReferences,
        MetadataReferenceCache referenceCache,
        ILogger logger,
        bool includePrivateMembers = false)
    {
        var resolved = ResolveTransitiveReferences(assemblyPath, fallbackReferences, logger);

        var references = new List<MetadataReference>(resolved.Count + 1);
        foreach (var path in resolved)
        {
            references.Add(referenceCache.Get(path));
        }

        var primary = referenceCache.Get(assemblyPath);
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

    /// <summary>
    /// Reports compilation diagnostics and checks for errors.
    /// </summary>
    /// <param name="compilation">The compilation to inspect.</param>
    /// <param name="logger">Logger to receive warning and error diagnostics.</param>
    /// <returns>True if any error-level diagnostics were found; otherwise false.</returns>
    public static bool ReportDiagnostics(this Compilation compilation, ILogger logger)
    {
        var errorCount = 0;

        foreach (var diagnostic in compilation.GetDeclarationDiagnostics())
        {
            if (diagnostic.IsSuppressed)
            {
                continue;
            }

            switch (diagnostic.Severity)
            {
                case DiagnosticSeverity.Warning:
                    {
                        LogDiagnosticWarning(logger, diagnostic.ToString());
                        break;
                    }

                case DiagnosticSeverity.Error:
                    {
                        LogDiagnosticError(logger, diagnostic.ToString());
                        if (++errorCount >= 20)
                        {
                            return true;
                        }

                        break;
                    }
            }
        }

        return errorCount > 0;
    }

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
    private static List<string> ResolveTransitiveReferences(
        string assemblyPath,
        Dictionary<string, string> fallbackIndex,
        ILogger logger)
    {
        using var primary = new PEFile(assemblyPath);
        var resolver = new UniversalAssemblyResolver(assemblyPath, throwOnError: false, primary.DetectTargetFrameworkId());
        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);

        var pending = new Stack<PEFile>();
        pending.Push(new(assemblyPath));

        try
        {
            while (pending.TryPop(out var current))
            {
                try
                {
                    foreach (var reference in current.AssemblyReferences)
                    {
                        if (reference.Version is { Major: 0, Minor: 0, Build: 0, Revision: 0 })
                        {
                            continue;
                        }

                        var file = resolver.FindAssemblyFile(reference);
                        if (file is null && !fallbackIndex.TryGetValue(reference.Name, out file))
                        {
                            LogUnresolvedReference(logger, reference.ToString(), Path.GetFileName(assemblyPath));
                            continue;
                        }

                        if (!resolved.TryAdd(reference.Name, file))
                        {
                            continue;
                        }

                        LogResolvedReference(logger, reference.Name, file);
                        pending.Push(new(file));
                    }
                }
                finally
                {
                    current.Dispose();
                }
            }
        }
        catch
        {
            while (pending.TryPop(out var p))
            {
                p.Dispose();
            }

            throw;
        }

        var results = new List<string>(resolved.Count);
        foreach (var pair in resolved)
        {
            results.Add(pair.Value);
        }

        return results;
    }

    /// <summary>Logs a Roslyn warning-level compilation diagnostic.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="diagnostic">Pre-formatted diagnostic text.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Compilation diagnostic (warning): {Diagnostic}")]
    private static partial void LogDiagnosticWarning(ILogger logger, string diagnostic);

    /// <summary>Logs a Roslyn error-level compilation diagnostic.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="diagnostic">Pre-formatted diagnostic text.</param>
    [LoggerMessage(Level = LogLevel.Error, Message = "Compilation diagnostic (error): {Diagnostic}")]
    private static partial void LogDiagnosticError(ILogger logger, string diagnostic);

    /// <summary>Logs an assembly reference the resolver and fallback index could not locate.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="reference">Reference identity (name + version).</param>
    /// <param name="assembly">Filename of the assembly that requested it.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Unable to resolve assembly reference '{Reference}' for {Assembly}")]
    private static partial void LogUnresolvedReference(ILogger logger, string reference, string assembly);

    /// <summary>Logs a successful assembly reference resolution.</summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="reference">Resolved assembly simple name.</param>
    /// <param name="file">Absolute path the reference resolved to.</param>
    [LoggerMessage(Level = LogLevel.Trace, Message = "  resolved {Reference} -> {File}")]
    private static partial void LogResolvedReference(ILogger logger, string reference, string file);
}
