// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;

namespace SourceDocParser.LibCompilation;

/// <summary>
/// Extension helpers that walk a <see cref="Compilation"/>'s declaration
/// diagnostics and forward them to an <see cref="ILogger"/>. Lives apart
/// from <see cref="CompilationLoader"/> because extension methods must be
/// declared in a non-generic static class and the loader is now an
/// instance type.
/// </summary>
public static partial class CompilationDiagnosticsExtensions
{
    /// <summary>
    /// The maximum number of errors to report before stopping.
    /// </summary>
    private const int MaxErrorCount = 20;

    /// <summary>
    /// Reports compilation declaration diagnostics to <paramref name="logger"/>.
    /// </summary>
    /// <param name="compilation">The compilation to inspect.</param>
    /// <param name="logger">Logger to receive warning and error diagnostics.</param>
    /// <returns>True when any error-level diagnostics were observed; otherwise false. Reporting stops after <see cref="MaxErrorCount"/> errors.</returns>
    public static bool ReportDiagnostics(this Compilation compilation, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(logger);

        var errorCount = 0;
        var diagnostics = compilation.GetDeclarationDiagnostics();
        for (var i = 0; i < diagnostics.Length; i++)
        {
            var diagnostic = diagnostics[i];
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
                        if (++errorCount >= MaxErrorCount)
                        {
                            return true;
                        }

                        break;
                    }
            }
        }

        return errorCount > 0;
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
}
