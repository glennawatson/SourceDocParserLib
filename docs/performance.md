# Performance

SourceDocParser focuses on generating .NET API documentation for MkDocs and
Zensical sites. It reads compiled assemblies, resolves the metadata their APIs
need, merges the selected target frameworks, and produces pages for your site
builder. Its Docfx adapter produces ManagedReference YAML from the same catalog.

Build cost depends on the packages, target frameworks, and output you select.
A small API package and a collection of platform packages are different
workloads, even when their final sites contain similar numbers of type pages.

## What affects your build

- **Package acquisition.** A first build may download dependencies and framework
  reference packs. Later builds reuse NuGet's cache. Published-package
  documentation does not require those SDKs or workloads to be installed.
- **Target frameworks.** Each root and selected TFM has its own reference graph.
  This preserves platform APIs and lets older and newer packages use different
  dependency versions. More groups mean more metadata to process.
- **Documentation roots.** Only declared packages receive API pages and local
  API links. Transitive dependencies supply reference metadata. Keeping roots
  focused also keeps the generated navigation focused.
- **XML documentation and output.** Comments, inherited documentation, and
  reference expansion have costs beyond reading type signatures. Markdown and
  ManagedReference YAML also carry different amounts of page metadata.

## A package collection with multiple frameworks

A pinned workload containing ReactiveUI 24.2.0, Splat 21.0.0, DynamicData
9.4.33, and System.Reactive 7.0.0 produced **462 merged types and 2,905 pages**.
It processed **35 package/TFM groups** with warm NuGet package caches.

| Runtime | Complete extraction and Markdown emission | Total managed allocation |
|---|---:|---:|
| .NET 10.0.12 | 8.713 ± 0.431 s | 9.85 GiB |
| .NET 11 release candidate | 8.737 ± 0.194 s | 9.85 GiB |

The intervals are BenchmarkDotNet's 99.9% confidence intervals. They overlap,
so this run does not establish a meaningful speed difference between the two
runtimes. The allocation figure is the total created throughout a build; it is
not a requirement to keep 9.85 GiB resident at once.

The merged type count is smaller than the work done while parsing each
framework. Separate phase measurements show that reference discovery and
metadata loading dominate this workload; writing the prepared Markdown pages
takes about 60–63 ms. Phase measurements use prepared state and are not additive.

## Comparison with Docfx

Docfx supplies a broader documentation toolchain out of the box, including
conceptual Markdown, source/project API extraction, REST API documentation,
templates, navigation, cross-references, and HTML site building and serving.
SourceDocParser is an embeddable API-generation component with opinionated
MkDocs / Zensical output. Its Docfx adapter follows our catalog and page choices;
it does not reproduce every Docfx policy or output detail.

For **Refit 15.2.0 on net10.0**, extracting APIs and writing YAML measured:

| Generator | Time ± 99.9% confidence interval | Managed allocation |
|---|---:|---:|
| SourceDocParser with its Docfx YAML emitter | 123.9 ± 6.70 ms | 128.22 MiB |
| Docfx 2.80.1 | 564.1 ± 7.58 ms | 501.37 MiB |

SourceDocParser was about **4.6 times faster and allocated 74% less for this
workload**. Both outputs parsed successfully and described the same 490 API
entries across 83 types. Visibility settings were matched, and both generators
received the same assemblies and XML documentation.

This measures a shared API-generation stage. It excludes package fetching and
site rendering, and does not establish a general speed ratio or feature parity.
Docfx writes richer reference output and an additional table-of-contents file;
those output choices contribute to the measured work.

## Reading the measurements

The measurements ran in Release mode on a Ryzen 7 5800X. The Refit comparison
used .NET 10.0.12; the package collection used separate .NET 10 and .NET 11 jobs.
Five warmups and 15 measurements were taken with no profiler attached. Separate
allocation traces attributed most of SourceDocParser's sampled allocation to
XML documentation loading and indexing.

Use results from a workload close to your own. Warm-cache measurements do not
predict the duration of a first download, and phase timings are not additive:
isolated phases use different prepared state from the complete pipeline.

The [benchmark guide](../src/benchmarks/README.md) contains execution details and
links to the full result tables. The [Docfx comparison notes](../src/benchmarks/docfx-comparison.md)
describe the matched configuration and output differences in detail.
