using Marrow.Core.Support;

namespace Marrow.Services.Tests.Port.Skills.Providers;

// PORT-ONLY: SK-11e. Fixture transcripts synthesized from each provider's documented format (never recorded from a live CLI).
// One JSON value per line under Fixtures/skill-taps; the finder walks up from the test output folder and fails closed.
internal static class SkillTapFixtures
{
    public static List<JSONValue> Lines(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var file = Path.Combine(dir.FullName, "Fixtures", "skill-taps", name);
            if (File.Exists(file)) return File.ReadAllLines(file).Where(l => l.Length != 0).Select(l => JSONValue.Parse(l) ?? throw new InvalidDataException(name)).ToList();
            var source = Path.Combine(dir.FullName, "tests", "Marrow.Services.Tests", "Fixtures", "skill-taps", name);
            if (File.Exists(source)) return File.ReadAllLines(source).Where(l => l.Length != 0).Select(l => JSONValue.Parse(l) ?? throw new InvalidDataException(name)).ToList();
        }
        throw new FileNotFoundException("Fixture transcript not found: " + name);
    }

    public static string TempFolder(string label)
    {
        var parent = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR") ?? throw new InvalidOperationException("MARROW_PORT_DATA_DIR is required (R-TEST-9).");
        var dir = Path.Combine(parent, "sk11e " + label + " " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
