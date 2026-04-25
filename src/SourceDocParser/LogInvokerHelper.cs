// Copyright (c) 2019-2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace SourceDocParser;

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
    /// Invokes <paramref name="action"/> only when <paramref name="logger"/> is enabled at <paramref name="level"/>.
    /// </summary>
    /// <param name="logger">Target logger.</param>
    /// <param name="level">Log level to gate the invocation on.</param>
    /// <param name="action">Action to run with the logger when enabled.</param>
    public static void Invoke(ILogger logger, LogLevel level, Action<ILogger> action)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(action);
        if (!logger.IsEnabled(level))
        {
            return;
        }

        action(logger);
    }

    /// <summary>
    /// Invokes <paramref name="action"/> with one state argument when <paramref name="logger"/> is enabled at <paramref name="level"/>.
    /// </summary>
    /// <typeparam name="T1">Type of the state argument.</typeparam>
    /// <param name="logger">Target logger.</param>
    /// <param name="level">Log level to gate the invocation on.</param>
    /// <param name="arg1">State argument forwarded to the action.</param>
    /// <param name="action">Action to run with the logger and state when enabled.</param>
    public static void Invoke<T1>(ILogger logger, LogLevel level, T1 arg1, Action<ILogger, T1> action)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(action);
        if (!logger.IsEnabled(level))
        {
            return;
        }

        action(logger, arg1);
    }

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
    /// Invokes <paramref name="action"/> with three state arguments when <paramref name="logger"/> is enabled at <paramref name="level"/>.
    /// </summary>
    /// <typeparam name="T1">Type of the first state argument.</typeparam>
    /// <typeparam name="T2">Type of the second state argument.</typeparam>
    /// <typeparam name="T3">Type of the third state argument.</typeparam>
    /// <param name="logger">Target logger.</param>
    /// <param name="level">Log level to gate the invocation on.</param>
    /// <param name="arg1">First state argument.</param>
    /// <param name="arg2">Second state argument.</param>
    /// <param name="arg3">Third state argument.</param>
    /// <param name="action">Action to run with the logger and state when enabled.</param>
    public static void Invoke<T1, T2, T3>(ILogger logger, LogLevel level, T1 arg1, T2 arg2, T3 arg3, Action<ILogger, T1, T2, T3> action)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(action);
        if (!logger.IsEnabled(level))
        {
            return;
        }

        action(logger, arg1, arg2, arg3);
    }

    /// <summary>
    /// Invokes <paramref name="action"/> with four state arguments when <paramref name="logger"/> is enabled at <paramref name="level"/>.
    /// </summary>
    /// <typeparam name="T1">Type of the first state argument.</typeparam>
    /// <typeparam name="T2">Type of the second state argument.</typeparam>
    /// <typeparam name="T3">Type of the third state argument.</typeparam>
    /// <typeparam name="T4">Type of the fourth state argument.</typeparam>
    /// <param name="logger">Target logger.</param>
    /// <param name="level">Log level to gate the invocation on.</param>
    /// <param name="arg1">First state argument.</param>
    /// <param name="arg2">Second state argument.</param>
    /// <param name="arg3">Third state argument.</param>
    /// <param name="arg4">Fourth state argument.</param>
    /// <param name="action">Action to run with the logger and state when enabled.</param>
    public static void Invoke<T1, T2, T3, T4>(ILogger logger, LogLevel level, T1 arg1, T2 arg2, T3 arg3, T4 arg4, Action<ILogger, T1, T2, T3, T4> action)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(action);
        if (!logger.IsEnabled(level))
        {
            return;
        }

        action(logger, arg1, arg2, arg3, arg4);
    }
}
