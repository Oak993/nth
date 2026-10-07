import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtempSync,writeFileSync,rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {createHash} from 'node:crypto';
import {validateInspectedBinaries} from '../release-package-identity.mjs';
test('PE inspections bind both binary hashes, real x64 headers, build receipt and canonical copyright',()=>{
 const root=mkdtempSync(join(tmpdir(),'marrow-pe-proof-'));try{
  const pe=Buffer.alloc(256);pe.write('MZ');pe.writeUInt32LE(128,0x3c);pe.write('PE\0\0',128);pe.writeUInt16LE(0x8664,132);
  const artifact=path=>{writeFileSync(join(root,path),pe);return {path,sha256:createHash('sha256').update(pe).digest('hex')};};
  const app=artifact('app.exe'),dll=artifact('app.dll'),setup=artifact('setup.exe'),setupDll=artifact('setup.dll');
  const inspected=(exe,assembly)=>({architecture:'x64',exe_sha256:exe.sha256,assembly_sha256:assembly.sha256,exe_artifact:exe,assembly_artifact:assembly,legal_copyright:'canonical attribution'});
  const inspection={pe_metadata:{app:inspected(app,dll),setup:inspected(setup,setupDll)}},identity={copyright:'canonical attribution'},pack={artifact:setup},build=[app,dll,setup,setupDll];
  assert.doesNotThrow(()=>validateInspectedBinaries(pack,inspection,identity,root,build));
  for(const mutate of [i=>i.pe_metadata.app.legal_copyright='',i=>i.pe_metadata.setup.exe_sha256='0'.repeat(64),i=>i.pe_metadata.app.architecture='x86',i=>i.pe_metadata.setup.assembly_artifact.path='../escape']){const bad=structuredClone(inspection);mutate(bad);assert.throws(()=>validateInspectedBinaries(pack,bad,identity,root,build));}
  assert.throws(()=>validateInspectedBinaries(pack,inspection,identity,root,build.slice(1)));
  const x86=Buffer.from(pe);x86.writeUInt16LE(0x14c,132);writeFileSync(join(root,'app.exe'),x86);assert.throws(()=>validateInspectedBinaries(pack,inspection,identity,root,build));
 }finally{rmSync(root,{recursive:true,force:true});}
});
