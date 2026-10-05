const fs = require('node:fs');
const path = require('node:path');
const readline = require('node:readline');
const [root, log] = process.argv.slice(2);
const skill = { name: 'marrow-harbor-readiness', description: 'Checks whether the harbor lantern is ready and gives its exact readiness answer.', folder: path.join(root, 'marrow-harbor-readiness') };
function record(value) { fs.appendFileSync(log, JSON.stringify(value) + '\n'); }
record({event:'started',pid:process.pid,root});
readline.createInterface({input:process.stdin}).on('line', line => {
  const request = JSON.parse(line); record({direction:'stdin',frame:request});
  if (request.id === undefined) return;
  let result;
  try {
    switch (request.method) {
      case 'initialize': result = {protocolVersion:request.params.protocolVersion,capabilities:{tools:{}},serverInfo:{name:'marrow-skills',version:'1.0.0'},instructions:'Use list_skills to discover relevant Marrow skills. Read a relevant skill with read_skill before answering. These are read-only skills.'}; break;
      case 'ping': result = {}; break;
      case 'tools/list': result = {tools:[
        {name:'list_skills',description:'List the names, descriptions and folders of Marrow-owned skills to find instructions relevant to the current request.',inputSchema:{type:'object',properties:{},additionalProperties:false},annotations:{readOnlyHint:true,idempotentHint:true}},
        {name:'read_skill',description:'Read the complete instructions of a relevant Marrow-owned skill by name.',inputSchema:{type:'object',properties:{name:{type:'string'}},required:['name'],additionalProperties:false},annotations:{readOnlyHint:true,idempotentHint:true}}
      ]}; break;
      case 'tools/call': {
        if (request.params.name === 'list_skills') result = {content:[{type:'text',text:JSON.stringify([skill])}]};
        else if (request.params.name === 'read_skill' && request.params.arguments?.name === skill.name) {
          const text = fs.readFileSync(path.join(skill.folder,'SKILL.md'),'utf8');
          record({event:'skill_read',name:skill.name,path:path.join(skill.folder,'SKILL.md')});
          result = {content:[{type:'text',text}]};
        } else result = {isError:true,content:[{type:'text',text:'Unknown Marrow skill or tool.'}]};
        break;
      }
      default: throw new Error('Unsupported method');
    }
    const response = {jsonrpc:'2.0',id:request.id,result}; record({direction:'stdout',frame:response}); process.stdout.write(JSON.stringify(response)+'\n');
  } catch (e) { process.stdout.write(JSON.stringify({jsonrpc:'2.0',id:request.id,error:{code:-32601,message:e.message}})+'\n'); }
});
