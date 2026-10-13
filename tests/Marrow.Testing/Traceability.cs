using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace Marrow.Testing;

/// <summary>
/// The meta-test checks every Marrow test project runs (ED-081). porting/tools/req_verify.mjs derives Gate D status
/// from the SOURCE text of the tests (the TRX carries no traits), with a line-by-line parser; these checks keep
/// test source in the shape it reads and keep every test naming what it proves. Each test project has one
/// TraceabilityTests class that calls these. Source: draft C (proto-C TraceabilityTests) plus the DisplayName and
/// stray class/record checks found by reading req_verify.mjs for this guide.
/// </summary>
public static partial class Traceability
{
    [GeneratedRegex(@"\A(PR|UI)-[A-Z]+-[0-9]{3}\z")] private static partial Regex RequirementId();

    [GeneratedRegex(@"\AR-[A-Z]+-[0-9]+\z")] private static partial Regex RuleId();

    // Unnumbered first-release decisions use their append-only heading (ED-360) until the lead numbers them. A web
    // surfaces unit names its own (ED-NEW-web-w12-1: the lane, the unit, the number; lead 2026-10-11).
    [GeneratedRegex(@"\A(?:ED-(?:[0-9]{3}|NEW-web-review-[0-9]+|NEW-[a-z]+(?:-[a-z]+[0-9]+[a-z]?)?-[0-9]+)|OD-[0-9]{4})\z")] private static partial Regex DecisionId();

    [GeneratedRegex(@"\AO-[0-9]{2}\z")] private static partial Regex OracleId();

    // First-release feature units (OD-0103): a Feature trait names the build unit ("VT-1") and a Spec trait the specification's test id
    // ("V-T10"). They locate a test; they never replace the Req, Rule or Decision trait R-TEST-1 asks for.
    [GeneratedRegex(@"\A[A-Z]{1,4}-[0-9]+[a-z]?\z")] private static partial Regex FeatureId();

    // Locating Spec traits also name plan acceptance lists (DL-16) and numbered engineering contracts (ED-326).
    [GeneratedRegex(@"\A(?:[A-Z]{1,4}-T[0-9]+|DL-[0-9]+|ED-[0-9]{3})\z")] private static partial Regex SpecTestId();

    // Two locating traits the skills units use: Platform marks a test that only runs on Windows, and Review marks a test
    // an independent review wrote (its value names the unit reviewed). Like Feature and Spec, neither replaces R-TEST-1's trait.
    [GeneratedRegex(@"\AWINDOWS-ONLY\z")] private static partial Regex PlatformId();

    [GeneratedRegex(@"\b(?:class|record)\s+(\w+)")] private static partial Regex TypeKeyword();

    [GeneratedRegex(@"\A\[(?:assembly:\s*)?[A-Z][A-Za-z0-9_.]*\s*(?:\(|\]|\z)")] private static partial Regex AttributeStart();

    public static IEnumerable<MethodInfo> TestMethods(Assembly tests) =>
        tests.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttributes<FactAttribute>().Any());

    /// <summary>xunit 2 keeps a trait only in its constructor arguments.</summary>
    // xUnit applies a class's [Trait]s to each of its tests, so a trait on the class counts for every method in it.
    public static List<(string Name, string Value)> Traits(MethodInfo method) =>
        method.GetCustomAttributesData().Concat(method.DeclaringType!.GetCustomAttributesData())
            .Where(a => a.AttributeType == typeof(TraitAttribute))
            .Select(a => ((string)a.ConstructorArguments[0].Value!, (string)a.ConstructorArguments[1].Value!)).ToList();

    /// <summary>R-TEST-1: every test names a requirement, a rule or a decision, and every id is well formed.</summary>
    public static void AssertEveryTestNamesWhatItProves(Assembly tests)
    {
        var problems = new List<string>();
        foreach (var method in TestMethods(tests))
        {
            var traits = Traits(method);
            var name = $"{method.DeclaringType!.Name}.{method.Name}";
            if (!traits.Any(t => t.Name is "Req" or "Rule" or "Decision")) problems.Add($"{name}: no Req, Rule or Decision trait");
            // An expectation resting on an open oracle item cannot verify a requirement (R-TEST-6): Gate D stays honest.
            if (traits.Any(t => t.Name == "Oracle") && traits.Any(t => t.Name == "Req"))
                problems.Add($"{name}: an Oracle test cannot carry a Req trait until the oracle item is settled");
            foreach (var (trait, value) in traits)
            {
                var ok = trait switch
                {
                    "Req" => RequirementId().IsMatch(value),
                    "Rule" => RuleId().IsMatch(value),
                    "Decision" => DecisionId().IsMatch(value),
                    "Oracle" => OracleId().IsMatch(value),
                    "Feature" => FeatureId().IsMatch(value),
                    "Spec" => SpecTestId().IsMatch(value),
                    "Platform" => PlatformId().IsMatch(value),
                    "Review" => FeatureId().IsMatch(value),
                    _ => false,
                };
                if (!ok) problems.Add($"{name}: bad trait {trait}='{value}'");
            }
        }
        Assert.Empty(problems);
    }

    /// <summary>R-TEST-2: test classes and Req tests have the shape req_verify.mjs can read.</summary>
    public static void AssertShapesReqVerifyCanRead(Assembly tests)
    {
        var problems = new List<string>();
        foreach (var type in TestMethods(tests).Select(m => m.DeclaringType!).Distinct())
        {
            // A declared nested type renames every later test in req_verify's eyes (it keys on the last
            // class/record keyword). Compiler-generated closures and state machines are fine.
            foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                if (!nested.IsDefined(typeof(CompilerGeneratedAttribute)))
                    problems.Add($"{type.Name}: nested type {nested.Name}");
            if (type.IsNested) problems.Add($"{type.Name}: a test class must be top level");
        }
        foreach (var method in TestMethods(tests))
        {
            var name = $"{method.DeclaringType!.Name}.{method.Name}";
            // The TRX names a test by its display name; req_verify looks for Namespace.Class.Method.
            if (method.GetCustomAttributes<FactAttribute>().Any(f => f.DisplayName is not null))
                problems.Add($"{name}: DisplayName changes the TRX name req_verify matches");
            if (!Traits(method).Any(t => t.Name == "Req")) continue;
            // req_verify matches `public [async] void|Task|Task<T> Name(`; nothing else is seen.
            if (!method.IsPublic || method.IsStatic) problems.Add($"{name}: a Req test must be public and instance");
            if (method.ReturnType != typeof(void) && method.ReturnType != typeof(Task) &&
                !(method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>)))
                problems.Add($"{name}: a Req test returns {method.ReturnType.Name}");
        }
        Assert.Empty(problems);
    }

    /// <summary>
    /// R-TEST-2 and R-TEST-3 on the source text: one test class per file, named after the file; every attribute on
    /// a single line; a Req trait starts its own attribute line; and no other `class X` or `record X` text (a
    /// helper type, or even a string literal) in a file that holds a test class.
    /// </summary>
    public static void AssertSourceLayout(Assembly tests, string projectDirectory)
    {
        var problems = new List<string>();
        var testClasses = TestMethods(tests).Select(m => m.DeclaringType!.Name).ToHashSet(StringComparer.Ordinal);
        var separator = Path.DirectorySeparatorChar;
        foreach (var file in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
                              && !f.Contains($"{separator}bin{separator}", StringComparison.Ordinal)))
        {
            var lines = File.ReadAllLines(file);
            var shortName = Path.GetFileName(file);
            var declared = new List<(string Name, int Line)>();
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimStart();
                if (line.StartsWith("//", StringComparison.Ordinal)) continue;
                // Only a line that opens an attribute counts (a capitalised name right after the bracket): a C# collection
                // expression that starts a line, such as `[`, `["a",` or `[new(...)`, is not an attribute.
                if (AttributeStart().IsMatch(line) && !line.Contains(']', StringComparison.Ordinal))
                    problems.Add($"{shortName}:{i + 1}: an attribute must fit on one line");
                if (line.Contains("Trait(\"Req\"", StringComparison.Ordinal) && !line.StartsWith('['))
                    problems.Add($"{shortName}:{i + 1}: a Req trait must start its own attribute line");
                foreach (Match match in TypeKeyword().Matches(line)) declared.Add((match.Groups[1].Value, i + 1));
            }
            var tested = declared.Where(d => testClasses.Contains(d.Name)).ToList();
            if (tested.Count == 0) continue;
            if (tested.Count > 1) problems.Add($"{shortName}: {tested.Count} test classes");
            if (tested[0].Name + ".cs" != shortName) problems.Add($"{shortName}: holds {tested[0].Name}");
            foreach (var other in declared.Where(d => d.Line != tested[0].Line))
                problems.Add($"{shortName}:{other.Line}: `class/record {other.Name}` text in a test file renames later tests for req_verify");
        }
        Assert.Empty(problems);
    }
}
