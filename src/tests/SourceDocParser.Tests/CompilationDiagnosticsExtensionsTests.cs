// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SourceDocParser.Tests;

/// <summary>
/// Tests for <see cref="CompilationDiagnosticsExtensions.ReportDiagnostics"/>.
/// </summary>
public class CompilationDiagnosticsExtensionsTests
{
    /// <summary>Gets the runtime BCL assemblies as <see cref="MetadataReference"/>s so synthetic compilations resolve System.Object.</summary>
    private static List<MetadataReference> BclReferences { get; } =
    [
        .. AppDomain.CurrentDomain.GetAssemblies()
            .Where(static a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(static a => MetadataReference.CreateFromFile(a.Location)),
    ];

    /// <summary>
    /// A compilation with no errors returns false.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReportDiagnosticsReturnsFalseWhenNoErrors()
    {
        var compilation = CSharpCompilation.Create(
            "Clean",
            [CSharpSyntaxTree.ParseText("public class A { }")],
            BclReferences);

        var hasErrors = compilation.ReportDiagnostics(NullLogger.Instance);

        await Assert.That(hasErrors).IsFalse();
    }

    /// <summary>
    /// A compilation with declaration errors returns true.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReportDiagnosticsReturnsTrueWhenErrorsPresent()
    {
        // Duplicate type with the same name in the same namespace -> declaration error.
        var compilation = CSharpCompilation.Create(
            "Broken",
            [
                CSharpSyntaxTree.ParseText("public class Dup { }"),
                CSharpSyntaxTree.ParseText("public class Dup { }"),
            ],
            BclReferences);

        var hasErrors = compilation.ReportDiagnostics(NullLogger.Instance);

        await Assert.That(hasErrors).IsTrue();
    }

    /// <summary>
    /// Null arguments throw before any work.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReportDiagnosticsValidatesArguments()
    {
        var compilation = CSharpCompilation.Create("X");

        await Assert.That(() => CompilationDiagnosticsExtensions.ReportDiagnostics(null!, NullLogger.Instance))
            .Throws<ArgumentNullException>();
        await Assert.That(() => compilation.ReportDiagnostics(null!))
            .Throws<ArgumentNullException>();
    }

    /// <summary>Warning-only compilations log every warning but return false.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReportDiagnosticsLogsWarningsButReturnsFalse()
    {
        // CS0114: 'override' missing on a member that hides an inherited
        // virtual member is a declaration warning — perfect for exercising
        // the warning branch without tripping the error path.
        var compilation = CSharpCompilation.Create(
            "Warnings",
            [
                CSharpSyntaxTree.ParseText(
                    """
                    public class Base { public virtual void Run() { } }
                    public class Derived : Base { public void Run() { } }
                    """),
            ],
            BclReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var logger = new RecordingLogger();

        var hasErrors = compilation.ReportDiagnostics(logger);

        await Assert.That(hasErrors).IsFalse();
        await Assert.That(logger.WarningCount).IsGreaterThan(0);
    }

    /// <summary>The 20-error early-exit short-circuits before walking the entire diagnostic list.</summary>
    /// <returns>A task representing the test execution.</returns>
    [Test]
    public async Task ReportDiagnosticsStopsAfter20Errors()
    {
        // 25 duplicate types — each generates a CS0101 declaration error;
        // the helper must short-circuit at 20 and still return true.
        var trees = new SyntaxTree[25];
        for (var i = 0; i < trees.Length; i++)
        {
            trees[i] = CSharpSyntaxTree.ParseText("public class Dup { }");
        }

        var compilation = CSharpCompilation.Create("Many", trees, BclReferences);
        var logger = new RecordingLogger();

        var hasErrors = compilation.ReportDiagnostics(logger);

        await Assert.That(hasErrors).IsTrue();
        await Assert.That(logger.ErrorCount).IsEqualTo(20);
    }

    /// <summary>Logger that counts warning- and error-level entries so tests can assert on log volume.</summary>
    private sealed class RecordingLogger : ILogger
    {
        /// <summary>Gets the number of warning-level log entries observed.</summary>
        public int WarningCount { get; private set; }

        /// <summary>Gets the number of error-level log entries observed.</summary>
        public int ErrorCount { get; private set; }

        /// <inheritdoc />
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc />
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            switch (logLevel)
            {
                case LogLevel.Warning:
                    {
                        WarningCount++;
                        break;
                    }

                case LogLevel.Error:
                    {
                        ErrorCount++;
                        break;
                    }
            }
        }
    }
}
