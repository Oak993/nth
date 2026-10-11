// PORT-ONLY: SK-13 / DL-15, skills-ONDEMAND-PLAN.md 2.5 and section 7 DL-15. The terms step of an item whose terms class is not open.
using System.Security.Cryptography;
using System.Text;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.State;
using Marrow.Core.Port.Skills.Terms;

namespace Marrow.Services.Port.Skills.Terms;

/// <summary>
/// Shows the licence text before the download, with a separate "I have read these terms" control, and stores the acceptance with the date and the licence file's hash (a later
/// pin with changed terms has a new hash, so it asks again: <see cref="ItemTerms"/>). The state is read through <c>state</c> and written through <c>accept</c> (the pipeline's
/// <c>AcceptTerms</c>, so state.json stays the one record). ENGINEERING DECISION (ED-339): the licence text comes from the item's own pinned licence file through
/// <see cref="ILicenceText"/> and is checked against the catalogue's size and SHA-256, so what the user reads is the file whose hash is stored; none of the item's other
/// files is fetched before the terms are accepted (<see cref="TermsGate"/>).
/// </summary>
public sealed class TermsStep(SkillsCatalogue catalogue, Func<AddonsState> state, Action<string, DateTimeOffset> accept, ILicenceText? text = null, TimeProvider? clock = null)
{
    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private readonly ILicenceText licence = text ?? new FailClosedLicenceText();
    private readonly TimeProvider time = clock ?? TimeProvider.System;

    private CatalogueItem Item(string id) => catalogue.Items.FirstOrDefault(i => i.Id == id) ?? throw new ArgumentException("Unknown catalogue item: " + id, nameof(id));

    /// <summary>The terms the item owes now, or null when it is open or its licence hash is already accepted.</summary>
    public TermsOffer? Offer(string id)
    {
        var item = Item(id);
        if (!ItemTerms.Required(item, state())) return null;
        return new TermsOffer(item.Id, item.Title, SkillsNotices.UnderTerms(item.Vendor), item.LicenceName, item.LicenceUrl, ItemTerms.LicenceHash(item), SkillsNotices.ReadControl);
    }

    /// <summary>The item's licence text: its pinned licence file, read through the seam and decoded after the size and hash match the catalogue.</summary>
    public async Task<string> LicenceTextAsync(string id, CancellationToken cancellation = default)
    {
        var item = Item(id);
        if (item.LicenceFile is null && PluginLicences.PerPlugin(item)) return await PerPluginTextAsync(item, cancellation).ConfigureAwait(false);
        var pinned = item.LicenceFile is { } path ? item.Files.FirstOrDefault(f => f.Path == path) : null;
        if (pinned is null) throw new InvalidOperationException("The catalogue holds no pinned licence file for " + id + " yet.");
        return Checked(await licence.ReadAsync(item, cancellation).ConfigureAwait(false), pinned);
    }

    /// <summary>
    /// ED-NEW-dl-13: the terms of a plugin item whose licences are recorded per plugin are all its licence files, each read through the seam, checked against its own pin and shown
    /// under a line naming the file, its licence and the plugins and skills it covers.
    /// </summary>
    private async Task<string> PerPluginTextAsync(CatalogueItem item, CancellationToken cancellation)
    {
        var text = new StringBuilder();
        foreach (var path in PluginLicences.Files(item))
        {
            var pinned = item.Files.FirstOrDefault(f => f.Path == path) ?? throw new InvalidOperationException("The catalogue holds no pinned licence file " + path + " for " + item.Id + ".");
            var plugins = item.Folders.Where(f => f.LicenceFile == path).Select(f => f.Name[(f.Name.LastIndexOf('/') + 1)..]).ToList();
            var skills = item.Skills.Where(s => s.LicenceFile == path).Select(s => "/" + (s.Name ?? s.Folder)).ToList();
            var name = item.Folders.FirstOrDefault(f => f.LicenceFile == path)?.LicenceName ?? item.Skills.First(s => s.LicenceFile == path).LicenceName;
            if (text.Length > 0) text.Append("\n\n");
            text.Append("—— ").Append(path).Append(" · ").Append(name).Append(" · ").Append(string.Join(", ", plugins.Concat(skills))).Append(" ——\n\n");
            text.Append(Checked(await licence.ReadFileAsync(item, path, cancellation).ConfigureAwait(false), pinned));
        }
        return text.ToString();
    }

    private static string Checked(byte[] bytes, CatalogueFile pinned)
    {
        if (bytes.LongLength != pinned.Size || !string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), pinned.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The licence file didn't match its checksum.");
        try { return Strict.GetString(bytes); }
        catch (DecoderFallbackException error) { throw new InvalidDataException("The licence file is not text.", error); }
    }

    /// <summary>
    /// Stores the acceptance. Refused (nothing stored) unless the separate read control was used, the item owes terms and its licence hash is known.
    /// </summary>
    public void Accept(string id, bool readControlChecked)
    {
        if (!readControlChecked) throw new InvalidOperationException("The terms are accepted only with \"" + SkillsNotices.ReadControl + "\".");
        var offer = Offer(id) ?? throw new InvalidOperationException("This item owes no terms.");
        if (!offer.CanAccept) throw new InvalidOperationException("The licence hash is not pinned yet; the terms cannot be accepted.");
        accept(id, time.GetUtcNow());
    }
}
