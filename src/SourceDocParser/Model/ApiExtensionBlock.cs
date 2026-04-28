// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Model;

/// <summary>
/// One C# 14 extension declaration declared on a static container --
/// e.g. <c>extension(string source) { public bool IsEmpty => ...; }</c>.
/// Captures the receiver parameter (its name and type) plus the
/// conceptual members declared inside the block. The compiler also
/// emits classic <c>[Extension]</c> static implementation methods
/// on the parent container; consumers that want the impl surface
/// can still walk the parent's regular members.
/// </summary>
/// <param name="ReceiverName">The extension parameter's name (e.g. <c>source</c>).</param>
/// <param name="Receiver">Reference to the receiver type.</param>
/// <param name="Members">Conceptual members declared inside the extension block (properties, methods, static factories).</param>
public sealed record ApiExtensionBlock(string ReceiverName, ApiTypeReference Receiver, ApiMember[] Members);
