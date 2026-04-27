// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace System.Runtime.CompilerServices;

/// <summary>
/// Stand-in for the upcoming <c>System.Runtime.CompilerServices.IUnion</c>
/// marker the C# 15 closed-hierarchy / discriminated-union feature
/// will ship. The walker keys its <c>IsUnion</c> probe off the
/// fully-qualified name of this interface, so declaring it locally
/// in the SamplePdb fixture lets the union-case capture path run
/// end-to-end without waiting for the BCL ship.
/// </summary>
public interface IUnion;
