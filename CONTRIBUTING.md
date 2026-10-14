# Contributing to nth

Thank you for helping with nth. Read this page before your first contribution. It has five parts: how to start, the
rules for every contribution, what happens to it, the nth Contributor License Agreement (CLA) that every contributor
signs once, and a signature block.

## 1. Before your first contribution

1. **Sign the CLA (section 4).** oak993 accepts no patch, pull request, file, image or design from anyone who has not
   signed it, including family and friends. Sign in one of these ways, before you send anything:
   - sign the signature block in section 5 (by hand, then a photo or PDF, or with an electronic signature) and send it
     to oak993; or
   - post this exact sentence on your first pull request: `I have read the nth CLA version 1.0 and I hereby sign it.`,
     and send oak993 privately your legal name, the country you live in and the email address you use for commits.
   oak993 keeps signatures privately and does not publish your legal name.
2. If an employer, a client or a school may own what you create, get their written permission first (CLA section 7(a)).
   If you are under the age of majority where you live, a parent or guardian must sign with you.
3. Read `docs/next-release/README.md` on branch `next-release`: branches, how to hand in work, build, tests and the
   checklist.
4. Set the name and email that will be public in the Git history before you commit, for example your GitHub handle
   and your GitHub no-reply address: `git config user.name` and `git config user.email`. The author line of every
   commit and patch becomes public when it is merged.

Every patch or pull request you send is a Contribution under the CLA you signed. Put this line in the note that comes
with each patch: `This is a Contribution under the nth CLA version 1.0, which I signed on <date>.`

## 2. Rules for every contribution

**License.** Everything you contribute is published under the GNU AGPL v3.0 with the additional terms in `LICENSE`,
and is licensed to oak993 under the CLA. Do not contribute anything that you cannot license that way.

**Sign off every commit.** Use `git commit -s`. The `Signed-off-by:` line certifies the Developer Certificate of
Origin 1.1 (https://developercertificate.org/) and confirms that the commit is a Contribution under the CLA. You may
sign off with the name you are known by on GitHub and your no-reply address.

**Your own work.** Contribute only what you made yourself, or material whose license allows it and that you list. Never
copy code, text, images, icons, fonts or sounds from another project, a website, a tutorial, a forum answer, your
employer, a client or one of your own closed products, unless:

- you name its source, author and license in your note or pull request;
- its license is MIT, BSD, ISC, Apache 2.0, zlib, Unicode, or SIL OFL for fonts. Code under GPL, LGPL, AGPL, MPL or
  any other copyleft license needs oak993's written approval first. GPL-2.0-only code cannot be used at all; and
- you add its notice to `THIRD_PARTY_NOTICES.md` in the same contribution.

Never contribute material that has no license, or whose license is non-commercial, no-derivatives, "all rights
reserved" or otherwise proprietary.

**AI tools.** You may use AI coding tools. Name the tools you used in your note or pull request. You must review and
understand everything you contribute, the tool's terms must allow you to contribute its output, and you must not
contribute output that you know or suspect copies someone else's code or text. You are responsible for the contribution
as if you had written every line yourself.

**No secrets and no personal data.** Never commit API keys, tokens, passwords, account names or plan details, personal
email addresses, paths that show a user name (write `C:/Users/<user>`), PC names, screenshots or recordings that show
other people's data, or private details of your own or anyone else's unreleased products. If you commit one by
mistake, tell oak993 privately at once; do not try to fix it by force-pushing.

**Brands.** Do not add logos or marketing images of Droppy or of any other company, and do not name a feature after
another company's product. See `TRADEMARK.md`.

**Keep the notices.** Never remove or change `LICENSE`, `NOTICE`, `TRADEMARK.md`, the `THIRD_PARTY_NOTICES*.md` files,
`src/Marrow.App/Legal/`, the copyright headers of source files, the `Source: DroppyCode/...` origin lines, the `// :97`
line references, or the credits in Settings > About.

**Security.** Report security problems privately as `SECURITY.md` says, never in an issue, a pull request or a patch
note.

**Building from source.** The .NET SDK and the Avalonia build tools send anonymous build-time telemetry to Microsoft and
to AvaloniaUI unless you set `DOTNET_CLI_TELEMETRY_OPTOUT=1` and `AVALONIA_TELEMETRY_OPTOUT=1`. The app you build does
not send telemetry.

## 3. What happens to your contribution

oak993 reviews every contribution and may ask for changes, change it after merging, or decline it. Merged contributions
are recorded in the Git history and, unless you ask otherwise, credited in the contributors list.

## 4. nth Individual Contributor License Agreement

Version 1.0, 14 October 2026

This agreement is between You, the person who signs it, and oak993. It sets out the terms on which You give Your
Contributions to nth. Read it carefully and ask any questions before You sign.

**1. Definitions**

"oak993" means the person who controls the GitHub account Oak993 and publishes nth at https://github.com/Oak993/nth,
and any successor under section 12.

"You" means the person identified by the legal name, GitHub account and email address in oak993's record of Your
signature.

"Contribution" means any work of authorship, for example code, tests, documentation, translations, designs, images,
icons, sounds, data, and changes to existing material, that You Submit to oak993 for nth, whether before or after You
sign this agreement. Material that You clearly mark in writing as "Not a Contribution" when You send it is not a
Contribution.

"Submit" means to send by any means to oak993 or to an nth repository, for nth: for example a patch, pull request,
commit, issue, comment, email, chat message or shared file.

"nth" means the software and other material that oak993 publishes or makes as nth, and any other product, version or
service that oak993 makes from it.

**2. You keep Your copyright.** You keep the copyright and other rights You have in Your Contributions. This agreement
gives licenses; it does not transfer ownership. You may use Your own Contributions in any other way, including in Your
own projects.

**3. Copyright license.** You grant oak993 a perpetual, worldwide, non-exclusive, no-charge, royalty-free and
irrevocable license to reproduce, adapt, translate, prepare derivative works of, publicly display, publicly perform,
distribute, make available and otherwise use Your Contributions and works based on them, in source and object form,
and to sublicense these rights through multiple tiers. oak993 may use and license Your Contributions under any terms it
chooses, including the GNU Affero General Public License v3.0, any other open-source license, and proprietary or
commercial licenses. This includes Your authorisation, for the purposes of section 7 of the GNU AGPL v3.0, for oak993
to apply to Your Contributions additional terms such as those in nth's `LICENSE` (attribution, origin, names and marks,
warranty and liability, publicity and indemnification).

**4. Patent license.** You grant oak993, and everyone who receives nth from oak993 or under a license from oak993, a
perpetual, worldwide, non-exclusive, no-charge, royalty-free and (except as stated in this section) irrevocable patent
license to make, have made, use, offer to sell, sell, import and otherwise transfer Your Contributions and nth as it
includes them. It covers only the patent claims that You can license and that are necessarily infringed by Your
Contribution alone or by its combination with nth as it was when You Submitted it. If anyone starts patent litigation
(including a cross-claim or counterclaim) alleging that Your Contribution, or nth as it includes it, infringes a
patent, the patent licenses that this section grants to that party for that Contribution end on the date the claim is
filed.

**5. Moral rights.** To the extent the law allows, You waive, and agree not to assert against oak993, its licensees or
its successors, any moral rights (for example the right to be named as the author, or to object to changes) in Your
Contributions, for the uses that this agreement allows. Where the law does not allow such a waiver or agreement, You
consent to those uses. This does not stop oak993 from crediting You (section 10).

**6. What this agreement does not do**

(a) oak993 does not have to use, merge or keep any Contribution, and may change or remove it.

(b) You are not owed payment, royalties, a share of income or any other compensation for Your Contributions, also if
oak993 earns money from nth, unless You and oak993 agree otherwise in a separate written agreement signed by both.

(c) This agreement does not make You an employee, partner, co-owner, joint venturer or agent of oak993. It gives You no
ownership of, or share in, nth as a whole, its names, its marks, its business or its income. You and oak993 do not
intend nth as a whole to be a joint work; Your rights are in Your Contributions.

(d) It gives You no rights in the names and marks listed in `TRADEMARK.md`. You will not register, or claim as Your own,
a trademark, company name, domain name, package name, app-store name or social-media handle that is nth's name or one of
its marks or is confusingly similar to them, unless oak993 agrees in writing.

(e) It does not limit the rights that the GNU AGPL v3.0 gives You in any copy of nth that You receive, in source or
object form.

**7. Your promises.** You promise that, for each Contribution:

(a) You have the legal right to grant the licenses in this agreement. If an employer, client, school or anyone else has
or may claim rights in what You create (for example under an employment contract or an intellectual-property policy),
You have their written permission to make the Contribution under this agreement, or they have signed an agreement with
oak993 for it.

(b) The Contribution is Your own original creation, except for material that You identify in Your Submission by its
source, author and license. That third-party material is under a license that allows it to be used in nth under the
GNU AGPL v3.0, and You have followed that license, including its notice requirements.

(c) If You used AI tools to make it, You have named the tools in Your Submission, reviewed the output, and are allowed
by the tools' terms to contribute it, and You are not aware that the output copies third-party code or other material
beyond short, functional fragments.

(d) It contains no secret, credential or token, no personal data of anyone else, and no confidential information of
anyone (including Your employer and Your own unreleased products) that You are not free to publish.

(e) It contains nothing designed to harm users, their computers or their data, or to collect data without telling them.

(f) You are of legal age to make this agreement where You live, or a parent or legal guardian has signed it with You.

You will tell oak993 promptly if You learn that any of these promises is not, or is no longer, true.

**8. No other warranty.** Except for the promises in section 7, You give Your Contributions "as is", without warranties
or conditions of any kind, and You do not have to support them.

**9. Confidential material and work for oak993**

(a) oak993 may share with You material that is not public, for example unreleased designs, plans, notes, test results,
findings or account details. You will keep it confidential, use it only to work on nth, and not publish or share it
without oak993's written permission, until oak993 makes it public. This does not cover material that is or becomes
public without Your fault, or that You already had.

(b) When oak993 gives You non-public nth source code only so that You can make changes to it for oak993 (the second
paragraph of section 2 of the GNU AGPL v3.0), You will do that work exclusively on oak993's behalf and under oak993's
direction, and You will not make or keep copies of that non-public code outside that work.

(c) This section never limits what the GNU AGPL v3.0 lets You do with a copy of nth that oak993 conveys to You for Your
own use, such as an installer and its Corresponding Source.

**10. Credit.** oak993 intends to credit You by the name or handle You choose, for example in the contributors list in
the README or the About screen, while nth contains a substantial part of Your Contributions, unless You ask not to be
named. The Git history also records Your Contributions. A missing or incomplete credit does not affect the licenses You
grant.

**11. Duration.** This agreement covers all Your Contributions, Submitted before or after You sign it. You may end it
for future Contributions by telling oak993 in writing; the licenses for Contributions Submitted before then continue.

**12. Transfer.** oak993 may transfer this agreement and the rights it grants to a successor that takes over nth, such
as a company that oak993 forms or controls, or a buyer of nth. The successor takes on oak993's duties under this
agreement. You may not transfer Your duties without oak993's written consent.

**13. Law and courts.** This agreement is governed by the law of the country where oak993 has its habitual residence
(or, if oak993 is then a company, its registered office) on the date You sign, without regard to its rules on conflict
of laws. The courts of that country have jurisdiction, except where mandatory law gives You the right to sue or be sued
in another court.

**14. Other terms**

(a) This is the entire agreement between You and oak993 about Your Contributions. For Your Contributions it replaces
the default license for contributions in GitHub's Terms of Service (section D.6).

(b) If a part of this agreement is held invalid or unenforceable, it applies to the fullest extent allowed and the rest
stays in force.

(c) It can be changed only in writing signed by You and oak993, including electronically. oak993 may publish a new
version for future contributors; a new version applies to You only if You sign it.

(d) You may sign electronically, as section 1 of `CONTRIBUTING.md` describes. An electronic signature has the same
effect as a handwritten one. oak993 keeps a private record of each signature (legal name, GitHub account, email
address, date and version).

## 5. Signature block

I have read the nth Individual Contributor License Agreement version 1.0 above and I agree to it.

```
Legal name:
Name or handle to be credited (or "do not credit"):
GitHub account:
Email address used for commits:
Country of residence:
Employer, client or school that may own my work (or "none"):
Date:
Signature:

Parent or legal guardian, if I am under the age of majority where I live:
Name:                                  Signature:

For oak993:
Signature:                             Date:
```
