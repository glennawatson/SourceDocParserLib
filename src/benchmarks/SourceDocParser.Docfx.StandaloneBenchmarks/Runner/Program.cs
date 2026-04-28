// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
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

        if (args.Length > 0)
        {
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
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

        Console.WriteLine();
        Console.WriteLine("SourceDocParser MetadataExtractor + DocfxYamlEmitter -- one pass per TFM");
        Console.WriteLine();
        Console.WriteLine("| TFM      | Wall time | Allocated |");
        Console.WriteLine("|----------|----------:|----------:|");

        for (var i = 0; i < Tfms.Length; i++)
        {
            var tfm = Tfms[i];
            bench.Tfm = tfm;
            bench.IterationSetup();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var allocBefore = GC.GetTotalAllocatedBytes(precise: true);
            var sw = Stopwatch.StartNew();
            await bench.RunAsync().ConfigureAwait(false);
            sw.Stop();
            var allocAfter = GC.GetTotalAllocatedBytes(precise: true);

            Console.WriteLine(
                $"| {tfm,-TfmColumnWidth} | {sw.Elapsed.TotalSeconds,NumericColumnWidth:F2} s | {(allocAfter - allocBefore) / BytesPerKiB / BytesPerKiB,NumericColumnWidth:F2} MB |");
        }

        Console.WriteLine();
        Console.WriteLine($"YAML output retained at: {bench.ScratchRootForInspection}");
    }
}
