using System.Runtime.CompilerServices;
using Marrow.Testing;

namespace Marrow.Setup.Tests;

// R-TEST-1 to R-TEST-3 (IMPLEMENTING.md 5): the meta-tests every Marrow test project carries.
public sealed class TraceabilityTests
{
    [Fact]
    [Trait("Rule", "R-TEST-1")]
    [Trait("Decision", "ED-081")]
    public void Every_test_names_a_requirement_a_rule_or_a_decision_and_every_id_is_well_formed() =>
        Traceability.AssertEveryTestNamesWhatItProves(typeof(TraceabilityTests).Assembly);

    [Fact]
    [Trait("Rule", "R-TEST-2")]
    public void Test_classes_and_Req_tests_have_the_shape_req_verify_can_read() =>
        Traceability.AssertShapesReqVerifyCanRead(typeof(TraceabilityTests).Assembly);

    [Fact]
    [Trait("Rule", "R-TEST-3")]
    public void Test_source_has_one_class_per_file_single_line_attributes_and_no_stray_type_keywords() =>
        Traceability.AssertSourceLayout(typeof(TraceabilityTests).Assembly, ProjectDirectory());

    private static string ProjectDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
