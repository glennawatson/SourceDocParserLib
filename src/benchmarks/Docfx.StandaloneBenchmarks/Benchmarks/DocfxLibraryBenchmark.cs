// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using Docfx.Dotnet;

namespace Docfx.StandaloneBenchmarks.Benchmarks;

/// <summary>
/// Standalone docfx metadata-extraction benchmark -- bypasses docfx's
/// MSBuild project loader and walks raw DLLs directly. The benchmark
/// restores a synthesised csproj only to populate the NuGet cache,
/// reads the resolved compile-time DLL paths out of
/// <c>obj/project.assets.json</c>, stages them into per-TFM folders,
/// and points docfx at those folders. Docfx then drives Roslyn against
/// the same physical assemblies our SourceDocParser pipeline walks.
/// One <c>[Params]</c>-selected TFM per row keeps each measurement
/// scoped to a single framework slice for a fair side-by-side.
/// </summary>
[ShortRunJob]
[MemoryDiagnoser]
public class DocfxLibraryBenchmark
{
    /// <summary>Packages the slim fixture pulls in -- matches the SourceDocParser benchmark fixture.</summary>
    private static readonly (string Id, string Version)[] FixturePackages =
    [
        ("ReactiveUI", "*"),
        ("Splat", "*"),
        ("DynamicData", "*"),
        ("System.Reactive", "*"),
    ];

    /// <summary>Workspace the benchmark scaffolds the synthetic csproj + per-TFM staging dirs in.</summary>
    private string _workspace = string.Empty;

    /// <summary>Per-TFM docfx.json paths populated in <see cref="GlobalSetup"/>.</summary>
    private Dictionary<string, string> _docfxConfigPerTfm = new(StringComparer.Ordinal);

    /// <summary>Resolved docfx config for the current iteration's <see cref="Tfm"/>.</summary>
    private string _docfxConfig = string.Empty;

    /// <summary>Gets the workspace directory -- exposed so the dump-mode runner can point users at the emitted YAML for diffing.</summary>
    public string WorkspaceForInspection => _workspace;

    /// <summary>Gets or sets the TFM under measurement -- BDN runs one row per value.</summary>
    [Params("net8.0", "net9.0", "net10.0", "net472")]
    public string Tfm { get; set; } = string.Empty;

    /// <summary>Restores the fixture once, stages compile-time DLLs per TFM, generates one docfx.json per TFM.</summary>
    [GlobalSetup]
    public void GlobalSetup()
    {
        _workspace = Path.Combine(Path.GetTempPath(), $"sdp-docfx-stand-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workspace);

        var fixtureProject = Path.Combine(_workspace, "Fixture.csproj");
        File.WriteAllText(fixtureProject, BuildFixtureCsproj());

        RunRestore(fixtureProject);

        var assets = JsonNode.Parse(File.ReadAllText(Path.Combine(_workspace, "obj", "project.assets.json")))!.AsObject();
        var packagesRoot = assets["project"]!["restore"]!["packagesPath"]!.GetValue<string>();
        var libraries = assets["libraries"]!.AsObject();
        var targets = assets["targets"]!.AsObject();

        _docfxConfigPerTfm = new(StringComparer.Ordinal);

        foreach (var (targetMoniker, targetItems) in targets)
        {
            if (ShortTfmFromMoniker(targetMoniker) is not { } tfmShort)
            {
                continue;
            }

            var stageDir = Path.Combine(_workspace, "stage", tfmShort);
            Directory.CreateDirectory(stageDir);

            foreach (var (libKey, libEntry) in targetItems!.AsObject())
            {
                if (libEntry?["compile"] is not JsonObject compile)
                {
                    continue;
                }

                if (libraries[libKey]?["path"]?.GetValue<string>() is not { } libRelativePath)
                {
                    continue;
                }

                var libRoot = Path.Combine(packagesRoot, libRelativePath);

                foreach (var (relativeDll, _) in compile)
                {
                    if (!relativeDll.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var src = Path.Combine(libRoot, relativeDll.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(src))
                    {
                        continue;
                    }

                    var dst = Path.Combine(stageDir, Path.GetFileName(src));
                    File.Copy(src, dst, overwrite: true);
                }
            }

            var configPath = Path.Combine(_workspace, $"docfx.{tfmShort}.json");
            File.WriteAllText(configPath, BuildDocfxJson(stageDir, tfmShort));
            _docfxConfigPerTfm[tfmShort] = configPath;
        }
    }

    /// <summary>Per-iteration setup. Picks the docfx.json for the current <see cref="Tfm"/> param.</summary>
    [IterationSetup]
    public void IterationSetup() => _docfxConfig = _docfxConfigPerTfm[Tfm];

    /// <summary>Removes the workspace after the benchmark series completes.</summary>
    [GlobalCleanup]
    public void GlobalCleanup()
    {
        if (!Directory.Exists(_workspace))
        {
            return;
        }

        try
        {
            Directory.Delete(_workspace, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    /// <summary>Times one docfx YAML emission pass over the staged DLL set for the current TFM.</summary>
    /// <returns>A task representing the asynchronous extraction.</returns>
    [Benchmark]
    public Task GenerateManagedReferenceYaml() =>
        DotnetApiCatalog.GenerateManagedReferenceYamlFiles(_docfxConfig);

    /// <summary>Runs <c>dotnet restore</c> on the synthetic fixture csproj to populate the local NuGet cache.</summary>
    /// <param name="fixtureProject">Absolute path to the csproj.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Roslynator", "RCS1208:Reduce 'if' nesting", Justification = "Linear restore-then-throw read more clearly than an early-exit shape here.")]
    private static void RunRestore(string fixtureProject)
    {
        var restore = Process.Start(new ProcessStartInfo("dotnet", $"restore \"{fixtureProject}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        restore.WaitForExit();
        if (restore.ExitCode != 0)
        {
            throw new InvalidOperationException($"dotnet restore failed: {restore.StandardError.ReadToEnd()}");
        }
    }

    /// <summary>Maps a NuGet target moniker (".NETCoreApp,Version=v8.0") to its short TFM ("net8.0").</summary>
    /// <param name="moniker">The full target moniker as it appears in <c>project.assets.json</c>.</param>
    /// <returns>The short TFM identifier, or null when the moniker isn't one of the four we measure.</returns>
    private static string? ShortTfmFromMoniker(string moniker)
    {
        // assets.json target keys come in two shapes: ".NETCoreApp,Version=v8.0"
        // (compile-time) and ".NETCoreApp,Version=v8.0/<rid>" (rid-specific
        // runtime). We only care about the compile-time shape; ignore the rest.
        if (moniker.Contains('/', StringComparison.Ordinal))
        {
            return null;
        }

        var commaIndex = moniker.IndexOf(',', StringComparison.Ordinal);
        if (commaIndex < 0)
        {
            // Modern NuGet writes short-form keys ("net8.0", "net10.0", ...) directly. Pass through.
            return moniker;
        }

        var family = moniker[..commaIndex];
        var rest = moniker[(commaIndex + 1)..];
        var version = rest.StartsWith("Version=v", StringComparison.Ordinal)
            ? rest["Version=v".Length..]
            : rest;

        return family switch
        {
            ".NETCoreApp" => $"net{version}",
            ".NETFramework" => "net" + version.Replace(".", string.Empty, StringComparison.Ordinal),
            _ => null,
        };
    }

    /// <summary>Builds the multi-target restore-only csproj. Workloads-free TFM set so restore needs no SDK extras.</summary>
    /// <returns>The csproj XML text.</returns>
    private static string BuildFixtureCsproj()
    {
        var refs = string.Join(
            "\n    ",
            FixturePackages.Select(p => $"<PackageReference Include=\"{p.Id}\" Version=\"{p.Version}\" />"));

        return $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net8.0;net9.0;net10.0;net472</TargetFrameworks>
                <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
                <DisableImplicitFrameworkReferences>false</DisableImplicitFrameworkReferences>
                <NoWarn>$(NoWarn);CS8002;NU1701;NU1605;NU1903</NoWarn>
              </PropertyGroup>
              <ItemGroup>
                {refs}
              </ItemGroup>
            </Project>
            """;
    }

    /// <summary>Builds a docfx.json that points at one staged TFM directory and walks every DLL inside it.</summary>
    /// <param name="stageDir">Absolute path to the per-TFM staging directory.</param>
    /// <param name="tfmShort">Short TFM identifier (net8.0, net472, ...). Drives the destination subfolder.</param>
    /// <returns>The docfx.json text.</returns>
    private static string BuildDocfxJson(string stageDir, string tfmShort) =>
        $$"""
        {
          "metadata": [
            {
              "src": [
                {
                  "src": "{{stageDir.Replace("\\", @"\\", StringComparison.Ordinal)}}",
                  "files": [ "*.dll" ]
                }
              ],
              "dest": "api/{{tfmShort}}"
            }
          ]
        }
        """;
}
