"""Run the checked-in diagnostic in an external scratch project, without editing the port.
Usage: python3 run-probe.py <cpu|page> <repository-root> <before|after> <output.csv> [dotnet-executable]
For Linux page mode, build the documented real ICU bridge and set LD_LIBRARY_PATH first.
"""
import pathlib,subprocess,sys,tempfile,json,os
root=pathlib.Path(__file__).resolve().parent
mode,repo,stage,output=sys.argv[1:5]
assert mode in ('cpu','page') and stage in ('before','after')
repo=str(pathlib.Path(repo).resolve())
scratch=pathlib.Path(tempfile.mkdtemp(prefix='gap2-t24-probe-'))
(scratch/'Program.cs').write_bytes((root/(mode+'-probe.cs.txt')).read_bytes())
(scratch/'frame-probe.csproj').write_bytes((root/(mode+'-probe.csproj.txt')).read_bytes())
cmd=[sys.argv[5] if len(sys.argv)>5 else 'dotnet','run','--project',str(scratch/'frame-probe.csproj'),'-p:MarrowRoot='+repo,'-p:EnableWindowsTargeting=true','--',stage]
env=os.environ.copy();env['MARROW_PORT_DATA_DIR']=str(scratch/'profile')
p=subprocess.run(cmd,capture_output=True,text=True,env=env)
pathlib.Path(output+'.execution.log').write_text(p.stdout+p.stderr)
pathlib.Path(output+'.command.json').write_text(json.dumps(cmd,indent=2)+'\n')
if p.returncode: sys.exit(p.returncode)
lines=p.stdout.splitlines();index=lines.index('stage,path,transition,frame,elapsed_ms,opacity,step_fraction,step_percent')
pathlib.Path(output).write_text('\n'.join(lines[index:])+'\n')
print(output)
