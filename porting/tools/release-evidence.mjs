// Mechanical evidence validation for semantic C..F. Human/source review remains mandatory.
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { captureBinding, json, sha256, validateArtifact, validateBinding } from './evidence-binding.mjs';
import { ledgerRecords, exempt, validateRequirementReport } from './requirement-evidence.mjs';
export { validateRequirementReport };
import { requiredReleaseScope } from './release-scope.mjs';
import { isDeepStrictEqual } from 'node:util';
export const LIFECYCLE = ['clean-install','launch','native-load','close-child-cleanup','second-launch','upgrade','same-version','downgrade-refusal','uninstall-data-retention'];
export const SCENARIO_CHECKS = {
 'clean-install': {installed:true,prerequisite_satisfied:true},launch:{launched:true,desktop_window:true},'native-load':{cpu_loaded:true,vulkan_loaded:true},'close-child-cleanup':{app_closed:true,children_remaining:0},'second-launch':{launched:true,existing_data_loaded:true},upgrade:{upgraded:true,data_retained:true},'same-version':{unchanged:true,installer_payload_started:false},'downgrade-refusal':{refused:true,unchanged:true},'uninstall-data-retention':{app_removed:true,data_retained:true,children_remaining:0}
};
export const NEGATIVES = ['missing-evidence','unverified-requirement','controlled-regression','stale-revision','tampered-artifact'];
export function dimensionsFor(r) {
 const method=String(r.verification_method||'').toLowerCase(), out=[];
 if(/unit/.test(method))out.push('unit');
 if(/contract/.test(method))out.push('contract');
 if(/interact|input|click|hover|focus|keyboard|drag|end.to.end|workflow/.test(method))out.push('interaction');
 if(r.id.startsWith('UI-')||/visual|pixel|render|screenshot|frame|geometry|animat|font|layout/.test(method))out.push('visual');
 if(/persist|restart|relaunch|round.trip|storage/.test(method)|| (r.persistence && !/^(none[.]?|n\/a|not applicable)$/i.test(r.persistence.trim())))out.push('restart');
 if(/fail|cancel|error|timeout|reject|refus/.test(method)||(r.failure_cases||[]).length||!['not cancellable','None.',null,undefined].includes(r.cancellation))out.push('failure');
 if(/integrat|end.to.end|live|provider.*(?:run|real)|real.*(?:provider|executable)/.test(method))out.push('end-to-end');
 return out.length?out:['contract'];
}
function nonempty(s) { return typeof s==='string'&&s.trim().length>0; }
function requireIt(value,why) {if(!value)throw Error(why);}
export function validateExecution(e,ctx) {
 requireIt(e?.status==='PASS',`evidence ${e?.id} is not PASS`);validateBinding(e.binding,ctx.binding);
 requireIt(Array.isArray(e.command)&&e.command.length&&e.command.every(nonempty),`evidence ${e.id} has no exact command`);
 requireIt(e.exit_code===0,`evidence ${e.id} command did not exit 0`);
 const start=Date.parse(e.started_at),end=Date.parse(e.finished_at);
 requireIt(Number.isFinite(start)&&Number.isFinite(end)&&end>=start&&end<=Date.now()+60000,`evidence ${e.id} has invalid run times`);
 requireIt(nonempty(e.producer)&&nonempty(e.environment?.os)&&nonempty(e.environment?.architecture)&&Object.keys(e.environment?.tools||{}).length,`evidence ${e.id} lacks producer/environment/toolchain`);
 requireIt(Array.isArray(e.artifacts)&&e.artifacts.length,`evidence ${e.id} has no artifacts`);
 for(const a of e.artifacts)validateArtifact(ctx.root,a);
 if(['interaction','visual','restart','failure','end-to-end','package-lifecycle','package-build'].includes(e.kind))requireIt(e.environment.platform==='win32'&&/Windows 11/.test(e.environment.os)&&e.environment.architecture==='x64',`evidence ${e.id} needs actual Windows 11 x64`);
}
function evidenceMap(doc,ctx) {
 const map=new Map();
 requireIt(Array.isArray(doc.evidence),'missing evidence list');
 for(const e of doc.evidence){requireIt(nonempty(e.id)&&!map.has(e.id),`duplicate/invalid evidence ID ${e.id}`);validateExecution(e,ctx);map.set(e.id,e);}
 return map;
}
function refs(ids,map,label) {
 requireIt(Array.isArray(ids)&&ids.length,`${label}: no evidence references`);
 return ids.map(id=>{requireIt(map.has(id),`${label}: missing evidence ${id}`);return map.get(id);});
}
function scopedAssertions(e,r,ctx) {
 const assertion=(e.assertions||[]).filter(a=>a.id===r.id);
 requireIt(assertion.length===1,`${e.id}: needs exactly one assertion for ${r.id}`);
 const a=assertion[0];
 requireIt(a.verification_method===r.verification_method&&JSON.stringify(a.source_evidence)===JSON.stringify(r.source_evidence||[]),`${e.id}: changed method/source clauses for ${r.id}`);
 requireIt(Array.isArray(a.checks)&&a.checks.length&&a.checks.every(c=>nonempty(c.name)&&c.status==='PASS'&&c.expected!==undefined&&JSON.stringify(c.expected)===JSON.stringify(c.actual)),`${e.id}: missing/failed expected-vs-observed checks for ${r.id}`);
 if(e.kind==='production-review') {
  for(const key of ['entrypoint','handler','state_transition','side_effects','persistence','failure_cancellation','authority'])requireIt(nonempty(a.trace?.[key]),`${r.id}: production trace lacks ${key}`);
  requireIt(Array.isArray(a.implementation_paths)&&a.implementation_paths.length,`${r.id}: no assertion production paths`);
 }
 if(e.kind==='visual') {
  const pair=a.visual;
  requireIt(pair&&['source-defined','owner-approved-deviation'].includes(pair.scope)&&nonempty(pair.method),`${r.id}: missing visual comparison scope/method`);
  for(const key of ['reference','windows','policy','measurements'])validateArtifact(ctx.root,pair[key]);
  requireIt(pair.reference_provenance?.baseline===ctx.binding.baseline&&nonempty(pair.reference_provenance?.source)&&pair.logical_width>0&&pair.logical_height>0&&[100,125,150,200].includes(pair.scale_percent)&&nonempty(pair.theme)&&nonempty(pair.state),`${r.id}: visual reference/condition provenance missing`);
  requireIt(e.artifacts.some(x=>x.path===pair.windows.path&&x.sha256===pair.windows.sha256),`${r.id}: Windows comparison is not a run artifact`);
  requireIt(pair.reference.path!==pair.windows.path,`${r.id}: reference and Windows captures must be distinct files`);
  for(const key of ['reference','windows']) {
   const png=readFileSync(validateArtifact(ctx.root,pair[key]));
   requireIt(png.length>32&&png.subarray(0,8).equals(Buffer.from([137,80,78,71,13,10,26,10]))&&png.toString('ascii',12,16)==='IHDR',`${r.id}: ${key} is not a PNG capture`);
   requireIt(png.readUInt32BE(16)===pair.logical_width*pair.scale_percent/100&&png.readUInt32BE(20)===pair.logical_height*pair.scale_percent/100,`${r.id}: ${key} dimensions differ from declared conditions`);
  }
  const policy=JSON.parse(readFileSync(validateArtifact(ctx.root,pair.policy),'utf8'));
  const measured=JSON.parse(readFileSync(validateArtifact(ctx.root,pair.measurements),'utf8'));
  requireIt(policy.id===r.id&&policy.method===pair.method&&nonempty(policy.authority)&&measured.id===r.id&&measured.status==='PASS'&&measured.reference_sha256===pair.reference.sha256&&measured.windows_sha256===pair.windows.sha256&&measured.policy_sha256===pair.policy.sha256,`${r.id}: failed/unbound visual measurements`);
  requireIt(Object.keys(policy.metrics||{}).length>0&&Object.keys(measured.metrics||{}).length===Object.keys(policy.metrics).length,`${r.id}: missing comparison metrics`);
  for(const [name,limit] of Object.entries(policy.metrics))requireIt(Number.isFinite(limit.max)&&limit.max>=0&&Number.isFinite(measured.metrics[name])&&measured.metrics[name]>=0&&measured.metrics[name]<=limit.max,`${r.id}: ${name} exceeds locked policy`);
  for(const key of ['logical_width','logical_height','scale_percent','theme','state'])requireIt(measured.conditions?.[key]===pair[key],`${r.id}: measurement condition ${key} differs`);
 }
 return a;
}
function coverage(doc,ctx,map,kind) {
 const expected=[...ctx.records.filter(r=>!exempt(r)),...ctx.scope], byId=new Map(expected.map(r=>[r.id,r]));
 requireIt(Array.isArray(doc.coverage),'missing coverage');const seen=new Set();
 for(const row of doc.coverage){
  const r=byId.get(row.id);requireIt(r&&!seen.has(row.id),`unknown/duplicate coverage ID ${row.id}`);seen.add(row.id);
  requireIt(row.status==='PASS',`coverage ${row.id} is ${row.status}`);
  requireIt(row.verification_method===r.verification_method,`verification method changed for ${row.id}`);
  requireIt(nonempty(row.review),`no semantic findings/trace for ${row.id}`);
  const es=refs(row.evidence,map,row.id);
  if(kind==='C') {
   requireIt(!r.blocked_by,`${row.id} is blocked in the ledger`);
   requireIt(r.kind==='release-unit'||['IMPLEMENTED','VERIFIED'].includes(r.status),`${row.id} is not implemented in the ledger`);
   requireIt(Array.isArray(row.implementation_paths)&&row.implementation_paths.length,`no production paths for ${row.id}`);
   for(const p of row.implementation_paths){requireIt(p.startsWith('src/'),`nonproduction path for ${row.id}`);const body=readFileSync(validateArtifact(ctx.root,{path:p,sha256:ctx.hash(p)}),'utf8');requireIt(body.length>0,`empty production path for ${row.id}`);}
   requireIt(es.some(e=>e.kind==='production-review'),`no production inspection for ${row.id}`);
  } else for(const dimension of dimensionsFor(r))requireIt(es.some(e=>e.kind===dimension),`${row.id}: missing ${dimension} evidence`);
  for(const e of es){const a=scopedAssertions(e,r,ctx);if(kind==='C'&&e.kind==='production-review')requireIt(JSON.stringify(a.implementation_paths)===JSON.stringify(row.implementation_paths),`${r.id}: production trace paths differ`);}
 }
 const absent=[...byId.keys()].filter(id=>!seen.has(id));requireIt(!absent.length,`missing coverage ${absent.length}: ${absent.slice(0,12).join(', ')}`);
 // N/A and RETIRED stay evidenced, never quietly vanish from the inventory.
 const exemptions=doc.exemptions||[], exemptRecords=ctx.records.filter(exempt),exemptIds=new Set(exemptRecords.map(r=>r.id));const seenEx=new Set();
 for(const e of exemptions){requireIt(exemptIds.has(e.id)&&!seenEx.has(e.id),`unknown/duplicate exemption ${e.id}`);seenEx.add(e.id);requireIt(nonempty(e.reason),`unjustified exemption ${e.id}`);const receipts=refs(e.evidence,map,e.id);requireIt(receipts.some(r=>r.exemptions?.some(x=>x.id===e.id&&x.status===exemptRecords.find(y=>y.id===e.id).status&&nonempty(x.reason))),`no scoped exemption proof ${e.id}`);}
 requireIt(seenEx.size===exemptIds.size,'missing retirement/N/A evidence');
 return `${expected.length} required records covered, ${exemptIds.size} exemptions evidenced`;
}
export function validateSemanticGate(gate,doc,ctx) {
 requireIt(doc?.schema==='marrow/release-evidence/1','missing release evidence manifest; prepare evidence inputs and collect actual receipts');
 validateBinding(doc.binding,ctx.binding);
 const map=evidenceMap(doc,ctx);
 const neg=refs(doc.negative_checks,map,'negative gate checks');
 for(const name of NEGATIVES)requireIt(neg.some(e=>e.kind==='negative-gate'&&e.negative_cases?.includes(name)),`missing negative control ${name}`);
 if(gate==='C'||gate==='D') {
  const summary=coverage(doc,ctx,map,gate);
  if(gate==='D') {
   // These obligations cannot disappear because individual methods have sparse wording.
   for(const kind of ['unit','contract','interaction','visual','restart','failure','end-to-end'])requireIt([...map.values()].some(e=>e.kind===kind),`Gate D lacks ${kind} evidence`);
  }
  return summary;
 }
 if(gate==='E') {
  requireIt(doc.package?.configuration==='Release'&&doc.package?.runtime==='win-x64'&&nonempty(doc.package?.version)&&nonempty(doc.package?.runtime_version),'missing release artifact/build/runtime identity');
  const exe=readFileSync(validateArtifact(ctx.root,doc.package.artifact));
  requireIt(doc.package.artifact.path.endsWith('.exe')&&exe.length>=64&&exe.toString('ascii',0,2)==='MZ','release package must be a PE installer');
  const pe=exe.readUInt32LE(0x3c);requireIt(pe+26<exe.length&&exe.toString('ascii',pe,pe+4)==='PE\0\0'&&exe.readUInt16LE(pe+4)===0x8664,'release installer must be x64 PE');
  const build=refs(doc.package.build_evidence,map,'package build');
  requireIt(build.some(e=>e.kind==='package-build'&&e.package_sha256===doc.package.artifact.sha256),'no exact-package build receipt');
  const inspectionPath=validateArtifact(ctx.root,doc.package.inspection);
  const inspection=JSON.parse(readFileSync(inspectionPath,'utf8'));
  requireIt(inspection.package_sha256===doc.package.artifact.sha256&&inspection.version===doc.package.version&&inspection.runtime==='win-x64'&&inspection.runtime_version===doc.package.runtime_version&&inspection.configuration==='Release','package inspection disagrees with declared build identity');
  requireIt(inspection.prerequisites?.includes('vcredist143-x64')&&inspection.product==='Marrow'&&inspection.app_id==='MarrowCode'&&inspection.data_identity==='Marrow Code','package inspection omits prerequisite/identity');
  for(const resource of ['cpu','vulkan','vad','test-clip','LICENSE','THIRD_PARTY_NOTICES.md','TRADEMARK.md'])requireIt(inspection.resources?.some(r=>r.name===resource&&/^[a-f0-9]{64}$/.test(r.sha256)),`package lacks ${resource} proof`);
  requireIt(build.some(e=>e.artifacts.some(a=>a.path===doc.package.inspection.path&&a.sha256===doc.package.inspection.sha256)),'inspection is not a package-build artifact');
  for(const name of LIFECYCLE) {
   const es=refs(doc.lifecycle?.[name],map,`lifecycle ${name}`);
   const matching=es.filter(e=>e.kind==='package-lifecycle'&&e.package_sha256===doc.package.artifact.sha256&&e.scenario===name&&e.scenario_result?.name===name);
   for(const [check,expected] of Object.entries(SCENARIO_CHECKS[name]))requireIt(matching.some(e=>e.scenario_result.checks?.some(c=>c.name===check&&c.status==='PASS'&&c.expected===expected&&c.actual===expected)),`${name}: missing exact-package lifecycle assertion ${check}`);
   requireIt(es.some(e=>e.kind==='package-lifecycle'&&e.package_sha256===doc.package.artifact.sha256&&e.scenario===name&&e.scenario_result?.name===name&&e.scenario_result.checks?.length&&e.scenario_result.checks.every(c=>nonempty(c.name)&&c.status==='PASS'&&c.expected!==undefined&&JSON.stringify(c.expected)===JSON.stringify(c.actual))),`missing exact-artifact ${name} assertions`);
  }
  return `Windows 11 x64 package ${doc.package.artifact.sha256}: ${LIFECYCLE.length} lifecycle scenarios evidenced`;
 }
 if(gate==='F') {
  const review=doc.independent_review;requireIt(nonempty(review?.reviewer)&&nonempty(review?.author)&&review.reviewer!==review.author,'independent reviewer must differ from implementation author');
  requireIt(Array.isArray(review.findings)&&review.findings.every(f=>f.status==='RESOLVED'&&nonempty(f.requirement)&&nonempty(f.resolution)),'unresolved parity findings');
  const es=refs(review.evidence,map,'independent review');requireIt(es.every(e=>e.kind==='independent-review'&&e.producer===review.reviewer),'review receipts have wrong reviewer/kind');
  const required=new Set([...ctx.records,...ctx.scope].map(r=>r.id));
  requireIt(Array.isArray(review.reviewed_ids)&&new Set(review.reviewed_ids).size===required.size&&review.reviewed_ids.length===required.size&&review.reviewed_ids.every(id=>required.has(id)),'independent review lacks complete source/product coverage');
  const deviations=ctx.records.filter(r=>r.deviation).map(r=>r.id).sort();requireIt(JSON.stringify((review.deviation_ids||[]).sort())===JSON.stringify(deviations),'approved-deviation inventory differs from ledger');
  requireIt(JSON.stringify((review.owner_decisions||[]).sort())===JSON.stringify([...ctx.ownerDecisions].sort()),'independent review omits owner rulings/additions');
  for(const id of required){const record=[...ctx.records,...ctx.scope].find(r=>r.id===id);requireIt(es.some(e=>e.assertions?.some(a=>a.id===id)),`review lacks findings for ${id}`);for(const e of es.filter(e=>e.assertions?.some(a=>a.id===id)))scopedAssertions(e,record,ctx);}
  return `${required.size} records independently reviewed with no unresolved finding; deviations and owner decisions reconciled`;
 }
 throw Error(`unsupported semantic gate ${gate}`);
}
export function releaseContext(root=process.cwd()) {
 const hash=p=>{const {sha256}=bindingHelpers;return sha256(readFileSync(resolve(root,p)));};
 const scope=json(root,'porting/verification/release-scope.json').records;
 requireIt(isDeepStrictEqual(scope,requiredReleaseScope(root)),'first-release scope differs from required plan/checklist/owner obligations');
 const ownerDecisions=[...readFileSync(resolve(root,'porting/owner_decisions.md'),'utf8').matchAll(/^## (OD-\d+)/gm)].map(m=>m[1]);
 return {root,binding:captureBinding(root),records:ledgerRecords(root),scope,ownerDecisions,hash};
}
import * as bindingHelpers from './evidence-binding.mjs';
import { canonicalPackageIdentity, validatePackageIdentity, validateInspectedBinaries } from './release-package-identity.mjs';
export function checkSemanticGate(gate,root=process.cwd()) {
 const ctx=releaseContext(root),doc=json(root,'porting/verification/release-evidence.json');
 if(gate==='F'){validateRequirementReport(json(root,'porting/verification/requirements.json'),ctx);for(const prior of ['C','D','E'])validateSemanticGate(prior,doc,ctx);}
 const result=validateSemanticGate(gate,doc,ctx);
 if(gate==='E'||gate==='F') {
  const inspection=JSON.parse(readFileSync(validateArtifact(root,doc.package.inspection),'utf8')),identity=canonicalPackageIdentity(root);
  validatePackageIdentity(doc.package,inspection,identity);
  const artifacts=doc.evidence.filter(e=>doc.package.build_evidence.includes(e.id)&&e.kind==='package-build').flatMap(e=>e.artifacts);
  validateInspectedBinaries(doc.package,inspection,identity,root,artifacts);
 }
 return result;
}
