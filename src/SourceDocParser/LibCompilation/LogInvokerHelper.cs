// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser.LibCompilation;

/// <summary>
/// Gates a log invocation behind <see cref="ILogger.IsEnabled"/> so any
/// expensive argument expressions (string joins, projections, ToString
/// calls) only run when the resulting log line is actually written.
/// </summary>
/// <remarks>
/// Use the static-lambda overloads with explicit state arguments to keep
/// the invocation allocation-free. Each overload mirrors the next; pick
/// the arity that matches the number of state values your action needs.
/// </remarks>
public static class LogInvokerHelper
{
    /// <summary>
    /// Invokes <paramref name="action"/> with two state arguments when <paramref name="logger"/> is enabled at <paramref name="level"/>.
    /// </summary>
    /// <typeparam name="T1">Type of the first state argument.</typeparam>
    /// <typeparam name="T2">Type of the second state argument.</typeparam>
    /// <param name="logger">Target logger.</param>
    /// <param name="level">Log level to gate the invocation on.</param>
    /// <param name="arg1">First state argument.</param>
    /// <param name="arg2">Second state argument.</param>
    /// <param name="action">Action to run with the logger and state when enabled.</param>
    public static void Invoke<T1, T2>(ILogger logger, LogLevel level, T1 arg1, T2 arg2, Action<ILogger, T1, T2> action)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(action);
        if (!logger.IsEnabled(level))
        {
            return;
        }

        action(logger, arg1, arg2);
    }

    /// <summary>
    /// Invokes <paramref name="action"/> with two state arguments and a projected third argument when
    /// <paramref name="logger"/> is enabled at <paramref name="level"/>.
    /// </summary>
    /// <typeparam name="T1">Type of the first state argument.</typeparam>
    /// <typeparam name="T2">Type of the second state argument.</typeparam>
    /// <typeparam name="T3">Type of the source argument to project.</typeparam>
    /// <typeparam name="TProjected">Type of the projected argument passed to the action.</typeparam>
    /// <param name="logger">Target logger.</param>
    /// <param name="level">Log level to gate the invocation on.</param>
    /// <param name="arg1">First state argument.</param>
    /// <param name="arg2">Second state argument.</param>
    /// <param name="arg3">Source argument to project only when logging is enabled.</param>
    /// <param name="projector">Projection invoked only when logging is enabled.</param>
    /// <param name="action">Action to run with the logger and projected state when enabled.</param>
    public static void Invoke<T1, T2, T3, TProjected>(
        ILogger logger,
        LogLevel level,
        T1 arg1,
        T2 arg2,
        T3 arg3,
        Func<T3, TProjected> projector,
        Action<ILogger, T1, T2, TProjected> action)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(projector);
        ArgumentNullException.ThrowIfNull(action);
        if (!logger.IsEnabled(level))
        {
            return;
        }

        action(logger, arg1, arg2, projector(arg3));
    }
}
