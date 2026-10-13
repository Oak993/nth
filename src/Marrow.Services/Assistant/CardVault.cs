// PORT-ONLY: Dextra AS-20 (DESIGN.md rev 5, 12.7 "The card", "Where it is kept", "What the owner sees", "Windows Hello
// guards every change", "The model never sees the card", "The fill is bound to the merchant"; 16.1; ED-NEW-dextra-as20-7, -8).
// No Swift twin.
// The card, kept only in Windows Credential Manager (through the app's SecretStore, ED-093) and sealed first with DPAPI in
// user scope plus nth's own entropy (ICardSealer). Its last 4 digits are a second item, so showing them never opens the
// card. There is no reveal: nothing here returns the number, the expiry or the security code. The only way out is Fill,
// which asks Windows Hello itself, checks the page against the approved origin at that moment, types each part into the
// shop's own field through the fill path, and zeroes every buffer that held a part straight after (pinned, so the
// collector never leaves a copy behind). Saving, replacing and removing the card each ask Windows Hello first.
using System.Security.Cryptography;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Port;

namespace Marrow.Services.Assistant;

/// <summary>A part of the card the owner types.</summary>
public enum CardPart { Number, Expiry, Code }

/// <summary>Why typed card details cannot be saved yet.</summary>
public enum CardEntryProblem { Incomplete, BadNumber, BadMonth, PastExpiry, BadCode }

/// <summary>
/// The card as the owner types it, one digit at a time, into pinned buffers that are zeroed on Clear and Dispose. It never
/// becomes a string: only its last 4 digits can be read, once the number checks out.
/// </summary>
public sealed class CardEntry : IDisposable
{
    public const int NumberMin = 12;
    public const int NumberMax = 19;
    public const int ExpiryDigits = 4;
    public const int CodeMin = 3;
    public const int CodeMax = 4;

    private readonly char[] number = GC.AllocateArray<char>(NumberMax, pinned: true);
    private readonly char[] expiry = GC.AllocateArray<char>(ExpiryDigits, pinned: true);
    private readonly char[] code = GC.AllocateArray<char>(CodeMax, pinned: true);
    private int numberLength, expiryLength, codeLength;
    private bool disposed;

    /// <summary>Raised after every typed or erased digit.</summary>
    public event Action? Changed;

    public int Length(CardPart part) => part switch
    {
        CardPart.Number => numberLength,
        CardPart.Expiry => expiryLength,
        _ => codeLength,
    };

    public static int Capacity(CardPart part) => part switch
    {
        CardPart.Number => NumberMax,
        CardPart.Expiry => ExpiryDigits,
        _ => CodeMax,
    };

    /// <summary>Adds one digit to a part; false (and nothing kept) for anything that is not 0-9, or a full part.</summary>
    public bool Type(CardPart part, char digit)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (digit is < '0' or > '9') return false;
        var (buffer, length) = Of(part);
        if (length >= buffer.Length) return false;
        buffer[length] = digit;
        SetLength(part, length + 1);
        Changed?.Invoke();
        return true;
    }

    /// <summary>Takes the last digit off a part (Backspace).</summary>
    public void Erase(CardPart part)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var (buffer, length) = Of(part);
        if (length == 0) return;
        buffer[length - 1] = '\0';
        SetLength(part, length - 1);
        Changed?.Invoke();
    }

    /// <summary>Zeroes every part.</summary>
    public void Clear()
    {
        Array.Clear(number);
        Array.Clear(expiry);
        Array.Clear(code);
        numberLength = expiryLength = codeLength = 0;
        if (!disposed) Changed?.Invoke();
    }

    /// <summary>Null when the card can be saved; else the first problem. The expiry is MMYY, good through its month.</summary>
    public CardEntryProblem? Problem(DateTimeOffset now)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (numberLength < NumberMin || expiryLength < ExpiryDigits || codeLength < CodeMin) return CardEntryProblem.Incomplete;
        if (!CardDigits.PassesLuhn(number.AsSpan(0, numberLength))) return CardEntryProblem.BadNumber;
        var month = (expiry[0] - '0') * 10 + (expiry[1] - '0');
        var year = 2000 + (expiry[2] - '0') * 10 + (expiry[3] - '0');
        if (month is < 1 or > 12) return CardEntryProblem.BadMonth;
        if (year < now.Year || (year == now.Year && month < now.Month)) return CardEntryProblem.PastExpiry;
        return null;
    }

    /// <summary>The last 4 digits once the whole number checks out, else null.</summary>
    public string? LastFour => !disposed && numberLength >= NumberMin && CardDigits.PassesLuhn(number.AsSpan(0, numberLength))
        ? new string(number, numberLength - 4, 4)
        : null;

    internal ReadOnlySpan<char> Digits(CardPart part)
    {
        var (buffer, length) = Of(part);
        return buffer.AsSpan(0, length);
    }

    /// <summary>The buffers themselves, for the memory scan test.</summary>
    internal IReadOnlyList<char[]> Buffers => [number, expiry, code];

    public void Dispose()
    {
        if (disposed) return;
        Clear();
        disposed = true;
    }

    private (char[] Buffer, int Length) Of(CardPart part) => part switch
    {
        CardPart.Number => (number, numberLength),
        CardPart.Expiry => (expiry, expiryLength),
        _ => (code, codeLength),
    };

    private void SetLength(CardPart part, int length)
    {
        switch (part)
        {
            case CardPart.Number: numberLength = length; break;
            case CardPart.Expiry: expiryLength = length; break;
            default: codeLength = length; break;
        }
    }
}

/// <summary>The Luhn check over digits in place (no string is made).</summary>
public static class CardDigits
{
    public static bool PassesLuhn(ReadOnlySpan<char> digits)
    {
        if (digits.Length == 0) return false;
        var sum = 0;
        var doubled = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var d = digits[i] - '0';
            if (d is < 0 or > 9) return false;
            if (doubled && (d *= 2) > 9) d -= 9;
            sum += d;
            doubled = !doubled;
        }
        return sum % 10 == 0;
    }
}

/// <summary>
/// The checkout page as the fill path (Visus's browser or Tactus's PC control) holds it. The vault reads it at the moment
/// of filling and types each part of the card into one field; the value is only valid during the call (its buffer is zeroed
/// after), so an implementation copies it straight into the field and keeps no copy.
/// </summary>
public interface ICheckoutPage
{
    /// <summary>The page now: its top frame's origin, and each field with its frame, form action, autocomplete and visibility.</summary>
    Task<CheckoutSnapshot> Snapshot(CancellationToken cancellationToken);

    /// <summary>Types the value into that field only. False when the field is gone or would not take it.</summary>
    Task<bool> Fill(string fieldId, ReadOnlyMemory<char> value, CancellationToken cancellationToken);

    /// <summary>
    /// Holds the page's agents (no input, no script from them) while the fill happens; disposing lets them go. Holds nest: the
    /// agents stay held until the last one is let go (the desk holds them from the fill until the order is placed).
    /// </summary>
    IDisposable HoldAgents();

    /// <summary>Places the order once the card is in (the shop's own submit). Unknown when the page cannot tell.</summary>
    Task<CheckoutOutcome> Submit(CancellationToken cancellationToken);
}

public enum CheckoutOutcome { Paid, NotPaid, Unknown }

/// <summary>Whether a card is kept.</summary>
public enum CardVaultState
{
    NoCard,
    Held,
    /// <summary>An item is there but nth cannot read its last 4 digits: the owner adds the card again.</summary>
    Damaged,
    /// <summary>Credential Manager refused to answer: nothing can be said or done until it does.</summary>
    Unreadable,
}

public enum CardVaultOutcome { Done, Invalid, NotVerified, NoCard, Damaged, Failed }

/// <summary>How a save or a removal went. <see cref="Hello"/> is the check's result when one ran.</summary>
public sealed record CardVaultResult(CardVaultOutcome Outcome, HelloResult? Hello = null, CardEntryProblem? Problem = null);

public enum CardFillOutcome { Filled, NotVerified, Refused, NoCard, Damaged, FieldRefused, Failed, Withdrawn }

/// <summary>How a fill went. Only <see cref="CardFillOutcome.Filled"/> put anything in the page.</summary>
public sealed record CardFillResult(CardFillOutcome Outcome, HelloResult? Hello = null, FillRefusal? Refusal = null)
{
    /// <summary>The ids of the fields nth filled (never what they hold), so a payment that does not go ahead can empty them.</summary>
    public ValueList<string> Fields { get; init; } = ValueList<string>.Empty;
}

public sealed class CardVault
{
    public const string DefaultService = "nth.dextra.card";
    private const string CardAccount = "card";
    private const string LastFourAccount = "last4";
    private const byte Version = 1;

    private readonly SecretStore secrets;
    private readonly ICardSealer sealer;
    private readonly HelloGuard hello;
    private readonly object gate = new();
    private CardVaultState state;
    private string? lastFour;

    /// <summary>A vault over a store and a seal (the tests'); production code takes <see cref="Production"/>.</summary>
    internal CardVault(SecretStore secrets, ICardSealer sealer, HelloGuard hello, string service = DefaultService)
    {
        this.secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        this.sealer = sealer ?? throw new ArgumentNullException(nameof(sealer));
        this.hello = hello ?? throw new ArgumentNullException(nameof(hello));
        Service = service;
        Refresh();
    }

    /// <summary>The production vault: the real Credential Manager and DPAPI with nth's entropy.</summary>
    public static CardVault Production(HelloGuard hello) => new(SecretStore.Shared.Current, DpapiCardSealer.Instance, hello);

    public string Service { get; }

    public CardVaultState State { get { lock (gate) return state; } }

    /// <summary>The card's last 4 digits, the only part of it nth ever shows; null with no readable card.</summary>
    public string? LastFour { get { lock (gate) return lastFour; } }

    /// <summary>Raised after the card was saved, replaced or removed.</summary>
    public event Action? Changed;

    /// <summary>Test seam: the buffers that held the card, right after a fill zeroed them (the memory scan).</summary>
    internal Action<IReadOnlyList<char[]>, IReadOnlyList<byte[]>>? Zeroed { get; set; }

    /// <summary>Reads what is kept (the last 4 digits only; the card itself is never opened here).</summary>
    public void Refresh()
    {
        CardVaultState next;
        string? four = null;
        var card = secrets.CopyMatching(Service, CardAccount, out var sealedCard);
        Zero(sealedCard);
        if (card == SecretStoreStatus.ItemNotFound) next = CardVaultState.NoCard;
        else if (card != SecretStoreStatus.Success) next = CardVaultState.Unreadable;
        else
        {
            var label = secrets.CopyMatching(Service, LastFourAccount, out var digits);
            if (label == SecretStoreStatus.Success && digits is { Length: 4 } && digits.All(b => b is >= (byte)'0' and <= (byte)'9'))
            {
                four = System.Text.Encoding.ASCII.GetString(digits);
                next = CardVaultState.Held;
            }
            else next = label is SecretStoreStatus.Success or SecretStoreStatus.ItemNotFound ? CardVaultState.Damaged : CardVaultState.Unreadable;
        }
        lock (gate)
        {
            state = next;
            lastFour = four;
        }
    }

    /// <summary>
    /// Saves (or replaces) the card after Windows Hello. The typed parts are zeroed whether it is saved or not, except when
    /// the details do not check out (then the owner can correct them) or the check did not pass (he can try again).
    /// </summary>
    public async Task<CardVaultResult> Save(CardEntry entry, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Problem(now) is { } problem) return new CardVaultResult(CardVaultOutcome.Invalid, Problem: problem);
        var verdict = await hello.Check(State == CardVaultState.Held ? "replace the card" : "save the card", cancellationToken).ConfigureAwait(false);
        if (verdict != HelloResult.Verified) return new CardVaultResult(CardVaultOutcome.NotVerified, verdict);
        if (entry.Problem(now) is { } changed) return new CardVaultResult(CardVaultOutcome.Invalid, verdict, changed);
        var four = entry.LastFour!;
        var payload = Payload(entry);
        byte[]? sealedCard = null;
        try
        {
            sealedCard = sealer.Seal(payload);
            Zero(payload);
            var written = Put(CardAccount, sealedCard);
            if (written != SecretStoreStatus.Success) return new CardVaultResult(CardVaultOutcome.Failed, verdict);
            if (Put(LastFourAccount, System.Text.Encoding.ASCII.GetBytes(four)) != SecretStoreStatus.Success)
            {
                // Never a card without its label: the half-saved card is taken out again.
                secrets.Delete(Service, CardAccount);
                Refresh();
                return new CardVaultResult(CardVaultOutcome.Failed, verdict);
            }
        }
        catch (CryptographicException) { return new CardVaultResult(CardVaultOutcome.Failed, verdict); }
        finally
        {
            Zero(payload);
            Zero(sealedCard);
        }
        entry.Clear();
        Refresh();
        Changed?.Invoke();
        return new CardVaultResult(CardVaultOutcome.Done, verdict);
    }

    /// <summary>Removes the card from Credential Manager after Windows Hello.</summary>
    public async Task<CardVaultResult> Remove(CancellationToken cancellationToken = default)
    {
        if (State == CardVaultState.NoCard) return new CardVaultResult(CardVaultOutcome.NoCard);
        var verdict = await hello.Check("remove the card", cancellationToken).ConfigureAwait(false);
        if (verdict != HelloResult.Verified) return new CardVaultResult(CardVaultOutcome.NotVerified, verdict);
        var card = secrets.Delete(Service, CardAccount);
        var label = secrets.Delete(Service, LastFourAccount);
        Refresh();
        Changed?.Invoke();
        var gone = card is SecretStoreStatus.Success or SecretStoreStatus.ItemNotFound && label is SecretStoreStatus.Success or SecretStoreStatus.ItemNotFound;
        return new CardVaultResult(gone ? CardVaultOutcome.Done : CardVaultOutcome.Failed, verdict);
    }

    /// <summary>
    /// Fills the card into the checkout: Windows Hello first (<paramref name="reason"/> is what the prompt says), then, with
    /// the page's agents held, the page as it is at that moment must pass the merchant binding for
    /// <paramref name="approvedOrigin"/> and <paramref name="approvedTotal"/>. Each part goes into its own field; every
    /// buffer that held one is zeroed before this returns, whatever happened.
    /// </summary>
    public async Task<CardFillResult> Fill(ICheckoutPage page, string approvedOrigin, decimal approvedTotal, string reason, CancellationToken cancellationToken = default,
        Func<bool>? stillWanted = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (State == CardVaultState.NoCard) return new CardFillResult(CardFillOutcome.NoCard);
        if (State != CardVaultState.Held) return new CardFillResult(CardFillOutcome.Damaged);
        var verdict = await hello.Check(reason, cancellationToken).ConfigureAwait(false);
        if (verdict != HelloResult.Verified) return new CardFillResult(CardFillOutcome.NotVerified, verdict);
        // While Windows asked, the card may have been stopped, withdrawn or turned off: then nothing is filled.
        if (stillWanted?.Invoke() == false) return new CardFillResult(CardFillOutcome.Withdrawn, verdict);
        using var held = page.HoldAgents();
        CheckoutSnapshot snapshot;
        try { snapshot = await page.Snapshot(cancellationToken).ConfigureAwait(false); }
        catch (Exception error) when (error is not OutOfMemoryException) { return new CardFillResult(CardFillOutcome.Failed, verdict); }
        var (plan, refusal) = CheckoutBoundary.Check(approvedOrigin, approvedTotal, snapshot);
        if (refusal is not null) return new CardFillResult(CardFillOutcome.Refused, verdict, refusal);

        var status = secrets.CopyMatching(Service, CardAccount, out var sealedCard);
        if (status != SecretStoreStatus.Success || sealedCard is null) return new CardFillResult(CardFillOutcome.Damaged, verdict);
        byte[]? opened = null;
        var value = GC.AllocateArray<char>(CardEntry.NumberMax + 2, pinned: true);
        try
        {
            opened = sealer.Open(sealedCard);
            Zero(sealedCard);
            if (opened is null || !Parts(opened, out var numberAt, out var numberLength, out var expiryAt, out var codeAt, out var codeLength))
                return new CardFillResult(CardFillOutcome.Damaged, verdict);
            var taken = new List<string>();
            foreach (var (field, kind) in plan!.Fields)
            {
                Array.Clear(value);
                var length = kind switch
                {
                    CardFieldKind.Number => Copy(opened, numberAt, numberLength, value, 0),
                    CardFieldKind.Expiry => Expiry(opened, expiryAt, value),
                    CardFieldKind.ExpiryMonth => Copy(opened, expiryAt, 2, value, 0),
                    CardFieldKind.ExpiryYear => Year(opened, expiryAt, value),
                    _ => Copy(opened, codeAt, codeLength, value, 0),
                };
                bool took;
                try { took = await page.Fill(field, value.AsMemory(0, length), cancellationToken).ConfigureAwait(false); }
                catch (Exception error) when (error is not OutOfMemoryException) { took = false; }
                Array.Clear(value);
                if (!took)
                {
                    // A field that refused the card leaves none of it behind: the ones already filled are emptied.
                    await Clear(page, taken, cancellationToken).ConfigureAwait(false);
                    return new CardFillResult(CardFillOutcome.FieldRefused, verdict);
                }
                taken.Add(field);
            }
            return new CardFillResult(CardFillOutcome.Filled, verdict) { Fields = new ValueList<string>(taken) };
        }
        catch (CryptographicException) { return new CardFillResult(CardFillOutcome.Damaged, verdict); }
        finally
        {
            Array.Clear(value);
            Zero(opened);
            Zero(sealedCard);
            Zeroed?.Invoke([value], opened is null ? [] : [opened]);
        }
    }

    /// <summary>
    /// Empties the card fields nth filled (a payment that did not go ahead, or a field that refused the card). False when a
    /// field could not be emptied; the agents stay held by the caller meanwhile.
    /// </summary>
    public static async Task<bool> Clear(ICheckoutPage page, IEnumerable<string> fields, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(fields);
        var all = true;
        foreach (var field in fields)
        {
            try { all &= await page.Fill(field, ReadOnlyMemory<char>.Empty, cancellationToken).ConfigureAwait(false); }
            catch (Exception error) when (error is not OutOfMemoryException) { all = false; }
        }
        return all;
    }

    // [version][n][n digits][MMYY][c][c digits], ASCII, in a pinned array.
    private static byte[] Payload(CardEntry entry)
    {
        var number = entry.Digits(CardPart.Number);
        var expiry = entry.Digits(CardPart.Expiry);
        var code = entry.Digits(CardPart.Code);
        var payload = GC.AllocateArray<byte>(1 + 1 + number.Length + expiry.Length + 1 + code.Length, pinned: true);
        var at = 0;
        payload[at++] = Version;
        payload[at++] = (byte)number.Length;
        foreach (var c in number) payload[at++] = (byte)c;
        foreach (var c in expiry) payload[at++] = (byte)c;
        payload[at++] = (byte)code.Length;
        foreach (var c in code) payload[at++] = (byte)c;
        return payload;
    }

    private static bool Parts(byte[] payload, out int numberAt, out int numberLength, out int expiryAt, out int codeAt, out int codeLength)
    {
        numberAt = 2;
        numberLength = payload.Length > 1 ? payload[1] : 0;
        expiryAt = numberAt + numberLength;
        codeAt = expiryAt + CardEntry.ExpiryDigits + 1;
        codeLength = payload.Length > codeAt - 1 && codeAt - 1 >= 0 ? payload[codeAt - 1] : 0;
        return payload.Length >= 2 && payload[0] == Version && numberLength is >= CardEntry.NumberMin and <= CardEntry.NumberMax
            && codeLength is >= CardEntry.CodeMin and <= CardEntry.CodeMax && payload.Length == codeAt + codeLength;
    }

    private static int Copy(byte[] from, int at, int length, char[] to, int into)
    {
        for (var i = 0; i < length; i++) to[into + i] = (char)from[at + i];
        return into + length;
    }

    private static int Expiry(byte[] from, int at, char[] to)
    {
        Copy(from, at, 2, to, 0);
        to[2] = '/';
        return Copy(from, at + 2, 2, to, 3);
    }

    private static int Year(byte[] from, int at, char[] to)
    {
        to[0] = '2';
        to[1] = '0';
        return Copy(from, at + 2, 2, to, 2);
    }

    private SecretStoreStatus Put(string account, byte[] data)
    {
        var status = secrets.Update(Service, account, data);
        return status == SecretStoreStatus.ItemNotFound ? secrets.Add(Service, account, data) : status;
    }

    private static void Zero(byte[]? data)
    {
        if (data is not null) CryptographicOperations.ZeroMemory(data);
    }
}
