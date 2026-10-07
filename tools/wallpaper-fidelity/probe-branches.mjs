// Read-only probes of the ORIGINAL highp GLSL in the fixture's pinned Chromium/SwiftShader.
// Diagnostic outputs use RGBA32F; no reference file or production shader is edited.
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { resolve, join } from 'node:path';
import { chromium } from 'playwright-core';
import { execFileSync } from 'node:child_process';

const root = resolve(import.meta.dirname, '../..');
const output = resolve(process.argv[2] || '/tmp/marrow-branch-probes');
await mkdir(output, { recursive: true });
const data = JSON.parse(await readFile(join(root, 'porting/first-release/design-src/wallpaper-library/static/wallpapers.json')));
const manifest = JSON.parse(await readFile(join(root, 'tests/fixtures/wallpapers/reference/manifest.json')));
const browser = await chromium.launch({ executablePath: process.env.WALLPAPER_CHROMIUM || '/usr/bin/chromium', headless: true, args: manifest.launchArgs });
if (browser.version() !== manifest.browser) throw new Error('Reference Chromium version differs');
const page = await browser.newPage();
const records = [];
try {
  for (const id of ['tunnel', 'stairwell']) for (const variant of ['default', 'perspective']) {
    const frame = manifest.renders.find(r => r.wallpaper === id && r.variant === variant && !r.dark);
    const wall = data.wallpapers.find(w => w.id === id);
    const prefix = data.contract.head + data.contract.lib + wall.src;
    const uniforms = { u_res: [160,100], u_dpr: 1, u_pal: frame.uniforms.palette, u_dark: 0, u_grain: frame.grain,
      u_opt: frame.options.option, u_tilt: frame.uniforms.tilt, u_tg: frame.uniforms.tg,
      u_light: frame.uniforms.light, u_light0: frame.uniforms.light0, u_lw: frame.uniforms.lw, u_focus: 0 };
    const p = 'vec2 fc = gl_FragCoord.xy; vec2 p = (fc/u_res - .5)*2.*vec2(u_res.x/u_res.y, 1.); p += u_tilt*u_tg;';
    const body = id === 'tunnel' ? wall.src.slice(wall.src.indexOf('{') + 1, wall.src.indexOf('\n}')) : wall.src.slice(wall.src.indexOf('{') + 1, wall.src.indexOf('  float fw'));
    const diagnostics = id === 'tunnel' ? {
      coordinates: 'o = vec4(p, 0., 1.);',
      center: 'float k=3.;'+body.slice(0,body.indexOf('  float a'))+'o=vec4(c,r,1.);',
      angle: 'float k=3.;'+body.slice(0,body.indexOf('  return'))+'o=vec4(q,a,1.);',
      distance: 'o=vec4(holeT(p,2.),holeT(p,3.),holeT(p,4.),1.);',
      terms: 'float k=3.;'+body.slice(0,body.indexOf('  return'))+'o=vec4(length(q),sin(2.*a+k*1.3),sin(3.*a-k*2.1),1.);',
      loop: 'vec3 diag=vec3(5.,1.,-1.); float done=0.; float prevD=-1.; for(int j=0;j<6;j++){float k=float(j);float d=j<5?holeT(p,k):1.;if(done<.5&&d>0.){done=1.;diag=vec3(k,d,prevD);}if(done<.5)prevD=d;}o=vec4(diag,1.);',
    } : {
      coordinates: 'o=vec4(p,0.,1.);',
      angle: body+'o=vec4(r,th,lr,1.);',
      distance: body+'o=vec4(s,f,step(.06,f),1.);',
    };
    // Preserve the original loop-variable evaluation. A literal holeT(p,3.) call
    // lets ANGLE fold sin/cos/pow in double and does NOT trace the shipped wall.
    if (id === 'tunnel') {
      for (const stage of ['center','angle','terms']) {
        const code = diagnostics[stage].replace('float k=3.;','float k=float(j);');
        const at = code.indexOf('o=');
        diagnostics[stage] = 'vec4 diagnostic=vec4(0.);vec2 originalP=p;for(int j=0;j<6;j++){vec2 p=originalP;'+code.slice(0,at)
          +'if(j==3)diagnostic='+code.slice(at+2)+'}o=diagnostic;';
      }
      diagnostics.distance = 'vec3 diagnostic=vec3(0.);for(int j=0;j<6;j++){float d=j<5?holeT(p,float(j)):1.;if(j==2)diagnostic.x=d;if(j==3)diagnostic.y=d;if(j==4)diagnostic.z=d;}o=vec4(diagnostic,1.);';
    }
    for (const [stage, debug] of Object.entries(diagnostics)) {
      const name = `${id}-${variant}-${stage}`;
      const glsl = prefix + 'void main(){'+p+debug+'}';
      const result = await page.evaluate(({ glsl, uniforms }) => {
        const gl = document.createElement('canvas').getContext('webgl2');
        if (!gl.getExtension('EXT_color_buffer_float')) throw new Error('Float targets unavailable');
        const info = gl.getExtension('WEBGL_debug_renderer_info');
        const renderer = gl.getParameter(info.UNMASKED_RENDERER_WEBGL);
        if (!/SwiftShader/.test(renderer)) throw new Error(renderer);
        function shader(type, source) { const s=gl.createShader(type); gl.shaderSource(s,source); gl.compileShader(s); if(!gl.getShaderParameter(s,gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(s)); return s; }
        const program=gl.createProgram();
        gl.attachShader(program,shader(gl.VERTEX_SHADER,'#version 300 es\nvoid main(){vec2 a=vec2(float((gl_VertexID<<1)&2),float(gl_VertexID&2));gl_Position=vec4(a*2.-1.,0.,1.);}'));
        gl.attachShader(program,shader(gl.FRAGMENT_SHADER,glsl)); gl.linkProgram(program);
        if(!gl.getProgramParameter(program,gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(program));
        gl.useProgram(program);
        for(const [name,v] of Object.entries(uniforms)) { const u=gl.getUniformLocation(program,name); if(Array.isArray(v)) { if(name==='u_pal')gl.uniform3fv(u,v);else gl.uniform2fv(u,v); }else gl.uniform1f(u,v); }
        const tex=gl.createTexture();gl.bindTexture(gl.TEXTURE_2D,tex);gl.texStorage2D(gl.TEXTURE_2D,1,gl.RGBA32F,160,100);
        const fb=gl.createFramebuffer();gl.bindFramebuffer(gl.FRAMEBUFFER,fb);gl.framebufferTexture2D(gl.FRAMEBUFFER,gl.COLOR_ATTACHMENT0,gl.TEXTURE_2D,tex,0);
        if(gl.checkFramebufferStatus(gl.FRAMEBUFFER)!==gl.FRAMEBUFFER_COMPLETE)throw new Error('Framebuffer incomplete');
        gl.viewport(0,0,160,100);gl.drawArrays(gl.TRIANGLES,0,3);
        const px=new Float32Array(160*100*4);gl.readPixels(0,0,160,100,gl.RGBA,gl.FLOAT,px);
        const flipped=new Float32Array(px.length);for(let y=0;y<100;y++)flipped.set(px.subarray((99-y)*640,(100-y)*640),y*640);
        const bytes=new Uint8Array(flipped.buffer);let binary='';for(const b of bytes)binary+=String.fromCharCode(b);
        gl.getExtension('WEBGL_lose_context').loseContext();return {data:btoa(binary),renderer};
      }, {glsl,uniforms});
      await writeFile(join(output,name+'-swift.bin'),Buffer.from(result.data,'base64'));
      const main = ('half4 main(float2 coord){'+p+debug+'}').replaceAll('gl_FragCoord.xy','float2(coord.x,u_res.y-coord.y)').replaceAll('vec2','float2').replaceAll('vec4','half4').replaceAll('o=','return ').replaceAll('o =','return ');
      const request=join(output,name+'.json');await writeFile(request,JSON.stringify({wallpaper:id,uniforms,main}));
      execFileSync(process.env.DOTNET || 'dotnet',['run','--no-build','--project',join(import.meta.dirname,'branch-probe'), '--',request,join(output,name+'-skia.bin')],{stdio:'pipe'});
      records.push({name,renderer:result.renderer,reference:frame.file,uniforms});
      console.log(name);
    }
  }
  await writeFile(join(output,'index.json'),JSON.stringify({browser:browser.version(),records},null,2)+'\n');
} finally { await browser.close(); }
