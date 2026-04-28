# CLAUDE.md

## Repository Orientation

- **Primary working directory for build/test:** `./src`
- **Main solution:** `src/SourceDocParserLib.slnx`
- **Production libraries:**
  - `src/SourceDocParser/` — Core walker, merger, source-link resolution. Defines the `IAssemblySource`, `IDocumentationEmitter`, `IMetadataExtractor` seams.
  - `src/SourceDocParser.NuGet/` — `IAssemblySource` that fetches packages from `nuget.org` by owner / explicit list and exposes the per-TFM `lib/` trees. Owns `INuGetFetcher` + the streaming `JsonDocument`-based `PackageConfigReader`.
  - `src/SourceDocParser.Zensical/` — `IDocumentationEmitter` that writes Markdown tuned for Zensical / mkdocs Material (admonitions, content tabs, mermaid).
  - `src/SourceDocParser.Docfx/` — docfx config-file shim. Reads + writes `docfx.json` shapes via `DocfxConfigReader` / `DocfxConfigWriter` (hand-written `JsonDocument` reader, `Utf8JsonWriter` writer; no source-gen serializer in the path). No emitter — that ships separately.
- **Test projects:**
  - `src/tests/SourceDocParser.Tests/` — Pure unit tests (TUnit). Covers `TfmResolver`, `TypeMerger`, `XmlDocToMarkdown`, `DocfxConfigReader` round-trip, and `MetadataExtractor` argument validation through fake `IAssemblySource` / recording `IDocumentationEmitter` impls. Uses `Fixtures/nuget-packages.json` (slim debug config) and shares it with the integration tests via a `Link` reference.
  - `src/tests/SourceDocParser.IntegrationTests/` — End-to-end pipeline test that fetches the slim fixture from nuget.org, walks it through the parser, and asserts catalog/page output. Plus `ZensicalRenderSmokeTests` which bootstraps a Python venv under `zensical/.venv` and runs `zensical build --strict` against the bundled mock site fixture.

## Build

```bash
# Restore + build (from src/)
cd src
dotnet build SourceDocParserLib.slnx

# Build a single project
dotnet build SourceDocParser/SourceDocParser.csproj
```

`<GeneratePackageOnBuild>true</GeneratePackageOnBuild>` is set in `src/Directory.Build.props` for every non-test project, so each `dotnet build` also writes `.nupkg` + `.snupkg` to `artifacts/packages/`. Consumers in other repos (e.g. the `reactiveui/website` Nuke build) wire that directory up as a local feed via `nuget.config` until the libraries are published to nuget.org.

## Testing: Microsoft Testing Platform (MTP) + TUnit

This repo uses **Microsoft Testing Platform (MTP)** with **TUnit** (not VSTest).

- MTP is configured via `src/global.json` (`"runner": "Microsoft.Testing.Platform"`).
- `TestingPlatformDotnetTestSupport` is enabled in `src/Directory.Build.props`.
- `IsTestProject` is auto-detected via `$(MSBuildProjectName.Contains('Tests'))` in `Directory.Build.props`. Test projects automatically get `<OutputType>Exe</OutputType>`, the TUnit + Verify.TUnit packages, the implicit-usings switches, and `<NoWarn>$(NoWarn);CA1812</NoWarn>`.
- `IsPackable=false` is set on each test project explicitly (defence-in-depth — Directory.Build.props sets it too).

### Test Commands (run from `./src`)

**CRITICAL:** Run `dotnet test` from the `src/` directory so the `global.json` MTP runner config is discovered. Use `--project` to specify the test project.

```bash
cd src

# Unit tests
dotnet test --project tests/SourceDocParser.Tests/SourceDocParser.Tests.csproj

# Integration tests (will hit nuget.org for the slim fixture, ~10s on first run)
dotnet test --project tests/SourceDocParser.IntegrationTests/SourceDocParser.IntegrationTests.csproj

# Detailed output (place BEFORE --)
dotnet test --project tests/SourceDocParser.Tests/SourceDocParser.Tests.csproj -- --output Detailed

# List tests
dotnet test --project tests/SourceDocParser.Tests/SourceDocParser.Tests.csproj -- --list-tests

# Fail fast
dotnet test --project tests/SourceDocParser.Tests/SourceDocParser.Tests.csproj -- --fail-fast

# Run a single test method by tree-node filter
dotnet test --project tests/SourceDocParser.Tests/SourceDocParser.Tests.csproj -- --treenode-filter "/*/*/TfmResolverTests/FindBestRefsTfmHandlesPlatformSuffix"

# All tests in a class
dotnet test --project tests/SourceDocParser.Tests/SourceDocParser.Tests.csproj -- --treenode-filter "/*/*/TfmResolverTests/*"
```

### Testing Best Practices

- **Do NOT use `--no-build`** — always build before testing to avoid stale binaries.
- Use `--output Detailed` **before** `--` for verbose output.
- The integration tests touch the network on first run to populate the local NuGet cache; subsequent runs reuse `apiPath/cache/*.nupkg` and complete in ~3 seconds.

### Code Coverage

Coverage uses **Microsoft.Testing.Extensions.CodeCoverage** wired in via `src/Directory.Build.props` (added to every test project). Per-assembly options (format, attribute exclusions) live in `src/testconfig.json` and are linked next to each test binary as `<AssemblyName>.testconfig.json`.

```bash
cd src

# Run unit tests with coverage
dotnet test --project tests/SourceDocParser.Tests/SourceDocParser.Tests.csproj -- --coverage --coverage-output-format cobertura

# Generate an HTML report (install once: dotnet tool install -g dotnet-reportgenerator-globaltool)
reportgenerator \
  -reports:"**/TestResults/**/*.cobertura.xml" \
  -targetdir:/tmp/sourcedocparser_coverage \
  -reporttypes:"Html;TextSummary"
cat /tmp/sourcedocparser_coverage/Summary.txt
```

### Benchmarks

`src/benchmarks/SourceDocParser.Benchmarks/` is a BenchmarkDotNet harness covering `MetadataExtractor.RunAsync` end-to-end against the slim debug NuGet fixture (3 owner-discovered packages, 19 TFM groups). The global setup runs one full fetch to warm the local NuGet cache, so per-iteration timings measure the walk + merge + emit pipeline without the network leg.

```bash
cd src

# Run every benchmark in the assembly
dotnet run --project benchmarks/SourceDocParser.Benchmarks/SourceDocParser.Benchmarks.csproj --configuration Release

# Filter to a single benchmark via the BenchmarkDotNet switcher
dotnet run --project benchmarks/SourceDocParser.Benchmarks/SourceDocParser.Benchmarks.csproj --configuration Release -- --filter '*RunAsync*'
```

### Zensical render-smoke

`ZensicalRenderSmokeTests` runs the actual `zensical build --strict` against a bundled mock site (`tests/SourceDocParser.IntegrationTests/zensical/mock-site/`) so we catch Zensical/Material rendering bugs that pure-C# tests can't. Self-bootstrapping:

1. On first run the test creates a Python virtualenv at `tests/SourceDocParser.IntegrationTests/zensical/.venv` via `python3 -m venv` (assumes `python3` is on PATH; gracefully skips otherwise).
2. Installs `requirements.txt` (`zensical`, `pymdown-extensions`) into the venv.
3. Writes a `.requirements-installed` marker; subsequent runs skip the install when the marker is newer than `requirements.txt`.
4. Runs `zensical build --strict` from the venv against the mock site.
5. Asserts a zero exit code; stdout/stderr surface in the failure message.

The venv lives under the project so it's isolated from the user's system Python and gitignored (see `.gitignore` — `.venv/` excluded along with `site/`, `__pycache__/`, etc.).

## Code Style

- `.editorconfig` at the repo root drives formatting + IDExxxx severities.
- StyleCop, Roslynator, and Blazor.Common analyzers are active in every project (configured in `src/Directory.Build.props`).
- `EnforceCodeStyleInBuild=true` so editorconfig severities for IDExxxx rules fire at compile time.
- File header copyright text comes from `stylecop.json` (`"companyName": "Glenn Watson and Contributors"`); SA1636 enforces every `.cs` file matches.
- Public APIs require XML documentation (`<GenerateDocumentationFile>true</GenerateDocumentationFile>`); SA1600 / SA1611 / SA1615 catch missing element / parameter / return docs.
- Logging is source-generated `[LoggerMessage]` partial methods on `ILogger` parameters — no `logger.LogInformation("...")` direct calls (CA1848). Expensive argument expressions go behind `LogInvokerHelper.Invoke(...)` to gate evaluation on `IsEnabled`.
- Public concrete classes that have an interface counterpart (`MetadataExtractor`/`IMetadataExtractor`, `NuGetFetcher`/`INuGetFetcher`, `SourceLinkValidator`/`ISourceLinkValidator`) keep their private helpers + `[LoggerMessage]` partials `static`; only the public entry point is instance.
- Path-typed public APIs use `string`, never `Nuke.Common.IO.AbsolutePath` — the libraries don't take a Nuke dependency. Validate with `ArgumentException.ThrowIfNullOrWhiteSpace`.

## Performance & Idiomatic C# Rules

These rules apply to **production code** (everything under `src/` that isn't a test project). Test projects (`src/tests/**`) are exempt from the allocation-discipline rules — `foreach`, LINQ, and `List<T>` are fine in tests where readability beats micro-optimisation. The pattern-matching, switch-expression, and list-pattern rules still apply to tests because they're style-not-perf.

### Pattern matching & flow control

- **Invert `if`s to flatten the happy path.** Guard-clauses + early `return`/`continue` first; main logic stays unindented. No `else` clauses on guarded branches.
- **Switch expressions over `if`/`else` chains** — use property patterns (`{ IsPublic: true }`), positional patterns, and recursive patterns. Every `ModifierLabel`/`KindLabel` style helper should be a switch expression.
- **List patterns for emptiness/cardinality.** Prefer `is [_, ..]` over `.Length > 0` / `.Count > 0` / `!string.IsNullOrEmpty`. Prefer `is []` for empty. Use `is [var single]` to bind a single-element collection in one shot.
- **`is`/`is not` patterns over `==`/`!=`** for null and type checks. Combine type test + property check in one line: `member is IMethodSymbol { IsExtensionMethod: true }`.
- **`is 0` / `is { Length: 0 }`** etc. for property-pattern numeric and length checks where it reads cleanly.

### Allocation discipline

- **Zero-LINQ policy.** No `System.Linq` in production code. LINQ pulls in lambdas + iterators on every call. Use plain `for` loops.
- **Avoid `foreach` whenever a `for` loop with an indexer works.** `foreach` over `IEnumerable<T>` boxes/allocates an enumerator; even on `List<T>`/`Dictionary<T,U>` it allocates a struct enumerator the JIT often can't elide. Use `for (var i = 0; i < x.Length; i++)` on arrays / `Span<T>` / `ReadOnlySpan<T>`. Only use `foreach` when iterating a type that genuinely lacks an indexer (e.g. `HashSet<T>`) and you've considered materialising it to an array first.
- **Arrays over `List<T>`** when the final length is known up front. Pre-size and write by index. Reserve `List<T>` for genuinely unbounded growth, and pre-size with a capacity hint.
- **`Span<char>` / `ReadOnlySpan<char>` + range expressions** for prefix checks, slicing, parsing — never allocate a temporary `string` to call `.StartsWith` / `.Substring`.
- **UTF-8 string literals (`"..."u8`)** in JSON / byte-level parsing paths to skip the UTF-16 → UTF-8 round-trip. Default for `Utf8JsonReader` / `Utf8JsonWriter` property names and any byte-sequence comparisons.
- **Pre-size `StringBuilder` / `Dictionary` / `HashSet`** with a capacity hint that reflects the expected size. The integration tests catch cases where this matters.
- **`string.Create(length, state, span => ...)`** for short, hot-path string assembly when concatenation would otherwise build a tree of intermediates.

### Collection expressions & syntax

- **Collection expressions `[...]`** for arrays, lists, and search sets — `IndexOfAny(['/', '\\'])`, `[..]` for spread, etc. Lets the compiler pick the optimal layout.
- **Range expressions (`x[..n]`, `x[n..]`)** for slicing, never `Substring`.
- **`TryPop` / `TryDequeue` / `TryGetValue`** in loops — drop the redundant `Count > 0` / `ContainsKey` pre-check.

### Constants & maintainability

- **Hoist magic strings/numbers to `private const`** with one-line XML docs explaining the *why*. Especially URL prefixes, separators, capacity hints, and length-of-suffix-style values.
- **No magic numbers in `string.Create` size calculations** — name them (e.g. `ParentDirectorySegmentLength = 3`).

### Pooling & buffer reuse

- **`ArrayPool<T>.Shared.Rent` / `.Return`** for transient byte/char buffers in I/O paths (see `PageWriter` for the pattern). Always pair `Rent` with a `try`/`finally` `Return`.
- **Custom pools for hot allocations.** `PageBuilderPool` + `PageBuilderRental` is the project pattern: a thread-static / concurrent stack of pre-sized `StringBuilder`s, returned via a `readonly struct` rental that calls `Return` on `Dispose`. Use this when a type is allocated thousands of times per emit run.
- **`stream.WriteAsync(buffer.AsMemory(0, length))`** to write a partially-filled rented buffer without copying.

### Span / search APIs

- **`SearchValues<T>` for repeated multi-character searches.** Cache as `private static readonly SearchValues<char>` (e.g. `XmlAttributeParser.WhitespaceChars`) and pass to `IndexOfAny` / `IndexOfAnyExcept`. Faster than `IndexOfAny([...])` for any call site hit more than once.
- **`IndexOfAnyExcept`** for "skip whitespace" / "skip terminator" loops — single intrinsic-backed call instead of a hand-rolled loop.
- **`in` modifier on span/struct parameters** (`in ReadOnlySpan<char>`, `in MarkupResult`) when the parameter is read-only and the struct is large enough that a copy matters.
- **Spans as fields on `ref struct`s** (e.g. `DocXmlScanner`) for stack-only stateful parsers. The struct can hold a `ReadOnlySpan<char>` cursor without ever allocating.
- **`TryFormat` / `TryParse` over `ToString` / `Parse`** when writing into a span buffer — skips the intermediate string allocation.

### Read-mostly lookups

- **`FrozenDictionary<TKey, TValue>` / `FrozenSet<T>`** for tables built once at startup and read many times (see `CatalogIndexes`). Build with `ToFrozenDictionary(StringComparer.Ordinal)`. Lookup is faster than `Dictionary<,>` and the table is immutable.
- **Always pass `StringComparer.Ordinal`** to dictionaries/sets keyed on identifiers, file paths, UIDs. Default culture-aware comparison is wrong for these and 5-10× slower. Same for `StringComparison.Ordinal` on `string.Equals` / `IndexOf`.

### Async & concurrency

- **`ConfigureAwait(false)` on every library `await`.** No exceptions in `src/SourceDocParser*/`. Tests don't need it.
- **`ValueTask` / `ValueTask.CompletedTask`** for hot async paths that may complete synchronously. Avoids `Task` allocation per call. Watch the consumption rules — never `await` a `ValueTask` twice.
- **`IAsyncEnumerable<T>`** for streaming sources (`IAssemblySource.DiscoverAsync`). Consumer pulls with `await foreach` (one of the few legitimate `foreach` uses — there is no indexed alternative).
- **`Parallel.ForEachAsync`** with an explicit `MaxDegreeOfParallelism` over hand-rolled `Task.WhenAll` fan-out. The cap is critical: `MetadataExtractor` uses `MaxParallelCompilations` so we don't OOM on large NuGet sets.
- **`Interlocked.Increment` / `Interlocked.Decrement`** for simple counters under contention. Reserve `lock` for genuine multi-field invariants.

### Type design

- **`sealed` every class** that isn't designed for inheritance. The default in this repo. Helps inlining and avoids accidental override surface.
- **`readonly record struct`** for immutable value-shaped data: small (≤ 4-5 fields) or holding only references (strings, arrays). Equality and hashing come for free, no GC pressure.
- **`sealed record` (class)** when the record participates in inheritance hierarchies (e.g. `ApiObjectType : ApiType`) or holds many fields.
- **Static helpers** for stateless functions; only the public entry-point class is instance-shaped (already documented above).
- **Singleton comparers (`private sealed class XComparer : IComparer<T>` with `public static readonly XComparer Instance`)** instead of allocating a fresh comparer / lambda per `Array.Sort` call.

### When in doubt

The order of preference is: **for-loop over array → for-loop over `List<T>` → `foreach` over indexable → `foreach` over `IEnumerable<T>` (last resort)**. If a hot path can't be expressed with the top option, leave a one-line comment explaining why.

## Versioning

`Nerdbank.GitVersioning` (`version.json` at the repo root) computes version on every build. Base is `0.1-alpha`. Public releases are gated on the `master`/`main` branch via `publicReleaseRefSpec`; off-branch builds get the height + commit suffix.

## Acknowledgements

The metadata extraction pipeline is inspired by — and lifts patterns from — [dotnet/docfx](https://github.com/dotnet/docfx) (MIT). See `LICENSE` for the original docfx attribution.
