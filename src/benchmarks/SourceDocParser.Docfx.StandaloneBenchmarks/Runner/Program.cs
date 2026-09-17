// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics;
using BenchmarkDotNet.Running;
using SourceDocParser.Docfx.StandaloneBenchmarks.Benchmarks;

namespace SourceDocParser.Docfx.StandaloneBenchmarks.Runner;

/// <summary>
/// Entry point for our docfx YAML emitter standalone benchmark.
/// With no args runs each TFM once via Stopwatch + allocated-bytes --
/// matches the dump mode of the sibling Docfx.StandaloneBenchmarks
/// runner so the README can put both numbers side by side. With args
/// defers to BenchmarkDotNet for full multi-iteration runs.
/// </summary>
public static class Program
{
    /// <summary>Column width for the left-aligned TFM cell in the dump table.</summary>
    private const int TfmColumnWidth = 8;

    /// <summary>Column width for right-aligned numeric cells in the dump table.</summary>
    private const int NumericColumnWidth = 7;

    /// <summary>Byte count in one kibibyte.</summary>
    private const double BytesPerKiB = 1024.0;

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
            _ = await BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).RunAsync(args).ConfigureAwait(false);
            return;
        }

        await RunDumpAsync().ConfigureAwait(false);
    }

    /// <summary>Runs each TFM once and prints wall time + allocated bytes; leaves YAML on disk for diffing.</summary>
    /// <returns>A task representing the asynchronous run.</returns>
    private static async Task RunDumpAsync()
    {
        var bench = new SourceDocParserDocfxLibraryBenchmark();
        await bench.GlobalSetupAsync().ConfigureAwait(false);

        var output = Console.Out;
        await output.WriteLineAsync().ConfigureAwait(false);
        await output.WriteLineAsync("SourceDocParser MetadataExtractor + DocfxYamlEmitter -- one pass per TFM").ConfigureAwait(false);
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
            await bench.RunAsync().ConfigureAwait(false);
            var elapsed = Stopwatch.GetElapsedTime(start);
            var allocAfter = GC.GetTotalAllocatedBytes(precise: true);

            var megabytes = (allocAfter - allocBefore) / BytesPerKiB / BytesPerKiB;
            await output.WriteLineAsync(
                $"| {tfm,-TfmColumnWidth} | {elapsed.TotalSeconds,NumericColumnWidth:F2} s | {megabytes,NumericColumnWidth:F2} MB |").ConfigureAwait(false);
        }

        await output.WriteLineAsync().ConfigureAwait(false);
        await output.WriteLineAsync($"YAML output retained at: {bench.ScratchRootForInspection}").ConfigureAwait(false);
    }
}
