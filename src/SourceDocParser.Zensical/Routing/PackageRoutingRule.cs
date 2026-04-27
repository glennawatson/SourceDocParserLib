// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Zensical.Routing;

/// <summary>
/// Represents a routing rule used to determine the folder mapping
/// for an assembly during the documentation generation process.
/// A <see cref="PackageRoutingRule"/> includes the name of the folder
/// and an assembly name prefix. If the prefix matches an assembly,
/// that assembly's types will be routed to the specified folder.
/// </summary>
/// <remarks>
/// This rule is commonly used in conjunction with the
/// <see cref="PackageRouter"/> class to organize assemblies into
/// specific package folders during document emission. The order of
/// the rules is significant, with the first matching rule applied.
/// </remarks>
public readonly record struct PackageRoutingRule(string FolderName, string AssemblyPrefix);
