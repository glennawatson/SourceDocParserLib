// Copyright (c) 2025-2026 Glenn Watson and contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.NuGet.Infrastructure;

/// <summary>
/// Pure helpers for the OS-conditional branches in
/// <see cref="NuGetGlobalCache"/>. Lifted out so each branch can be
/// driven directly with explicit inputs -- the surrounding
/// <c>OperatingSystem.IsWindows()</c> + special-folder reads are
/// not test-substitutable in the host process.
/// </summary>
internal static class NuGetConfigPathsResolver
{
    /// <summary>Tail of the platform-default global packages path under the user profile.</summary>
    private const string DefaultRelativePath = ".nuget/packages";

    /// <summary>Directory containing user-scoped configuration files.</summary>
    private const string ConfigurationDirectoryName = "NuGet";

    /// <summary>Configuration filename used in user-scoped directories.</summary>
    private const string ConfigurationFileName = "NuGet.Config";

    /// <summary>
    /// Resolves the user-scoped <c>nuget.config</c> candidate paths
    /// for the current OS -- Windows looks under <c>%AppData%\NuGet</c>,
    /// Unix looks under both <c>~/.nuget/NuGet</c> and
    /// <c>~/.config/NuGet</c>. Returns an empty array when the
    /// required folder isn't resolvable.
    /// </summary>
    /// <param name="isWindows">True to follow the Windows layout.</param>
    /// <param name="appData">Resolved <c>%AppData%</c> (Windows only); null/empty allowed.</param>
    /// <param name="userProfile">Resolved <c>$HOME</c> (Unix only); null/empty allowed.</param>
    /// <returns>Candidate paths in precedence order, or an empty array.</returns>
    internal static string[] GetUserPaths(bool isWindows, string? appData, string? userProfile)
    {
        if (isWindows)
        {
            return !TextHelpers.HasValue(appData) ? [] : [Path.Combine(appData, ConfigurationDirectoryName, ConfigurationFileName)];
        }

        return !TextHelpers.HasValue(userProfile)
            ? []
            : [
            Path.Combine(userProfile, ".nuget", ConfigurationDirectoryName, ConfigurationFileName),
            Path.Combine(userProfile, ".config", ConfigurationDirectoryName, ConfigurationFileName),
        ];
    }

    /// <summary>
    /// Returns every <c>*.config</c> file under
    /// <paramref name="root"/>, sorted ordinal -- the same shape
    /// NuGet's machine-scope walk produces. Returns an empty array
    /// when the root is blank or doesn't exist.
    /// </summary>
    /// <param name="root">Machine config root directory.</param>
    /// <returns>Existing <c>*.config</c> paths under the root, sorted ordinal.</returns>
    internal static string[] GetMachinePaths(string? root)
    {
        if (!TextHelpers.HasNonWhitespace(root) || !Directory.Exists(root))
        {
            return [];
        }

        var files = Directory.GetFiles(root, "*.config", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.Ordinal);
        return files;
    }

    /// <summary>
    /// Composes the platform-default global packages path from a
    /// resolved user profile -- falls back to the current working
    /// directory when the user profile is blank (rare on Unix, only
    /// happens in some sandboxes that don't set <c>HOME</c>).
    /// </summary>
    /// <param name="userProfile">Resolved user profile / home folder.</param>
    /// <returns>The absolute path to the default global packages folder.</returns>
    internal static string GetDefaultGlobalPackagesFolder(string? userProfile)
    {
        var resolved = TextHelpers.HasValue(userProfile)
            ? userProfile
            : Directory.GetCurrentDirectory();

        return Path.Combine(resolved, PathSeparatorHelpers.ToPlatformPath(DefaultRelativePath));
    }
}
