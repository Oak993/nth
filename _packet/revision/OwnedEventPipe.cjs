// Diagnostic-only port of this worktree's existing Core/Port/StackSampler.cs IPC and
// nettrace reader. No package, C# build, source mutation or test invocation is needed.
// The caller must hold a pinned handle to the verified owned testhost for the whole capture.
const net=require('node:net');
const MAGIC=Buffer.from('DOTNET_IPC_V1\0','ascii');
function message(command,payload){const b=Buffer.alloc(20+payload.length);MAGIC.copy(b);b.writeUInt16LE(b.length,14);b[16]=2;b[17]=command;payload.copy(b,20);return b;}
function collectMessage(){
 const parts=[];const u32=n=>{const b=Buffer.alloc(4);b.writeUInt32LE(n);parts.push(b);};
 const u64=n=>{const b=Buffer.alloc(8);b.writeBigUInt64LE(BigInt(n));parts.push(b);};
 const text=s=>{if(!s){u32(0);return;}u32(s.length+1);parts.push(Buffer.from(s+'\0','utf16le'));};
 u32(256);u32(1);parts.push(Buffer.from([1]));u32(2);
 u64(0);u32(4);text('Microsoft-DotNETCore-SampleProfiler');text(null);
 u64(0x18);u32(5);text('Microsoft-Windows-DotNETRuntime');text(null);
 return message(3,Buffer.concat(parts));
}
function stopMessage(session){const b=Buffer.alloc(8);b.writeBigUInt64LE(session);return message(1,b);}
function bounded(promise,milliseconds,label){let timer;return Promise.race([promise,new Promise((_,reject)=>{timer=setTimeout(()=>reject(Error(label)),milliseconds);})]).finally(()=>clearTimeout(timer));}
function openPipe(pipe){return new Promise((resolve,reject)=>{const socket=net.createConnection(pipe);const timer=setTimeout(()=>socket.destroy(Error('IPC connect exceeded 2 seconds')),2000);socket.once('error',reject);socket.once('connect',()=>{clearTimeout(timer);resolve(socket);});socket.once('close',()=>clearTimeout(timer));});}
function readChannel(socket,streaming){
 let header=Buffer.alloc(0),resolved=false,ended=false,total=0,problem=null,replyResolve,replyReject,endResolve;
 const chunks=[];const reply=new Promise((resolve,reject)=>{replyResolve=resolve;replyReject=reject;});
 const end=new Promise(resolve=>{endResolve=resolve;});
 const finish=()=>{if(ended)return;ended=true;if(!resolved)replyReject(problem||Error('IPC closed before reply'));endResolve();};
 const append=data=>{if(!data.length)return;total+=data.length;if(total>128*1024*1024){problem=Error('Diagnostic trace exceeded the 128 MiB local retention bound');socket.destroy(problem);return;}chunks.push(data);};
 socket.on('data',data=>{
  if(resolved){if(streaming)append(data);return;}
  header=Buffer.concat([header,data]);
  if(header.length<20)return;
  const size=header.readUInt16LE(14);
  if(!header.subarray(0,MAGIC.length).equals(MAGIC)||size<20||header[16]!==255){problem=Error('Invalid runtime IPC response header');replyReject(problem);socket.destroy();return;}
  if(header.length<size)return;
  const payload=header.subarray(20,size);
  if(header[17]===255){problem=Error('Runtime IPC error 0x'+(payload.length>=4?payload.readUInt32LE(0).toString(16):'unknown'));replyReject(problem);socket.destroy();return;}
  if(header[17]!==0||payload.length<8){problem=Error('Unexpected runtime IPC response');replyReject(problem);socket.destroy();return;}
  resolved=true;replyResolve(payload.readBigUInt64LE(0));
  if(streaming)append(header.subarray(size));header=Buffer.alloc(0);
 });
 socket.on('error',error=>{problem=error;if(!resolved)replyReject(error);});
 socket.once('end',finish);socket.once('close',finish);
 return{reply,end,bytes:()=>Buffer.concat(chunks),problem:()=>problem?.message||null};
}
async function captureOwned(pipe,milliseconds=2000){
 let collect,stop,reader,session=null,stopAttempted=false;const errors=[];const started=new Date().toISOString();
 try{
  collect=await openPipe(pipe);reader=readChannel(collect,true);collect.write(collectMessage());
  session=await bounded(reader.reply,2000,'Collect reply exceeded 2 seconds');
  await new Promise(resolve=>setTimeout(resolve,milliseconds));
  stopAttempted=true;stop=await openPipe(pipe);const stopReader=readChannel(stop,false);stop.write(stopMessage(session));
  await bounded(stopReader.reply,10000,'StopTracing reply exceeded 10 seconds');
  await bounded(reader.end,10000,'Collect stream did not end within 10 seconds after StopTracing');
 }catch(error){errors.push(String(error));}
 finally{
  // If starting succeeded but a pre-stop observation failed, explicitly stop our session.
  if(session!==null&&!stopAttempted){try{stop=await openPipe(pipe);const s=readChannel(stop,false);stop.write(stopMessage(session));await bounded(s.reply,10000,'Cleanup StopTracing exceeded its bound');}catch(error){errors.push('Cleanup: '+String(error));}}
  if(stop)stop.destroy();if(collect)collect.destroy();
  if(reader){await bounded(reader.end,2000,'Local stream close exceeded bound').catch(error=>errors.push(String(error)));if(reader.problem())errors.push(reader.problem());}
 }
 return{started,ended:new Date().toISOString(),session:session?.toString()||null,stop_attempted:stopAttempted,errors,bytes:reader?.bytes()||Buffer.alloc(0)};
}
function parseTrace(data){
 const metadata=new Map(),stacks=new Map(),samples=[],methods=[];let position=0,pointerSize=8,ended=false,error=null;
 const need=(at,n,end=data.length)=>{if(!Number.isSafeInteger(at)||!Number.isSafeInteger(n)||at<0||n<0||at+n>end)throw Error('Malformed trace at byte '+at);};
 const int32=()=>{need(position,4);const n=data.readInt32LE(position);position+=4;return n;};
 const expect=n=>{need(position,1);if(data[position++]!==n)throw Error('Unexpected tag at byte '+(position-1));};
 const varUInt=end=>{let n=0n;for(let shift=0n;shift<64n;shift+=7n){need(position,1,end);const b=data[position++];n|=BigInt(b&127)<<shift;if(!(b&128))return n;}throw Error('Invalid variable integer at '+position);};
 const varInt=end=>{const n=varUInt(end);if(n>2147483647n)throw Error('Invalid integer at '+position);return Number(n);};
 const utf16=(payload,state)=>{const start=state.at;let end=start;while(end+1<payload.length&&(payload[end]||payload[end+1]))end+=2;state.at=end+2;return payload.subarray(start,Math.min(end,payload.length)).toString('utf16le');};
 function metadataEvent(payload){if(payload.length<4)throw Error('Truncated metadata');const id=payload.readInt32LE(0),state={at:4},provider=utf16(payload,state);if(state.at+4>payload.length)throw Error('Truncated metadata event');metadata.set(id,{provider,id:payload.readInt32LE(state.at)});}
 function event(id,thread,stack,payload){
  const kind=metadata.get(id);if(!kind)return;
  if(kind.provider==='Microsoft-DotNETCore-SampleProfiler'&&kind.id===0){samples.push({thread:thread.toString(),external:payload.length>=4&&payload.readUInt32LE(0)===1,frames:[...(stacks.get(stack)||[])]});}
  else if((kind.provider==='Microsoft-Windows-DotNETRuntime'&&kind.id===143)||(kind.provider==='Microsoft-Windows-DotNETRuntimeRundown'&&kind.id===144)){
   if(payload.length<36)return;const start=payload.readBigUInt64LE(16),size=payload.readUInt32LE(24),state={at:36};
   const type=utf16(payload,state),name=utf16(payload,state),signature=utf16(payload,state);methods.push({start,end:start+BigInt(size),name:type+'.'+name+'('+signature+')'});
  }
 }
 function eventBlock(start,end,isMetadata){
  need(start,4,end);const header=data.readUInt16LE(start),flags=data.readUInt16LE(start+2);if(!(flags&1)||header<4||start+header>end)throw Error('Unsupported event block at '+start);
  position=start+header;let id=0,stack=0,size=0,thread=0n;
  while(position<end){const flags=data[position++];if(flags&1)id=varInt(end);if(flags&2){varUInt(end);varUInt(end);varUInt(end);}if(flags&4)thread=varUInt(end);if(flags&8)stack=varInt(end);varUInt(end);if(flags&16)position+=16;if(flags&32)position+=16;if(flags&128)size=varInt(end);need(position,size,end);const payload=data.subarray(position,position+size);position+=size;if(isMetadata)metadataEvent(payload);else event(id,thread,stack,payload);}
 }
 function stackBlock(start,end){position=start;need(position,8,end);const first=int32(),count=int32();if(count<0)throw Error('Negative stack count');for(let i=0;i<count;i++){need(position,4,end);const size=int32();need(position,size,end);if(size%pointerSize)throw Error('Misaligned stack');const frames=[];for(let j=0;j<size;j+=pointerSize)frames.push(pointerSize===8?data.readBigUInt64LE(position+j):BigInt(data.readUInt32LE(position+j)));position+=size;stacks.set(first+i,frames);}}
 try{
  need(0,8);if(data.subarray(0,8).toString('ascii')!=='Nettrace')throw Error('Nettrace header absent');position=8;
  if(int32()!==20)throw Error('Unsupported FastSerialization header');need(position,20);if(data.subarray(position,position+20).toString('ascii')!=='!FastSerialization.1')throw Error('FastSerialization magic absent');position+=20;
  while(true){need(position,1);if(data[position]===1){position++;ended=true;break;}expect(5);expect(5);expect(1);int32();int32();const length=int32();need(position,length);const name=data.subarray(position,position+length).toString('ascii');position+=length;expect(6);
   if(name==='Trace'){need(position,48);pointerSize=data.readInt32LE(position+32);if(pointerSize!==4&&pointerSize!==8)throw Error('Invalid pointer size');position+=48;}
   else if(['MetadataBlock','EventBlock','StackBlock','SPBlock'].includes(name)){const size=int32(),padding=(4-position%4)%4;need(position,padding+size);const start=position+padding,end=start+size;if(name==='MetadataBlock'||name==='EventBlock')eventBlock(start,end,name==='MetadataBlock');else if(name==='StackBlock')stackBlock(start,end);position=end;}
   else throw Error('Unsupported trace object '+name+' at '+position);expect(6);
  }
 }catch(e){error=String(e);}
 methods.sort((a,b)=>a.start<b.start?-1:a.start>b.start?1:0);let unresolved=0,resolved=0;
 const resolve=ip=>{let lo=0,hi=methods.length-1,found=-1;while(lo<=hi){const mid=(lo+hi)>>1;if(methods[mid].start<=ip){found=mid;lo=mid+1;}else hi=mid-1;}if(found>=0&&ip<methods[found].end){resolved++;return methods[found].name;}unresolved++;return'[unresolved 0x'+ip.toString(16).toUpperCase()+']';};
 const threads=new Map();for(const sample of samples){const frames=sample.frames.map(resolve);if(sample.external)frames.unshift('[Native Frames]');const key=frames.join('\n');if(!threads.has(sample.thread))threads.set(sample.thread,new Map());const unique=threads.get(sample.thread);unique.set(key,(unique.get(key)||0)+1);}
 const report=[`Nettrace complete=${ended}; consumed=${position}/${data.length}; samples=${samples.length}; methods=${methods.length}; resolved_frames=${resolved}; unresolved_frames=${unresolved}; error=${error||'none'}`,''];
 for(const [thread,groups] of [...threads].sort((a,b)=>BigInt(a[0])<BigInt(b[0])?-1:1)){report.push('Thread '+thread+':');for(const [stack,count]of[...groups].sort((a,b)=>b[1]-a[1]))report.push('  '+count+' samples',...stack.split('\n').map(line=>'    '+line));report.push('');}
 return{ended,error,consumed:position,bytes:data.length,samples:samples.length,methods:methods.length,threads:threads.size,resolved_frames:resolved,unresolved_frames:unresolved,report:report.join('\n')};
}
module.exports={captureOwned,parseTrace,collectMessage,stopMessage};
