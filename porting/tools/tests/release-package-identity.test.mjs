import test from 'node:test';
import assert from 'node:assert/strict';
import { validatePackageIdentity } from '../release-package-identity.mjs';
const identity={version:'1.7.0',build:'21',runtime:'10.0.12'};
const metadata=title=>({assembly_version:'1.7.0.0',file_version:'1.7.0.21',product_version:'1.7.0+build.21',product:'Marrow',company:'Marrow Code',title});
const packageInput={version:'1.7.0',runtime_version:'10.0.12'};
const inspection={pe_metadata:{app:metadata('Marrow'),setup:metadata('Marrow Setup')}};
test('release package identity follows canonical version, build, runtime and both PE identities',()=>{
 assert.doesNotThrow(()=>validatePackageIdentity(packageInput,inspection,identity));
 for(const mutate of [p=>p.version='1.0.0',p=>p.runtime_version='10.0.10']){const p=structuredClone(packageInput);mutate(p);assert.throws(()=>validatePackageIdentity(p,inspection,identity));}
 for(const mutate of [i=>delete i.pe_metadata.setup,i=>i.pe_metadata.app.file_version='1.0.0.0',i=>i.pe_metadata.app.company='Marrow.App',i=>i.pe_metadata.setup.product_version='1.0.0',i=>i.pe_metadata.app.assembly_version='1.7.0.1',i=>i.pe_metadata.setup.title='MarrowCode']){const i=structuredClone(inspection);mutate(i);assert.throws(()=>validatePackageIdentity(packageInput,i,identity));}
 assert.throws(()=>validatePackageIdentity(packageInput,inspection,{}));
});
