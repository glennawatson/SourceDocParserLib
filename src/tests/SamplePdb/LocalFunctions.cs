// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SamplePdb;

/// <summary>Local function inside a regular method — exercises the angle-bracket name skip path.</summary>
public class LocalFunctions
{
    private const int InnerReturnValue = 7;

    /// <summary>Outer method that hosts a local function.</summary>
    /// <returns>The constant the local function returns.</returns>
    public int Outer()
    {
        return Inner();

        static int Inner() => InnerReturnValue;
    }
}
