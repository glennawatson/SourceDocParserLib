// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.LibCompilation;

/// <summary>
/// Cross-platform discovery for the locally-installed .NET SDK pack
/// directories. The walker uses these as the lowest-priority fallback
/// when resolving transitive assembly references that don't ship as
/// NuGet packages -- WPF / WinForms framework assemblies, Android /
/// iOS / macOS workload refs, ASP.NET Core targeting refs, and so on.
///
/// Discovery walks the canonical locations in priority order so an
/// explicit override (the <c>DOTNET_ROOT</c> env var, or a
/// <c>dotnet</c> on PATH) wins over the defaults, and the per-user
/// <c>~/.dotnet</c> tail catches workload installs that frequently
/// land there even on a system-wide SDK install. The returned roots
/// are deduplicated and filtered to those that exist on disk so the
/// caller can iterate without re-checking <see cref="Directory.Exists(string)"/>.
///
/// Functional design: every public method takes a frozen
/// <see cref="DotNetSdkLocatorInputs"/> snapshot, so the locator
/// itself holds no global state and concurrent callers (or
/// concurrent tests) can each work against their own captured
/// view of process / OS input. The parameterless overloads keep
/// production callers ergonomic by capturing a snapshot lazily via
/// <see cref="DotNetSdkLocatorInputs.Snapshot"/>.
/// </summary>
internal static class DotNetSdkLocator
{
    /// <summary>The <c>packs/</c> subdirectory holds every ref pack.</summary>
    private const string PacksFolder = "packs";

    /// <summary>The <c>.dotnet</c> per-user install/workload folder.</summary>
    private const string DotnetUserFolder = ".dotnet";

    /// <summary>Common name of the dotnet executable on every supported OS (sans extension).</summary>
    private const string DotnetExeStem = "dotnet";

    /// <summary>Windows-specific executable extension when probing PATH.</summary>
    private const string WindowsExeExtension = ".exe";

    /// <summary>
    /// Lazily-captured process snapshot. Env vars + OS detection are
    /// effectively static for the process lifetime; computing them
    /// once via <see cref="Lazy{T}"/> spares every call site the
    /// fan-out of env reads. <see cref="LazyThreadSafetyMode.ExecutionAndPublication"/>
    /// is the default and is what we want -- the snapshot is built
    /// at most once even under concurrent first-touch.
    /// </summary>
    private static readonly Lazy<DotNetSdkLocatorInputs> _processSnapshot = new(DotNetSdkLocatorInputs.Snapshot);

    /// <summary>
    /// Returns the candidate dotnet install roots in discovery
    /// priority order, using a process-lifetime snapshot of env /
    /// OS state. Subsequent calls reuse the same snapshot.
    /// </summary>
    /// <returns>Ordered, distinct, existing install-root paths.</returns>
    public static IReadOnlyList<string> EnumerateInstallRoots() =>
        EnumerateInstallRoots(_processSnapshot.Value);

    /// <summary>
    /// Returns the install roots derivable from <paramref name="inputs"/>.
    /// Pure function: no env / OS reads happen inside this overload,
    /// so concurrent callers can never see torn results and tests can
    /// construct any combination of env state without mutating the
    /// process.
    /// </summary>
    /// <param name="inputs">Frozen process / OS snapshot.</param>
    /// <returns>Ordered, distinct, existing install-root paths.</returns>
    public static IReadOnlyList<string> EnumerateInstallRoots(DotNetSdkLocatorInputs inputs)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>(capacity: 6);

        AddIfExists(inputs.DotnetRoot, seen, ordered);
        AddIfExists(inputs.DotnetRootX86, seen, ordered);
        AddIfExists(GetDotnetDirFromPath(inputs), seen, ordered);

        var defaults = GetDefaultInstallRoots(inputs);
        for (var i = 0; i < defaults.Count; i++)
        {
            AddIfExists(defaults[i], seen, ordered);
        }

        AddIfExists(GetUserDotnetRoot(inputs), seen, ordered);

        return ordered;
    }

    /// <summary>
    /// Returns the <c>packs/</c> directories under every discovered
    /// install root, captured from the live process environment.
    /// Includes both each system root's <c>packs/</c> AND the per-
    /// user <c>~/.dotnet/packs</c> since workload installs often go
    /// to the latter even on system-wide SDKs.
    /// </summary>
    /// <returns>Ordered, distinct, existing <c>packs/</c> paths.</returns>
    public static IReadOnlyList<string> EnumeratePackRoots() =>
        EnumeratePackRoots(_processSnapshot.Value);

    /// <summary>
    /// Snapshot-driven equivalent of <see cref="EnumeratePackRoots()"/>.
    /// </summary>
    /// <param name="inputs">Frozen process / OS snapshot.</param>
    /// <returns>Ordered, distinct, existing <c>packs/</c> paths.</returns>
    public static IReadOnlyList<string> EnumeratePackRoots(DotNetSdkLocatorInputs inputs)
    {
        var roots = EnumerateInstallRoots(inputs);
        if (roots is [])
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var packs = new List<string>(capacity: roots.Count);
        for (var i = 0; i < roots.Count; i++)
        {
            AddIfExists(Path.Combine(roots[i], PacksFolder), seen, packs);
        }

        return packs;
    }

    /// <summary>
    /// Default per-OS install paths in priority order. Returned
    /// without existence checks so callers can verify them through
    /// the same gate as the env-var/PATH candidates.
    /// </summary>
    /// <param name="inputs">Frozen process / OS snapshot.</param>
    /// <returns>The platform's default install candidates.</returns>
    public static IReadOnlyList<string> GetDefaultInstallRoots(DotNetSdkLocatorInputs inputs)
    {
        if (inputs.IsWindows)
        {
            var candidates = new List<string>(2);
            if (inputs.ProgramFiles is { Length: > 0 } pf)
            {
                candidates.Add(Path.Combine(pf, DotnetExeStem));
            }

            if (inputs.ProgramFilesX86 is { Length: > 0 } pf86)
            {
                candidates.Add(Path.Combine(pf86, DotnetExeStem));
            }

            return candidates;
        }

        if (inputs.IsMacOs)
        {
            return ["/usr/local/share/dotnet"];
        }

        if (inputs.IsLinux)
        {
            return ["/usr/share/dotnet", "/usr/lib/dotnet"];
        }

        return [];
    }

    /// <summary>
    /// Returns the per-user <c>~/.dotnet</c> root when
    /// <paramref name="inputs"/> carries a user profile path. Used
    /// for both per-user SDK installs and workload installs that
    /// the dotnet CLI places under the user's profile by default.
    /// </summary>
    /// <param name="inputs">Frozen process / OS snapshot.</param>
    /// <returns>The path, or null when the user profile cannot be located.</returns>
    public static string? GetUserDotnetRoot(DotNetSdkLocatorInputs inputs) =>
        inputs.UserProfile is { Length: > 0 } home ? Path.Combine(home, DotnetUserFolder) : null;

    /// <summary>
    /// Walks the <c>PATH</c> value held in <paramref name="inputs"/>
    /// looking for a <c>dotnet</c> executable and returns the
    /// directory it lives in. The first match wins.
    /// </summary>
    /// <param name="inputs">Frozen process / OS snapshot.</param>
    /// <returns>The dotnet executable's parent directory, or null when not on PATH.</returns>
    public static string? GetDotnetDirFromPath(DotNetSdkLocatorInputs inputs)
    {
        if (inputs.Path is not { Length: > 0 } path)
        {
            return null;
        }

        var entries = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var executableName = inputs.IsWindows ? DotnetExeStem + WindowsExeExtension : DotnetExeStem;
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            try
            {
                if (File.Exists(Path.Combine(entry, executableName)))
                {
                    return entry;
                }
            }
            catch (ArgumentException)
            {
                // Malformed PATH entry (e.g. invalid characters on
                // Windows). Skip rather than fail the whole probe.
            }
        }

        return null;
    }

    /// <summary>
    /// Adds <paramref name="dir"/> to <paramref name="ordered"/> when
    /// it's non-null, exists, and hasn't been recorded before.
    /// </summary>
    /// <param name="dir">Candidate directory.</param>
    /// <param name="seen">Dedupe set.</param>
    /// <param name="ordered">Output list, mutated in place.</param>
    private static void AddIfExists(string? dir, HashSet<string> seen, List<string> ordered)
    {
        if (dir is not { Length: > 0 } || !Directory.Exists(dir))
        {
            return;
        }

        var canonical = NormaliseFullPath(dir);
        if (!seen.Add(canonical))
        {
            return;
        }

        ordered.Add(canonical);
    }

    /// <summary>Resolves <paramref name="dir"/> to an absolute path with no trailing separator for stable dedupe.</summary>
    /// <param name="dir">The directory to canonicalise.</param>
    /// <returns>The canonicalised path.</returns>
    private static string NormaliseFullPath(string dir) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir));
}
