// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.NuGet;

/// <summary>
/// Cut-down implementation of NuGet's global-packages-folder
/// resolution. Mirrors the algorithm <c>SettingsUtility.GetGlobalPackagesFolder</c>
/// uses (see <c>NuGet.Client/src/NuGet.Core/NuGet.Configuration/Utility/SettingsUtility.cs</c>)
/// without dragging in <c>NuGet.Configuration</c> + <c>NuGet.Packaging</c>
/// + <c>NuGet.Protocol</c> (and through them <c>Newtonsoft.Json</c>,
/// dual JSON stacks on net472, <c>Cryptography.Pkcs</c>, etc.).
/// Each helper does one thing so the wider fetcher can compose them
/// and the tests can pin each in isolation.
/// </summary>
internal static class NuGetGlobalCache
{
    /// <summary>NuGet's environment-variable override for the global packages folder.</summary>
    internal const string GlobalPackagesFolderEnvVar = "NUGET_PACKAGES";

    /// <summary>Tail of the platform-default path under the user profile.</summary>
    private const string DefaultRelativePath = ".nuget/packages";

    /// <summary>Sentinel file NuGet writes after a successful extraction — its presence means "no need to re-download / re-extract".</summary>
    private const string ExtractionMarkerFileName = ".nupkg.metadata";

    /// <summary>The lib subfolder under each per-package directory.</summary>
    private const string LibFolderName = "lib";

    /// <summary>
    /// Returns the path to NuGet's global packages folder using
    /// the same precedence chain the SDK does:
    /// <c>NUGET_PACKAGES</c> env var → <c>nuget.config</c>
    /// <c>globalPackagesFolder</c> setting (when <paramref name="configOverride"/>
    /// is supplied) → platform default
    /// (<c>~/.nuget/packages</c> on Unix, <c>%USERPROFILE%\.nuget\packages</c>
    /// on Windows). The optional override is the resolved value from
    /// <see cref="NuGetConfigReader.ReadGlobalPackagesFolderAsync(string, CancellationToken)"/>;
    /// callers that don't care about config-file overrides pass
    /// <see langword="null"/>.
    /// </summary>
    /// <param name="configOverride">Pre-resolved value from a <c>nuget.config</c>, or <see langword="null"/> when none.</param>
    /// <returns>The absolute path to the global packages folder.</returns>
    public static string ResolveGlobalPackagesFolder(string? configOverride = null)
    {
        var envValue = Environment.GetEnvironmentVariable(GlobalPackagesFolderEnvVar);
        if (TextHelpers.HasNonWhitespace(envValue))
        {
            return envValue;
        }

        if (TextHelpers.HasNonWhitespace(configOverride))
        {
            return configOverride;
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!TextHelpers.HasValue(userProfile))
        {
            // Unix without HOME set is rare but happens in some
            // sandboxes; fall back to the same literal `.nuget/packages`
            // resolved against cwd so we still produce a usable path.
            userProfile = Directory.GetCurrentDirectory();
        }

        return Path.Combine(userProfile, DefaultRelativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>
    /// Returns the per-package install directory inside the global
    /// cache. NuGet lowercases both id and version when laying out
    /// the cache — the docs tooling has to match exactly so the
    /// "already extracted" probe finds the SDK-written marker.
    /// </summary>
    /// <param name="globalPackagesFolder">Result of <see cref="ResolveGlobalPackagesFolder"/>.</param>
    /// <param name="packageId">NuGet package ID.</param>
    /// <param name="packageVersion">NuGet package version (use the normalised form).</param>
    /// <returns>The absolute install path.</returns>
    public static string GetPackageInstallPath(string globalPackagesFolder, string packageId, string packageVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(globalPackagesFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);
        return Path.Combine(globalPackagesFolder, packageId.ToLowerInvariant(), packageVersion.ToLowerInvariant());
    }

    /// <summary>
    /// Returns the absolute path to the per-TFM lib directory under
    /// <paramref name="packageInstallPath"/> — i.e.
    /// <c>&lt;packageInstallPath&gt;/lib/&lt;tfm&gt;/</c>. Doesn't
    /// check existence; pair with <see cref="Directory.Exists(string)"/>
    /// at the call site to detect packages that don't ship a lib
    /// folder for the requested TFM.
    /// </summary>
    /// <param name="packageInstallPath">Result of <see cref="GetPackageInstallPath"/>.</param>
    /// <param name="tfm">Short TFM identifier (e.g. <c>net8.0</c>, <c>net472</c>).</param>
    /// <returns>The absolute lib/TFM path.</returns>
    public static string GetLibTfmPath(string packageInstallPath, string tfm)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageInstallPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(tfm);
        return Path.Combine(packageInstallPath, LibFolderName, tfm);
    }

    /// <summary>
    /// Returns true when the SDK-written extraction marker file is
    /// present in <paramref name="packageInstallPath"/>. NuGet uses
    /// <c>.nupkg.metadata</c> as the canonical "fully extracted"
    /// signal — added in NuGet 4.x specifically because the older
    /// <c>.sha512</c> sidecar wasn't sufficient on its own.
    /// </summary>
    /// <param name="packageInstallPath">Result of <see cref="GetPackageInstallPath"/>.</param>
    /// <returns>True when the package has been fully installed.</returns>
    public static bool IsPackageInstalled(string packageInstallPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageInstallPath);
        return File.Exists(Path.Combine(packageInstallPath, ExtractionMarkerFileName));
    }

    /// <summary>
    /// Returns the standard locations (in precedence order) to
    /// search for a user-scoped <c>nuget.config</c>. Caller layers
    /// walk-from-cwd on top and machine-scoped underneath.
    /// </summary>
    /// <returns>Candidate nuget.config paths in precedence order.</returns>
    public static string[] GetUserNuGetConfigPaths()
    {
        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!TextHelpers.HasValue(appData))
            {
                return [];
            }

            return
            [
                Path.Combine(appData, "NuGet", "NuGet.Config"),
            ];
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!TextHelpers.HasValue(userProfile))
        {
            return [];
        }

        return
        [
            Path.Combine(userProfile, ".nuget", "NuGet", "NuGet.Config"),
            Path.Combine(userProfile, ".config", "NuGet", "NuGet.Config"),
        ];
    }

    /// <summary>
    /// Returns every <c>*.config</c> file under the machine-wide
    /// NuGet config root — NuGet recursively reads them all so
    /// admins can drop multiple files alongside the default
    /// (e.g. <c>NuGetDefaults.Config</c>). Windows uses
    /// <c>%ProgramData%\NuGet\Config\</c>; Unix uses
    /// <c>/etc/opt/NuGet/Config/</c>.
    /// </summary>
    /// <returns>Existing <c>*.config</c> paths under the machine root, sorted ordinal.</returns>
    public static string[] GetMachineNuGetConfigPaths()
    {
        var root = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NuGet", "Config")
            : "/etc/opt/NuGet/Config";

        if (!TextHelpers.HasValue(root) || !Directory.Exists(root))
        {
            return [];
        }

        var files = Directory.GetFiles(root, "*.config", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.Ordinal);
        return files;
    }
}
