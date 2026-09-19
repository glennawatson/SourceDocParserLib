// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;

namespace SourceDocParser.Benchmarks;

/// <summary>Captures allocation call stacks separately from pipeline phase timings.</summary>
[EventPipeProfiler(EventPipeProfile.GcVerbose, performExtraBenchmarksRun: false)]
public class PipelinePhaseAllocationBenchmarks : PipelinePhaseBenchmarks;
