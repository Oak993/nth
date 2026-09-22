using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Marrow.Core.Port;

/// <summary>
/// PORT-ONLY. A Swift runtime trap: a precondition failure, a failed <c>MainActor.assumeIsolated</c>, a
/// <c>Mutex</c> re-entry, a second continuation resume, a force unwrap of nil. Swift cannot catch a trap; the
/// process ends. No port of <c>try?</c>, <c>do/catch</c> or <c>catch { return }</c> may catch this (ED-078).
/// </summary>
public sealed class SwiftTrapException(string message) : Exception(message);

/// <summary>Swift's trapping operators that have no C# operator.</summary>
public static class SwiftTraps
{
    /// <summary><c>x!</c> on an optional reference: traps on nil.</summary>
    public static T Unwrap<T>(T? value, [CallerArgumentExpression(nameof(value))] string expression = "") where T : class =>
        value ?? throw new SwiftTrapException($"Unexpectedly found nil while unwrapping {expression}.");

    /// <summary><c>x!</c> on an optional value type: traps on nil.</summary>
    public static T Unwrap<T>(T? value, [CallerArgumentExpression(nameof(value))] string expression = "") where T : struct =>
        value ?? throw new SwiftTrapException($"Unexpectedly found nil while unwrapping {expression}.");

    /// <summary><c>fatalError(message)</c> and <c>preconditionFailure(message)</c>.</summary>
    public static SwiftTrapException Fatal(string message) => new(message);
}

/// <summary>
/// Swift <c>try?</c>: any THROWN error becomes nil, cancellation included (Swift's try? swallows
/// CancellationError too). A trap is not an error and always propagates, so the .NET exceptions that stand
/// for traps are never caught here. A bare <c>catch (Exception)</c> would turn a crash Swift can never
/// survive into a silent nil. Source: drafts A and C (union of their trap lists).
/// </summary>
public static class SwiftTry
{
    /// <summary>True for an exception that stands for a Swift <c>throw</c>; false for one that stands for a trap.</summary>
    public static bool IsThrownError(Exception error) => error is not (SwiftTrapException or OverflowException or
        IndexOutOfRangeException or ArgumentOutOfRangeException or NullReferenceException or InvalidCastException or
        DivideByZeroException or KeyNotFoundException or UnreachableException or SwitchExpressionException or
        OutOfMemoryException or InsufficientExecutionStackException);

    /// <summary><c>try? f()</c> for a reference result.</summary>
    public static T? Optional<T>(Func<T?> body) where T : class
    {
        try { return body(); }
        catch (Exception error) when (IsThrownError(error)) { return null; }
    }

    /// <summary><c>try? f()</c> for a value result.</summary>
    public static T? OptionalValue<T>(Func<T> body) where T : struct
    {
        try { return body(); }
        catch (Exception error) when (IsThrownError(error)) { return null; }
    }

    /// <summary><c>try? f()</c> whose result is not used.</summary>
    public static void Ignore(Action body)
    {
        try { body(); }
        catch (Exception error) when (IsThrownError(error)) { }
    }

    /// <summary><c>try? await f()</c> for a reference result.</summary>
    public static async Task<T?> OptionalAsync<T>(Func<Task<T?>> body) where T : class
    {
        try { return await body(); }
        catch (Exception error) when (IsThrownError(error)) { return null; }
    }

    /// <summary><c>try? await f()</c> for a value result.</summary>
    public static async Task<T?> OptionalValueAsync<T>(Func<Task<T>> body) where T : struct
    {
        try { return await body(); }
        catch (Exception error) when (IsThrownError(error)) { return null; }
    }

    /// <summary><c>try? await f()</c> whose result is not used.</summary>
    public static async Task IgnoreAsync(Func<Task> body)
    {
        try { await body(); }
        catch (Exception error) when (IsThrownError(error)) { }
    }
}

/// <summary>A Swift <c>LocalizedError</c>. <see cref="ErrorDescription"/> is user-visible text, byte for byte.</summary>
public interface ILocalizedError
{
    string? ErrorDescription { get; }
}

/// <summary>The identity Foundation prints for a Swift error that has no description.</summary>
public interface ISwiftErrorIdentity
{
    /// <summary>"Module.Type", for example "Swift.CancellationError".</summary>
    string Domain { get; }
    long Code { get; }
}

/// <summary>Base for every error type ported from Swift. Message is the Swift description.</summary>
public abstract class SwiftError : Exception, ILocalizedError
{
    public abstract string? ErrorDescription { get; }

    public override string Message => ErrorDescription ?? (this is ISwiftErrorIdentity ? SwiftErrors.LocalizedDescription(this) : GetType().Name);
}

/// <summary>Swift <c>EncodingError</c>: what JSONEncoder throws, for example for a non-finite Double.</summary>
public sealed class EncodingError(string message) : Exception(message);

public static class SwiftErrors
{
    /// <summary>Foundation's generic text. U+2019 RIGHT SINGLE QUOTATION MARK, not an apostrophe.</summary>
    public const string CouldNotComplete = "The operation couldn\U00002019t be completed."; // U+2019 RIGHT SINGLE QUOTATION MARK

    /// <summary>
    /// <c>error.localizedDescription</c>: a LocalizedError's description; for an error without one, Foundation's
    /// "(Domain error Code.)" text (UNVERIFIED O-14). Any other .NET exception stands in for an NSError whose
    /// Apple text Windows cannot reproduce, so this throws instead of inventing text: each such site needs a
    /// recorded decision (section 6 of IMPLEMENTING.md).
    /// </summary>
    public static string LocalizedDescription(Exception error) => error switch
    {
        ILocalizedError { ErrorDescription: { } description } => description,
        ISwiftErrorIdentity identity => FormattableString.Invariant($"{CouldNotComplete} ({identity.Domain} error {identity.Code}.)"),
        _ => throw new NotSupportedException($"No Swift text is defined for {error.GetType().Name}; record a per-site decision."),
    };
}
