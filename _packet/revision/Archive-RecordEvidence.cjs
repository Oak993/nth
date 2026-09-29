// Preserve generated logs/TRX byte-for-byte, including their native CRLF and BOM, inside ZIPs.
// Generated evidence is not rewritten to make a source whitespace check pass.
const fs=require('node:fs');
const path=require('node:path');
const cp=require('node:child_process');
const crypto=require('node:crypto');
const root='E:\\Droppy\\wt\\oc\\chatgpt-1';
if(process.cwd().toLowerCase()!==root.toLowerCase())throw Error('Wrong worktree');
const record=process.argv[2];
if(!/^(PR|UI)-[A-Z]+-\d{3}$/.test(record||''))throw Error('A record is required');
const proof=JSON.parse(fs.readFileSync('_packet/revision/records/'+record+'.json','utf8'));
const folders=[...new Set(proof.mutations.map(m=>path.dirname(m.evidence)).concat(path.dirname(proof.ordinary_record_gate.evidence)))];
const hash=bytes=>crypto.createHash('sha256').update(bytes).digest('hex').toUpperCase();
const quote=s=>"'"+s.replace(/'/g,"''")+"'";
function inventory(folder,relative='') {
  return fs.readdirSync(path.join(folder,relative),{withFileTypes:true}).sort((a,b)=>a.name.localeCompare(b.name)).flatMap(entry=>{
    if(entry.isSymbolicLink())throw Error('Evidence must contain no symlink');
    const name=path.join(relative,entry.name);
    if(entry.isDirectory())return inventory(folder,name);
    const bytes=fs.readFileSync(path.join(folder,name));
    return [{path:name.replace(/\\/g,'/'),bytes:bytes.length,sha256:hash(bytes)}];
  });
}
fs.mkdirSync('_packet/revision/evidence-archives',{recursive:true});
const archives=[];
for(const relative of folders) {
  const folder=path.resolve(root,relative);
  if(!folder.startsWith(path.join(root,'_packet','revision','evidence')+path.sep))throw Error('Unexpected source');
  const archiveRelative='_packet/revision/evidence-archives/'+path.basename(folder)+'.zip';
  const archive=path.join(root,archiveRelative);
  const manifestRelative=archiveRelative+'.json';
  const files=inventory(folder);
  if(!fs.existsSync(archive)) {
    const command='Add-Type -AssemblyName System.IO.Compression.FileSystem; [IO.Compression.ZipFile]::CreateFromDirectory('+quote(folder)+','+quote(archive)+',[IO.Compression.CompressionLevel]::Optimal,$true)';
    cp.execFileSync('powershell.exe',['-NoProfile','-NonInteractive','-Command',command],{cwd:root,windowsHide:true,stdio:'pipe'});
    fs.writeFileSync(manifestRelative,JSON.stringify({original_directory:relative,archive:archiveRelative,archive_sha256:hash(fs.readFileSync(archive)),files},null,2)+'\n');
  }
  const manifest=JSON.parse(fs.readFileSync(manifestRelative,'utf8'));
  if(manifest.archive_sha256!==hash(fs.readFileSync(archive))||JSON.stringify(manifest.files)!==JSON.stringify(files))throw Error('Evidence changed after archiving');
  archives.push({original_directory:relative,archive:archiveRelative,archive_sha256:manifest.archive_sha256,manifest:manifestRelative,file_count:files.length});
}
fs.writeFileSync('_packet/revision/records/'+record+'.archives.json',JSON.stringify({record,archives},null,2)+'\n');
console.log('Archived '+archives.length+' evidence directories without changing their bytes.');
