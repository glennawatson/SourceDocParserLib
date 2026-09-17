// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics;
using BenchmarkDotNet.Running;
using Docfx.StandaloneBenchmarks.Benchmarks;

namespace Docfx.StandaloneBenchmarks.Runner;

/// <summary>
/// Entry point for the docfx-only standalone benchmark. With no args
/// it runs each TFM once via Stopwatch + allocated-bytes telemetry --
/// fast enough to land baseline numbers before docfx's verbose
/// resolver-warning spew turns a full BenchmarkDotNet sweep into an
/// hours-long affair. With args it defers to BenchmarkDotNet.
/// </summary>
public static class Program
{
    /// <summary>Conversion factor from bytes to megabytes.</summary>
    private const double BytesToMegabytes = 1024.0 * 1024.0;

    /// <summary>Column width for the TFM in the output table.</summary>
    private const int TfmColumnWidth = 8;

    /// <summary>Column width for the time in the output table.</summary>
    private const int TimeColumnWidth = 7;

    /// <summary>Column width for the allocation size in the output table.</summary>
    private const int AllocationColumnWidth = 7;

    /// <summary>The TFM matrix the dump mode walks.</summary>
    private static readonly string[] Tfms = ["net8.0", "net9.0", "net10.0", "net472"];

    /// <summary>Entry point.</summary>
    /// <param name="args">If empty, runs the dump mode; otherwise forwarded to BenchmarkSwitcher.</param>
    /// <returns>A task representing the asynchronous run.</returns>
    public static async Task Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args is [_, ..])
        {
            _ = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
            return;
        }

        await RunDumpAsync().ConfigureAwait(false);
    }

    /// <summary>Runs each TFM once and prints wall time + allocated bytes; leaves YAML on disk for diffing.</summary>
    /// <returns>A task representing the asynchronous run.</returns>
    private static async Task RunDumpAsync()
    {
        var bench = new DocfxLibraryBenchmark();
        await bench.GlobalSetupAsync().ConfigureAwait(false);

        var output = Console.Out;
        await output.WriteLineAsync().ConfigureAwait(false);
        await output.WriteLineAsync("docfx GenerateManagedReferenceYamlFiles -- one pass per TFM").ConfigureAwait(false);
        await output.WriteLineAsync().ConfigureAwait(false);
        await output.WriteLineAsync("| TFM      | Wall time | Allocated |").ConfigureAwait(false);
        await output.WriteLineAsync("|----------|----------:|----------:|").ConfigureAwait(false);

        for (var i = 0; i < Tfms.Length; i++)
        {
            var tfm = Tfms[i];
            bench.Tfm = tfm;
            bench.IterationSetup();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var allocBefore = GC.GetTotalAllocatedBytes(precise: true);
            var start = Stopwatch.GetTimestamp();
            await bench.GenerateManagedReferenceYaml().ConfigureAwait(false);
            var elapsed = Stopwatch.GetElapsedTime(start);
            var allocAfter = GC.GetTotalAllocatedBytes(precise: true);

            await output.WriteLineAsync(
                $"| {tfm,-TfmColumnWidth} | {elapsed.TotalSeconds,TimeColumnWidth:F2} s | {(allocAfter - allocBefore) / BytesToMegabytes,AllocationColumnWidth:F2} MB |").ConfigureAwait(false);
        }

        await output.WriteLineAsync().ConfigureAwait(false);
        await output.WriteLineAsync($"YAML output retained at: {bench.WorkspaceForInspection}").ConfigureAwait(false);
    }
}
