#!/usr/bin/env node
// Prepare evidence inputs only. No tests, native programs, package/installer, signing or network calls.
import { writeFileSync, existsSync } from 'node:fs';
import { releaseContext, LIFECYCLE } from './release-evidence.mjs';
import { exempt } from './requirement-evidence.mjs';
const ctx=releaseContext(),out=process.argv[2]||'porting/verification/release-evidence.template.json';
if(existsSync(out))throw Error(`refusing to overwrite ${out}`);
const doc={schema:'marrow/release-evidence/1',binding:ctx.binding,
 note:'NOT RUN. Populate with authentic exact-revision receipts; copy to release-evidence.json only after collecting evidence. Never hand-set a status to hide missing work.',
 evidence:[],negative_checks:[],
 coverage:[...ctx.records.filter(r=>!exempt(r)),...ctx.scope].map(r=>({id:r.id,verification_method:r.verification_method,status:'NOT_RUN',implementation_paths:[],evidence:[],review:''})),
 exemptions:ctx.records.filter(exempt).map(r=>({id:r.id,reason:r.not_applicable||r.retired_reason||r.b3_note||r.title||'',evidence:[]})),
 package:{configuration:'Release',runtime:'win-x64',version:'',runtime_version:'',artifact:{path:'',sha256:''}},
 lifecycle:Object.fromEntries(LIFECYCLE.map(n=>[n,[]])),
 independent_review:{reviewer:'',author:'',findings:[],evidence:[],reviewed_ids:[],deviation_ids:ctx.records.filter(r=>r.deviation).map(r=>r.id),owner_decisions:ctx.ownerDecisions}};
writeFileSync(out,JSON.stringify(doc,null,2)+'\n');
console.log(`Prepared ${out}: ${doc.coverage.length} required records, ${doc.exemptions.length} exemptions; NO verification credit.`);
