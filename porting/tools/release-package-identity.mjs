// Package inspection is a genuine Windows receipt, bound/hash-checked by release-evidence.mjs.
// This additionally rejects a consistently declared but wrong release identity.
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { validateArtifact } from './evidence-binding.mjs';
export function canonicalPackageIdentity(root) {
 const source=readFileSync(resolve(root,'src/Marrow.App/AssemblyInfo.cs'),'utf8');
 const version=source.match(/AssemblyMetadata\("CFBundleShortVersionString",\s*"([^"]+)"\)/)?.[1];
 const build=source.match(/AssemblyMetadata\("CFBundleVersion",\s*"([^"]+)"\)/)?.[1];
 const runtime=JSON.parse(readFileSync(resolve(root,'scripts/release-toolchain.json'),'utf8')).dotnetRuntime;
 const copyright=source.match(/AssemblyCopyright\("([^"]+)"\)/)?.[1];
 return {version,build,runtime,copyright};
}
export function validateInspectedBinaries(packageInput,inspection,identity,root,buildArtifacts) {
 const requireIt=(ok,message)=>{if(!ok)throw Error(message);};
 requireIt(typeof identity.copyright==='string'&&identity.copyright.length>0,'canonical copyright missing');
 requireIt(inspection.pe_metadata?.app?.legal_copyright===identity.copyright,'application legal copyright differs from unchanged source');
 for(const name of ['app','setup']) {
  const metadata=inspection.pe_metadata?.[name];requireIt(metadata?.architecture==='x64',`${name} architecture must be x64`);
  for(const kind of ['exe','assembly']) {
   const artifact=metadata[`${kind}_artifact`];requireIt(artifact?.sha256===metadata[`${kind}_sha256`],`${name} inspected ${kind} hash mismatch`);
   validateArtifact(root,artifact);
   requireIt(buildArtifacts.some(a=>a.path===artifact.path&&a.sha256===artifact.sha256),`${name} inspected ${kind} lacks exact build artifact binding`);
  }
  const bytes=readFileSync(validateArtifact(root,metadata.exe_artifact));
  requireIt(bytes.length>=64&&bytes.toString('ascii',0,2)==='MZ',`${name} is not a PE EXE`);
  const offset=bytes.readUInt32LE(0x3c);requireIt(offset+6<bytes.length&&bytes.toString('ascii',offset,offset+4)==='PE\0\0'&&bytes.readUInt16LE(offset+4)===0x8664,`${name} EXE is not x64 PE`);
 }
 requireIt(inspection.pe_metadata.setup.exe_sha256===packageInput.artifact.sha256&&inspection.pe_metadata.setup.exe_artifact.path===packageInput.artifact.path,'inspected setup is not the exact release package');
}
export function validatePackageIdentity(packageInput,inspection,identity) {
 const requireIt=(ok,message)=>{if(!ok)throw Error(message);};
 requireIt(/^\d+\.\d+\.\d+$/.test(identity.version||'')&&/^\d+$/.test(identity.build||'')&&/^\d+\.\d+\.\d+$/.test(identity.runtime||''),'missing canonical release identity');
 requireIt(packageInput?.version===identity.version&&packageInput?.runtime_version===identity.runtime,'package version/runtime differs from canonical release inputs');
 for(const [name,title] of [['app','Marrow'],['setup','Marrow Setup']]) {
  const expected={assembly_version:`${identity.version}.0`,file_version:`${identity.version}.${identity.build}`,product_version:`${identity.version}+build.${identity.build}`,product:'Marrow',company:'Marrow Code',title};
  for(const [key,value] of Object.entries(expected))requireIt(inspection.pe_metadata?.[name]?.[key]===value,`${name} PE ${key} differs from canonical release identity`);
 }
 return 'Both PE identities match canonical version/build/runtime and Marrow / Marrow Code identity';
}
