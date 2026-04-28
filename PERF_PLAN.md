# Performance & Allocation Plan

Captured from a baseline BenchmarkDotNet run against the slim 3-package
NuGet fixture (Linux, .NET 10.0.4, AMD64 RyuJIT, AVX2). Use this as a
working backlog — pick items off in the suggested execution order
unless something else changes the priority.

## Baseline numbers

### End-to-end pipeline (slim fixture: 3 packages, 19 TFM groups)

| Bench | Mean | Allocated |
|---|---:|---:|
| `MetadataExtractor.RunAsync` | **1.48 s** | **554 MB** |
| `Discover` (network warm-cache) | 958 ms | 274 MB |
| `LoadAndWalk` | 522 ms | 253 MB |
| ↳ `LoadOnly` | 70 ms | 64 MB |
| ↳ `WalkOnly` | 136 ms | 77 MB |
| `Emit` | 151 ms | 39 MB |
| `Merge` (entire pipeline merge step) | 0.83 ms | 383 KB |
| `SourceLinkOnly` | 2.6 ms | 159 KB |

Discover + LoadAndWalk dominate: ~70 % of wall time, ~95 % of allocations.

### Per-call hot helpers

| Bench | Mean | Allocated |
|---|---:|---:|
| `TfmResolver.FindBestRefsTfmExactMatch` | 3.0 ns | 0 |
| `TfmResolver.FindBestRefsTfmFrameworkRefs` | 3.3 ns | 0 |
| `TfmResolver.FindBestRefsTfmMixedRefs` | 5.9 ns | 0 |
| `TfmResolver.FindBestRefsTfmPlatformSuffix` | 11.7 ns | 0 |
| `TfmResolver.FindBestRefsTfmNetstandardFallback` | 896 ns | 1.1 KB |
| `XmlDocToMarkdown.ConvertPlainSummary` | 24 ns | 176 B |
| `XmlDocToMarkdown.ConvertTaggedSummary` | 913 ns (38×) | 456 B |
| `XmlDocToMarkdown.ConvertCodeAndListSummary` | 1.24 µs (52×) | 440 B |

### Scaling benches

`TypeMerger.Merge`:

| Types | Mean | Allocated |
|---:|---:|---:|
| 100 | 65 µs | 171 KB |
| 600 | 115 µs | 358 KB |
| 2000 | 517 µs | 883 KB |

`Zensical TypePage` render (per-page):

| Types × Members | Mean | Allocated | per-page |
|---|---:|---:|---:|
| 100 × 5 | 79 µs | 288 KB | 0.79 µs / 2.9 KB |
| 100 × 30 | 266 µs | 763 KB | 2.66 µs / 7.6 KB |
| 600 × 5 | 482 µs | 1.7 MB | 0.80 µs / 2.9 KB |
| 600 × 30 | 1.75 ms | 4.6 MB | 2.92 µs / 7.6 KB |
| 2000 × 5 | 1.85 ms | 5.8 MB | 0.93 µs / 2.9 KB |
| 2000 × 30 | 7.63 ms | 15.3 MB | 3.82 µs / 7.6 KB |

Linear in both axes; ~3-4 µs and ~7-8 KB per type-page is the steady-state cost.

---

## Tier 1 — biggest bets (architectural, days+ of work, multi-x wins)

### 1. Streaming pipeline end-to-end

Today: Discover → Walk → Merge → Emit each hold their full output in
memory (peak ~554 MB on the slim fixture, projected GBs on a real
repo). Walk emits
`ConcurrentDictionary<TFM, ConcurrentBag<ApiType[]>>`; `Merge`
consumes the whole thing; `Emit` consumes the merged result.

Refactor to a streaming pipeline: as soon as a TFM catalog is walked,
push it into the `StreamingTypeMerger` (already exists, partially
wired). As soon as a merged type is finalised (no more incoming TFMs
can touch its UID), emit it. Use `Channel<T>` between stages with
bounded capacity. Realistic 5-10× peak-memory reduction; might also
unlock parallel Walk + Emit overlap.

### 2. Bypass Roslyn for the common-case walk

`PublicSurfaceProbe` already uses `System.Reflection.Metadata.MetadataReader`
for type-UID enumeration without spinning up a `CSharpCompilation`.
The walker proper still pays for full Roslyn (which is where most of
LoadAndWalk's 253 MB lives). For types where we only need
name/namespace/modifiers/base/interfaces/member signatures,
MetadataReader is sufficient. Roslyn would remain only for:

- XML-doc `<inheritdoc>` resolution chains
- type-forward chasing across DLLs
- attribute-argument extraction

Multi-week work but would likely halve LoadAndWalk's wall time and
allocations.

### 3. Compilation cache across compatible TFM siblings

Right now each `(TFM, primary DLL)` work item gets a fresh
`CSharpCompilation`. After the compatible-TFM fallback fix, sibling
TFMs (`net8.0` / `net6.0` / `netstandard2.0`) often share the same
reference set. Key compilations by
`(sorted-reference-paths-hash, includePrivate)` in a
`ConcurrentDictionary<…, Lazy<Compilation>>`. Roslyn compilations are
50-200 MB live each — reusing one across siblings could shave 30 %+
of LoadAndWalk allocations.

---

## Tier 2 — clear wins (hours-to-day each)

### 4. AppliesTo array interning

Post-merge, every `ApiType` carries an `AppliesTo: string[]` of TFMs.
Strings like `"net8.0"` repeat across thousands of types verbatim, and
the *array itself* duplicates whenever two types ship in the same
set. Build a `Dictionary<TfmSetKey, string[]>` interner keyed on a
hash of the sorted set; reuse arrays. Estimated 10-20 MB saved on the
slim fixture, much more on real repos.

### 5. PageBuilderPool everywhere in `TypePageEmitter`

`TypePageEmitter` has 5+ `new StringBuilder(capacity: …)` allocations
in helper methods (lines 416, 471, 531, 555, 574). The
`PageBuilderPool` rental infrastructure already exists in
`SourceDocParser`. Replace these with rentals + extend the pool to
handle small-capacity buckets. Probably 20-30 % Emit allocation drop.

### 6. Helper methods returning `string` should `Append` into the caller's builder

`TypePageEmitter` has helpers like `BuildHeaderInfoBlock`,
`BuildAppliesToBlock`, `BuildAttributesBlock` that return `string`
and then get `sb.Append`'d into the parent. Convert each to
`AppendHeaderInfoBlock(StringBuilder dest, …)`. Avoids the
intermediate string materialisation per section per page. ~3-5 % Emit
time, similar allocation reduction.

### 7. ToDisplayString caching keyed on `ISymbol`

`MemberBuilder.BuildOne` and `SymbolWalkerHelpers.BuildTypeReference`
call `ToDisplayString` per symbol. Roslyn's display formatter walks
full type syntax and allocates fresh strings every call. Add a
per-walk `Dictionary<ISymbol, string>` for signatures and type refs —
symbols are reference-stable within a compilation. Modest single-digit
% walk time + 5-10 MB allocation drop.

### 8. RenderedDoc cache per-page

`MemberPageEmitter.AppendSections:369` constructs
`new RenderedDoc(member.Documentation, converter)` per overload. Same
`member.Documentation` reference (e.g. inheritdoc-resolved) gets
re-rendered across overloads. Cache by `member.Documentation`
identity per `MemberPageEmitter.RenderToFile` invocation.

### 9. ConcurrentBag → Channel for walker fan-in

`MetadataExtractor.cs:203` uses
`ConcurrentDictionary<string, ConcurrentBag<ApiType[]>>`.
ConcurrentBag has thread-local segments that allocate per-thread
state and produce non-deterministic enumeration order. For this
one-writer-per-TFM, single-reader pattern: per-TFM `Channel<ApiType[]>`
(or even a plain `lock`-guarded `List<ApiType[]>`) is cheaper and
produces deterministic results — better for cross-TFM merge baseline
tests.

### 10. ArrayPool the reference list in `CompilationLoader.Load`

`Load` allocates `new List<MetadataReference>(resolved.Count + 1)`
per call. Roslyn copies references into internal state during
`CSharpCompilation.Create`, so we can rent + return. Same for the
`List<string> ResolvedPaths` inside `ResolveTransitiveReferences`.
Few MB saved; trivial.

---

## Tier 3 — easy wins (minutes to hours)

### 11. Bump `MaxParallelCompilations` from `3`

Hardcoded at 3 in `MetadataExtractor.cs:30`. Modern dev machines have
8-32 cores. Use `Math.Min(Environment.ProcessorCount, 8)` (or a
config knob). Probably 20-30 % wall-time win on warm-cache runs.
Risk: peak memory scales linearly with parallelism, so we need item
#1 (streaming) to safely push higher.

### 12. Pre-size `Stack<PEFile>` and `List<string>` in `ResolveTransitiveReferences`

Both grow from default capacity inside the per-DLL hot loop. The
seed assembly's `AssemblyReferences.Count` is a tight initial size.

### 13. `XmlDocToMarkdown` tagged path is 38× the plain baseline

`ConvertTaggedSummary` at 913 ns vs 24 ns plain. Look at
`XmlMarkupParser.ReadMarkup` — likely allocates intermediate `string`
per tag name. Switch fully to `ReadOnlySpan<char>` + cached
`SearchValues<char>` for tag-terminator scans (the pattern is
already used in `XmlAttributeParser`).

### 14. Confirm every `ToDisplayString()` call site passes a cached `SymbolDisplayFormat`

`SymbolWalkerHelpers.cs:345` and `:349` call `.ToDisplayString()`
(no format). The default format triggers Roslyn's least-optimised
path. Pin to a project-shared format constant and re-benchmark walk.

### 15. `Tfm.Tfm.Parse` is called repeatedly with the same string

Particularly inside `StreamingTypeMerger.Add` per catalog and per
type-bucket sort. Cache the parsed result alongside the catalog or
intern via a `ConcurrentDictionary<string, Tfm>`.

---

## Suggested execution order

1. **#11** — parallelism bump (10-min change, free speedup).
2. **#5 + #6** — PageBuilderPool + Append-helpers (half-day, ~30 % Emit allocation drop).
3. **#4** — AppliesTo interning (half-day, scales with repo size).
4. **#10 + #12 + #15** — small ArrayPool / pre-size / parse-cache (afternoon, hygiene).
5. **#7 + #8 + #13 + #14** — display-string + RenderedDoc + xml-doc spans (1-2 days, walker hot path tuning).
6. **#3** — compilation cache (2-3 days, big LoadAndWalk win).
7. **#1** — streaming pipeline (1-2 weeks, unlocks large repos).
8. **#2** — MetadataReader-based walker (2-4 weeks, biggest single-feature win).
