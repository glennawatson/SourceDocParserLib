// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.Model;

/// <summary>
/// The Invoke signature of an <see cref="ApiDelegateType"/> -- return
/// type, parameters, and any generic type parameters declared on the
/// delegate itself. The emitter renders this directly on the delegate's
/// type page; no per-overload pages are produced for delegate types.
/// </summary>
/// <param name="Signature">Pre-formatted display signature (e.g. <c>void EventHandler(object sender, EventArgs e)</c>).</param>
/// <param name="ReturnType">Reference to the return type, or <see langword="null"/> for <c>void</c> returns.</param>
/// <param name="Parameters">Invoke parameters in declaration order.</param>
/// <param name="TypeParameters">Generic type parameter names (empty for non-generic delegates).</param>
public sealed record ApiDelegateSignature(
    string Signature,
    ApiTypeReference? ReturnType,
    ApiParameter[] Parameters,
    string[] TypeParameters);
