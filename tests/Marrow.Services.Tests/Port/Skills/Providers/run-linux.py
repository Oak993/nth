#!/usr/bin/env python3
"""Offline SK-11c verification using cached NuGet packages; no provider process is started.

The temporary Services project excludes Port/Voice, whose NAudio/Whisper packages
are absent from this worker's cache. This is a bounded Linux check, not a Windows
solution build. The repository's projects and all existing tests stay unchanged.
"""
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
from xml.sax.saxutils import escape

root = Path(__file__).resolve().parents[5]
cache = os.environ.get("NUGET_PACKAGES")
if not cache or not Path(cache).is_dir():
    raise SystemExit("NUGET_PACKAGES must name the existing local package cache; no network fallback.")
dotnet = shutil.which("dotnet")
if not dotnet:
    raise SystemExit("Put the installed .NET 10 SDK on PATH; this runner never downloads an SDK.")
work = Path(tempfile.mkdtemp(prefix="marrow-sk11c-", dir="/tmp"))
tests = work / "test"
tests.mkdir()
data = work / "data"
data.mkdir()
env = dict(os.environ, MARROW_PORT_DATA_DIR=str(data), SK11C_SOURCE_ROOT=str(root), EnableWindowsTargeting="true")
print(f"SK-11c verification artifacts: {work}", flush=True)


def item(tag, path, **attributes):
    attrs = {"Include": str(path), **attributes}
    return "<" + tag + " " + " ".join(f'{key}="{escape(value)}"' for key, value in attrs.items()) + "/>"


properties = """<TargetFramework>net10.0-windows</TargetFramework><EnableDefaultItems>false</EnableDefaultItems>
<ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><LangVersion>latest</LangVersion>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors><CheckForOverflowUnderflow>true</CheckForOverflowUnderflow><NoWarn>CS8524</NoWarn>"""
service_items = item("ProjectReference", root / "src/Marrow.Core/Marrow.Core.csproj")
service_items += item("Compile", root / "src/Marrow.Services/**/*.cs", Exclude=";".join(str(root / p) for p in [
    "src/Marrow.Services/obj/**/*.cs", "src/Marrow.Services/bin/**/*.cs", "src/Marrow.Services/Port/Voice/**/*.cs"]))
(work / "Services.csproj").write_text(f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>{properties}</PropertyGroup><ItemGroup>{service_items}</ItemGroup></Project>')
test_items = item("ProjectReference", work / "Services.csproj") + item("ProjectReference", root / "tests/Marrow.Testing/Marrow.Testing.csproj")
for package, version in [("Microsoft.NET.Test.Sdk", "17.14.1"), ("xunit", "2.9.3"), ("xunit.runner.visualstudio", "3.1.4")]:
    test_items += item("PackageReference", package, Version=version)
test_items += '<Using Include="Xunit"/>'
for path in [
    "Port/Skills/Providers/**/*.cs", "Port/Skills/Generations/**/*.cs", "Port/Skills/Store/Fixtures/**/*.cs",
    "TraceabilityTests.cs", "Providers/ACPSessionTests.cs", "Providers/CopilotSessionThirdTests.cs", "Providers/CopilotSessionFourthTests.cs",
    "Fixtures/ACPSessionFixtures.cs", "Fixtures/CopilotSessionHarness.cs", "Fixtures/CopilotStandIns.cs",
    "Fixtures/ProviderRegistryFixtures.cs", "Fixtures/MCPSessionOverridesFixtures.cs", "Fixtures/StorageScratchFolder.cs",
]:
    test_items += item("Compile", root / "tests/Marrow.Services.Tests" / path)
project = tests / "Tests.csproj"
project.write_text(f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>{properties}<AssemblyName>Marrow.Services.Tests</AssemblyName><IsTestProject>true</IsTestProject></PropertyGroup><ItemGroup>{test_items}</ItemGroup></Project>')


def run(label, arguments):
    log = work / (label + ".log")
    with log.open("w") as output:
        result = subprocess.run([dotnet, *arguments], cwd=root, env=env, stdout=output, stderr=subprocess.STDOUT)
    print(log, flush=True)
    print("\n".join(log.read_text().splitlines()[-10:]), flush=True)
    if result.returncode:
        raise SystemExit(result.returncode)


# The source is a filesystem cache, never an HTTP NuGet source.
run("restore", ["restore", str(project), "--source", cache])
run("sk11c", ["test", str(project), "--no-restore", "--filter", "Feature=SK-11c&FullyQualifiedName!~WindowsByteIdentityTests", "--logger", "trx;LogFileName=sk11c.trx", "--results-directory", str(work)])
checks = ["Feature=SK-11c", "Feature=SK-9", "Feature=SK-11d", "FullyQualifiedName~TraceabilityTests",
          "FullyQualifiedName~MakeToolCall_", "FullyQualifiedName~MakeToolUpdate_", "FullyQualifiedName~Cursor_argv",
          "FullyQualifiedName~Grok_argv", "FullyQualifiedName~Devin_argv", "FullyQualifiedName~OpenCode_Zen_and_Go_argv"]
# These existing test classes mix offline handlers, Windows ICU and Windows process stand-ins. Select their offline handlers explicitly.
checks.append("(FullyQualifiedName~CopilotSessionThirdTests&" + "&".join("FullyQualifiedName!~" + name for name in [
    "An_automatic_approval_is_", "An_Auto_tier_switch_is_", "The_session_runs_only_", "No_session_delete_is_",
    "Kinds_without_paths_are_described_", "The_bracket_holds_one_Character_", "Todos_parse_bullets_and_numbers_"]) + ")")
checks.append("(FullyQualifiedName~CopilotSessionFourthTests&" + "&".join("FullyQualifiedName!~" + name for name in [
    "A_model_row_detail_shows_", "The_usage_rows_keep_", "The_sign_in_text_is_", "The_approval_card_titles_fall_back_"]) + ")")
regression_filter = "(" + "|".join(checks) + ")&FullyQualifiedName!~WindowsGenerationTests&FullyQualifiedName!~WindowsByteIdentityTests"
run("regression", ["test", str(project), "--no-build", "--no-restore", "--filter", regression_filter, "--logger", "trx;LogFileName=regression.trx", "--results-directory", str(work)])
core = root / "tests/Marrow.Core.Tests/Marrow.Core.Tests.csproj"
run("core-restore", ["restore", str(core), "--source", cache])
run("core", ["test", str(core), "--no-restore", "--filter", "FullyQualifiedName~Traceability|Feature=SK-10|Feature=SK-1|Feature=SK-7", "--logger", "trx;LogFileName=core.trx", "--results-directory", str(work)])
