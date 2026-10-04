#:project ../../src/Marrow.Core/Marrow.Core.csproj
#:property PublishAot=false
// Developer tool of the skills catalogue builder (unit SK-2, DL-2 of porting/first-release/skills-ONDEMAND-PLAN.md section 7). Run by hand, never by a test.
// It is the part of the pin run that must use Marrow.Core itself, so the builder and the app can never disagree about the schema, the gate or the report:
//
//   dotnet run tools/skills-catalogue/CatalogueTool.cs -- finalize <raw.json> <out.json> [--strict]
//       Reads a catalogue the builder wrote (strictly, with SK-1's loader), sets every pin state from the values, runs SK-1's gate, writes the canonical catalogue.json to
//       <out.json> whether or not the gate is satisfied (so it can be read), prints the gate's violations and the items that still lack values, and exits 1 when
//       there is a violation (with --strict also when any item is unpinned).
//   dotnet run tools/skills-catalogue/CatalogueTool.cs -- report <previous.json> <next.json> <out.md> [--max-files N]
//       Writes the change report for the owner (Core's CatalogueChangeReport) and prints its summary and the items that need the owner's attention.
//   dotnet run tools/skills-catalogue/CatalogueTool.cs -- gate <catalogue.json> [--strict]
//       Runs the gate on a catalogue file and prints what it finds.
//
// Nothing here reads the network or a data folder; it reads and writes only the files named on the command line.
using System.Text;
using Marrow.Core.Port.Skills.Catalogue;

var utf8 = new UTF8Encoding(false);

SkillsCatalogue Load(string path)
{
    try
    {
        return SkillsCatalogueLoader.Parse(File.ReadAllText(path, utf8));
    }
    catch (CatalogueFormatException error)
    {
        Console.Error.WriteLine($"{path}: {error.Message}");
        Environment.Exit(2);
        throw;
    }
}

int PrintGate(SkillsCatalogue catalogue, bool strict)
{
    var report = CatalogueGate.Check(catalogue, new CatalogueGateOptions { RequireAllPinned = strict });
    foreach (var violation in report.Violations.Where(v => v.Rule != CatalogueRules.Unpinned))
        Console.WriteLine($"VIOLATION [{violation.Rule}] {violation.ItemId ?? "-"}: {violation.Message}");
    foreach (var unpinned in report.Unpinned)
        Console.WriteLine($"UNPINNED {unpinned.ItemId}: lacks {string.Join(", ", unpinned.Missing)}");
    Console.WriteLine($"{report.Violations.Count(v => v.Rule != CatalogueRules.Unpinned)} violation(s); {report.Unpinned.Count} of {catalogue.Items.Count} item(s) still lack values.");
    return report.Violations.Count > 0 ? 1 : 0;
}

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: CatalogueTool.cs finalize <raw.json> <out.json> [--strict] | report <previous.json> <next.json> <out.md> [--max-files N] | gate <catalogue.json> [--strict]");
    return 2;
}

var strict = args.Contains("--strict");
switch (args[0])
{
    case "finalize" when args.Length >= 3:
    {
        var catalogue = SkillsCatalogueWriter.WithDerivedPinStates(Load(args[1]));
        File.WriteAllText(args[2], SkillsCatalogueWriter.Serialize(catalogue), utf8);
        Console.WriteLine($"wrote {args[2]}");
        return PrintGate(catalogue, strict);
    }
    case "report" when args.Length >= 4:
    {
        var previous = Load(args[1]);
        var next = Load(args[2]);
        var maxFiles = Array.IndexOf(args, "--max-files") is var at and >= 0 && at + 1 < args.Length ? int.Parse(args[at + 1], System.Globalization.CultureInfo.InvariantCulture) : (int?)null;
        var report = CatalogueChangeReport.Compare(previous, next);
        File.WriteAllText(args[3], report.ToMarkdown(new ChangeReportRenderOptions { MaxFilesPerItem = maxFiles }), utf8);
        Console.WriteLine($"wrote {args[3]}: {report.Added.Count} added, {report.Removed.Count} removed, {report.Items.Count - report.Added.Count - report.Removed.Count} changed, {report.Unchanged.Count} unchanged.");
        foreach (var item in report.Attention) Console.WriteLine($"ATTENTION {item.ItemId}: {item.Flag}: {item.Reason}");
        return 0;
    }
    case "gate" when args.Length >= 2:
        return PrintGate(Load(args[1]), strict);
    default:
        Console.Error.WriteLine("unknown command or missing arguments: " + string.Join(' ', args));
        return 2;
}
