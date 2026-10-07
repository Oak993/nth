import os,re,json,subprocess,tempfile,pathlib,sys
root=pathlib.Path(__file__).resolve().parents[4]
source_evidence=pathlib.Path(__file__).resolve().parent
evidence=pathlib.Path(os.environ.get('GAP2_RESULTS_DIR', tempfile.mkdtemp(prefix='gap2-results-')))
evidence.mkdir(parents=True, exist_ok=True)
env=os.environ.copy() # Put the selected .NET 10 SDK on PATH before running this script.
for key in ('http_proxy','https_proxy','no_proxy'):
    if key in env and key.upper() in env and env[key]==env[key.upper()]: env.pop(key)
project=sys.argv[1]
files=[root/x['path'] for x in json.loads((source_evidence/'trait-additions.json').read_text())]
files += list((root/'tests').glob('**/Gap*AcceptanceTests.cs'))+list((root/'tests').glob('**/GapComposerPreviewRenderTests.cs'))
classes=set()
for p in files:
    if '/Marrow.'+project+'.Tests/' not in str(p): continue
    s=p.read_text(); ns=re.search(r'namespace\s+([\w.]+)',s).group(1)
    cls=re.search(r'(?:partial\s+)?class\s+(\w+)',s).group(1)
    classes.add(ns+'.'+cls+'.')
classes.add('Marrow.'+project+'.Tests.TraceabilityTests.')
filter='('+'|'.join('FullyQualifiedName~'+c for c in sorted(classes))+')&FullyQualifiedName!~Native'
# Traceability classes can have a Port namespace: use their actual declarations.
for p in (root/'tests'/('Marrow.'+project+'.Tests')).rglob('TraceabilityTests.cs'):
    ns=re.search(r'namespace\s+([\w.]+)',p.read_text()).group(1)
    filter=filter.replace('Marrow.'+project+'.Tests.TraceabilityTests.',ns+'.TraceabilityTests.')
if len(sys.argv)>2: filter='FullyQualifiedName~'+sys.argv[2]+'&FullyQualifiedName!~Native'
env['MARROW_PORT_DATA_DIR']=tempfile.mkdtemp(prefix='gap2-'+project.lower()+'-')
name=project.lower() if len(sys.argv)<3 else sys.argv[2].split('.')[-1]
args=['dotnet','test','tests/Marrow.'+project+'.Tests/Marrow.'+project+'.Tests.csproj','--no-build','--no-restore','-p:EnableWindowsTargeting=true','--filter',filter,'--logger','trx;LogFileName='+name+'.trx','--results-directory',str(evidence)]
(evidence/(name+'-command.json')).write_text(json.dumps(args,indent=2)+'\n')
with open(evidence/(name+'.log'),'w') as out: result=subprocess.run(args,cwd=root,env=env,stdout=out,stderr=subprocess.STDOUT)
(evidence/(name+'.exit')).write_text(str(result.returncode)+'\n')
print(name,result.returncode)
