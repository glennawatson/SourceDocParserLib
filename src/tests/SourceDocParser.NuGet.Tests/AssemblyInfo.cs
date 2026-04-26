// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

// The NuGet test suite is heavy on disk I/O (zip extraction, sidecar
// reads, env-var-mutating discovery walks). Run the whole assembly
// serially so concurrent tests don't fight over /tmp scratch dirs or
// the NUGET_PACKAGES env var.
[assembly: TUnit.Core.NotInParallel]
