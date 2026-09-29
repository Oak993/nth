const fs=require('node:fs'),path=require('node:path');
const {fixtureFailure}=require('./MutationFailureClassifier.cjs');
if(process.cwd().toLowerCase()!=='e:\\droppy\\wt\\oc\\chatgpt-1')throw Error('Wrong worktree');
const base='_packet/revision/evidence',invalid=[];
for(const dir of fs.readdirSync(base)) {
  const file=path.join(base,dir,'results.json');
  if(!fs.existsSync(file))continue;
  for(const row of JSON.parse(fs.readFileSync(file,'utf8'))) {
    if(row.status!=='KILLED_TWICE_ALONE_AND_ORDINARY_GREEN')continue;
    for(const run of row.runs.filter(r=>/-red-[12]$/.test(r.name))) {
      const reason=fixtureFailure(run.tests);
      if(reason)invalid.push({directory:dir,id:row.id,records:row.records,run:run.name,reason});
    }
  }
}
fs.writeFileSync('_packet/revision/retrospective-fixture-exclusions.json',JSON.stringify({reviewed_at:new Date().toISOString(),invalid},null,2)+'\n');
console.log(JSON.stringify(invalid,null,2));
