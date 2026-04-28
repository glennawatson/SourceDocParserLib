// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.LibCompilation;

/// <summary>
/// Snapshot of every process / OS input <see cref="DotNetSdkLocator"/>
/// reads when discovering install roots. Pulled into its own value
/// type so a single concurrent <c>Snapshot()</c> call captures a
/// consistent view of the world -- subsequent enumeration runs
/// against that frozen snapshot can never observe a torn read of
/// <c>DOTNET_ROOT</c> while another thread is mutating it -- and so
/// tests can construct any combination of inputs without touching
/// process-wide state. Every field is nullable to model
/// "unset / not discoverable on this host".
/// </summary>
/// <param name="DotnetRoot">Value of the <c>DOTNET_ROOT</c> env var, or null.</param>
/// <param name="DotnetRootX86">Value of the <c>DOTNET_ROOT(x86)</c> env var, or null.</param>
/// <param name="Path">Value of the <c>PATH</c> env var, or null.</param>
/// <param name="UserProfile">Resolved user profile dir (e.g. <c>%USERPROFILE%</c> or <c>$HOME</c>), or null when unavailable.</param>
/// <param name="ProgramFiles">Value of <see cref="Environment.SpecialFolder.ProgramFiles"/> on Windows, otherwise null.</param>
/// <param name="ProgramFilesX86">Value of <see cref="Environment.SpecialFolder.ProgramFilesX86"/> on Windows, otherwise null.</param>
/// <param name="IsWindows">True on Windows hosts.</param>
/// <param name="IsMacOs">True on macOS hosts.</param>
/// <param name="IsLinux">True on Linux hosts.</param>
internal readonly record struct DotNetSdkLocatorInputs(
    string? DotnetRoot,
    string? DotnetRootX86,
    string? Path,
    string? UserProfile,
    string? ProgramFiles,
    string? ProgramFilesX86,
    bool IsWindows,
    bool IsMacOs,
    bool IsLinux)
{
    /// <summary>
    /// Lock object guarding the multi-read snapshot operation. The
    /// scope is tiny -- only spans the env-var fan-out -- so
    /// contention is negligible and never blocks discovery work.
    /// </summary>
    private static readonly Lock _snapshotGate = new();

    /// <summary>
    /// Captures a consistent snapshot of the current process /
    /// operating environment. Reads happen in a tight, lock-protected
    /// section so concurrent callers can't observe a torn view if a
    /// foreign caller mutates env vars at the same time.
    /// </summary>
    /// <returns>The frozen snapshot.</returns>
    public static DotNetSdkLocatorInputs Snapshot()
    {
        // .NET's env-var reads are themselves atomic, but we still
        // serialise the multi-read sequence so the FOUR strings we
        // capture (DOTNET_ROOT / DOTNET_ROOT(x86) / PATH / profile)
        // come from the same instant of time -- that's the contract
        // tests want when they exercise specific combinations.
        lock (_snapshotGate)
        {
            var isWindows = OperatingSystem.IsWindows();
            var programFiles = isWindows ? NullIfBlank(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)) : null;
            var programFilesX86 = isWindows ? NullIfBlank(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)) : null;
            var userProfile = NullIfBlank(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            return new(
                DotnetRoot: ReadEnv("DOTNET_ROOT"),
                DotnetRootX86: ReadEnv("DOTNET_ROOT(x86)"),
                Path: ReadEnv("PATH"),
                UserProfile: userProfile,
                ProgramFiles: programFiles,
                ProgramFilesX86: programFilesX86,
                IsWindows: isWindows,
                IsMacOs: OperatingSystem.IsMacOS(),
                IsLinux: OperatingSystem.IsLinux());
        }
    }

    /// <summary>Reads an env var and normalises empty/missing to null.</summary>
    /// <param name="name">Env var name.</param>
    /// <returns>The value, or null when unset/blank.</returns>
    private static string? ReadEnv(string name) => NullIfBlank(Environment.GetEnvironmentVariable(name));

    /// <summary>Coerces empty / null strings to null, otherwise returns the value as-is.</summary>
    /// <param name="value">The value to inspect.</param>
    /// <returns>The non-empty value, or null.</returns>
    private static string? NullIfBlank(string? value) => value is { Length: > 0 } ? value : null;
}
