// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics;
using BenchmarkDotNet.Running;
using Docfx.StandaloneBenchmarks.Benchmarks;

namespace Docfx.StandaloneBenchmarks.Runner;

/// <summary>
/// Entry point for the docfx-only standalone benchmark. With no args
/// it runs each TFM once via Stopwatch + allocated-bytes telemetry —
/// fast enough to land baseline numbers before docfx's verbose
/// resolver-warning spew turns a full BenchmarkDotNet sweep into an
/// hours-long affair. With args it defers to BenchmarkDotNet.
/// </summary>
public static class Program
{
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
        var bench = new DocfxLibraryBenchmark();
        bench.GlobalSetup();

        Console.WriteLine();
        Console.WriteLine("docfx GenerateManagedReferenceYamlFiles — one pass per TFM");
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
            await bench.GenerateManagedReferenceYaml().ConfigureAwait(false);
            sw.Stop();
            var allocAfter = GC.GetTotalAllocatedBytes(precise: true);

            Console.WriteLine($"| {tfm,-8} | {sw.Elapsed.TotalSeconds,7:F2} s | {(allocAfter - allocBefore) / 1024.0 / 1024.0,7:F2} MB |");
        }

        Console.WriteLine();
        Console.WriteLine($"YAML output retained at: {bench.WorkspaceForInspection}");
    }
}
