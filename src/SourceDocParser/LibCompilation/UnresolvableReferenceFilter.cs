// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using ICSharpCode.Decompiler.Metadata;

namespace SourceDocParser.LibCompilation;

/// <summary>
/// Decides which assembly references the resolver should silently skip
/// rather than log as <c>Unable to resolve assembly reference</c>.
/// Two classes of refs land here:
/// <list type="bullet">
/// <item>
///   <description>Compiler / source-generator stubs that carry sentinel
///   versions (<c>0.0.0.0</c> from MSIL stubs, <c>255.255.255.255</c>
///   from Uno's design-time projections). These are guaranteed never
///   to ship as a real assembly anywhere, so resolving them is
///   impossible by definition.</description>
/// </item>
/// <item>
///   <description>Platform / SDK / workload reference assemblies
///   (<c>Java.Interop</c>, <c>Microsoft.iOS</c>, <c>PresentationCore</c>,
///   <c>Microsoft.WinUI</c>, ASP.NET Core targeting refs, legacy WCF
///   internals, and friends). These ship via <c>dotnet workload</c>
///   installs or .NET SDK ref packs rather than NuGet, so they're
///   never going to be in the package fallback index. Logging them
///   produces thousands of noise entries that drown out the genuine
///   resolver problems.</description>
/// </item>
/// </list>
/// SDK-pack-based resolution is layered on top in <c>RefPackProbe</c>:
/// when those packs are present locally the resolver finds the DLL
/// through its fallback dir list and never reaches this filter, so
/// the prefix list here only kicks in for genuinely-unresolvable refs.
/// </summary>
internal static class UnresolvableReferenceFilter
{
    /// <summary>Compiler-stub sentinel value (e.g. ECMA-335 stub).</summary>
    private const int StubVersionZero = 0;

    /// <summary>Uno's design-time projection sentinel.</summary>
    private const int StubVersionMax = 255;

    /// <summary>
    /// Exact assembly names that we know aren't on NuGet and won't be
    /// in any SDK ref pack we can probe -- looked up via <see cref="HashSet{T}"/>
    /// for O(1) membership.
    /// </summary>
    private static readonly HashSet<string> ExactUnresolvable = new(StringComparer.Ordinal)
    {
        // Android workload synthetic refs
        "Java.Interop",
        "_Microsoft.Android.Resource.Designer",

        // iOS / macOS / Mac Catalyst workload refs
        "Microsoft.iOS",
        "Microsoft.macOS",
        "Microsoft.MacCatalyst",
        "Microsoft.tvOS",

        // WPF / WinForms framework refs (windowsdesktop SDK pack)
        "PresentationCore",
        "PresentationFramework",
        "PresentationUI",
        "System.Xaml",
        "System.Windows.Forms",
        "System.Windows.Controls.Ribbon",
        "System.Windows.Forms.Design",
        "System.Windows.Forms.Primitives",
        "UIAutomationProvider",
        "UIAutomationClient",
        "UIAutomationTypes",
        "UIAutomationClientSideProviders",
        "ReachFramework",
        "WindowsBase",
        "WindowsFormsIntegration",
        "System.IO.Packaging",
        "System.Drawing.Common",
        "System.Printing",

        // WinUI 3 / Windows App SDK / Windows SDK projections
        "Microsoft.WinUI",
        "WinRT.Runtime",
        "Microsoft.Windows.SDK.NET",
        "Microsoft.InteractiveExperiences.Projection",
        "Microsoft.Web.WebView2.Core.Projection",
        "Microsoft.Windows.UI.Xaml",
        "Microsoft.Windows.AppLifecycle.Projection",
        "Microsoft.Web.WebView2.Wpf",
        "Microsoft.Web.WebView2.Core",
        "Microsoft.Web.WebView2.WinForms",

        // Legacy desktop framework shims (only present on full .NET Framework)
        "SMDiagnostics",
        "System.ServiceModel.Internals",

        // Uno's design-time projections (in addition to the
        // 255.255.255.255 version sentinel filter).
        "Uno",
        "Uno.Foundation",
        "Uno.UI",
        "Uno.UI.Toolkit",

        // Windows API contract refs (UWP-era, never on NuGet).
        "Windows.Foundation.UniversalApiContract",
        "Windows.Foundation.FoundationContract",
    };

    /// <summary>
    /// Prefixes whose entire family ships outside NuGet -- bucketed
    /// here so we don't have to list every workload assembly variant.
    /// Match is OrdinalIgnoreCase against the start of the simple name.
    /// </summary>
    private static readonly string[] UnresolvablePrefixes =
    [
        "Microsoft.Android.",
        "_Microsoft.Android.",
        "Xamarin.Google.",
        "Xamarin.Android.",
        "Xamarin.iOS",
        "Xamarin.Mac",
        "Microsoft.AspNetCore.",
        "Microsoft.Maui.",
    ];

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="reference"/>
    /// is known not to be locatable through NuGet packages or SDK ref
    /// packs the caller might have probed -- callers should silently
    /// skip these and not log an unresolved-reference warning.
    /// </summary>
    /// <param name="reference">The assembly reference being resolved.</param>
    /// <returns>True when the reference is a stub or platform-only assembly.</returns>
    public static bool IsKnownUnresolvable(AssemblyReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return IsStubVersion(reference.Version) || IsKnownUnresolvableName(reference.Name);
    }

    /// <summary>
    /// Tests <paramref name="version"/> against the known sentinel
    /// values that compiler/source-generator output uses for never-
    /// resolvable refs.
    /// </summary>
    /// <param name="version">The assembly version (may be null).</param>
    /// <returns>True when the version matches a stub sentinel.</returns>
    public static bool IsStubVersion(Version? version) => version switch
    {
        null => false,
        { Major: StubVersionZero, Minor: StubVersionZero, Build: StubVersionZero, Revision: StubVersionZero } => true,
        { Major: StubVersionMax, Minor: StubVersionMax, Build: StubVersionMax, Revision: StubVersionMax } => true,
        _ => false,
    };

    /// <summary>
    /// Tests the simple <paramref name="assemblyName"/> against the
    /// exact-match set and prefix list.
    /// </summary>
    /// <param name="assemblyName">The simple assembly name (no extension).</param>
    /// <returns>True when the name matches a known platform/SDK ref.</returns>
    public static bool IsKnownUnresolvableName(string assemblyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);

        if (ExactUnresolvable.Contains(assemblyName))
        {
            return true;
        }

        var span = assemblyName.AsSpan();
        for (var i = 0; i < UnresolvablePrefixes.Length; i++)
        {
            if (span.StartsWith(UnresolvablePrefixes[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
