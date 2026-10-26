import { chromium } from '@playwright/test';
import { mkdir, writeFile } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { join } from 'node:path';
const baselineIndex=process.argv.indexOf('--baseline');
const baseline=baselineIndex>=0?process.argv[baselineIndex+1]:null;
if(baselineIndex>=0&&(!baseline||!/^[a-zA-Z0-9][a-zA-Z0-9._/-]*$/.test(baseline)))throw Error('Provide a Git revision after --baseline');
const label=baseline?'before':'after';
const output=fileURLToPath(new URL('../../artifacts/tanks-performance/',import.meta.url));
await mkdir(output,{recursive:true});
const baselineSource=baseline?execFileSync('git',['show',baseline+':frontend/public/games/tanks/render.js'],{encoding:'utf8',cwd:fileURLToPath(new URL('../../',import.meta.url))}):null;
const browser=await chromium.launch({channel:'msedge',headless:true});
const results=[];
try {
for(const config of [{name:'desktop',width:1440,height:900,dpr:1,throttle:1},{name:'mobile-cpu4x',width:390,height:844,dpr:2,throttle:4}]){
 const page=await browser.newPage({viewport:{width:config.width,height:config.height},deviceScaleFactor:config.dpr});
 const client=await page.context().newCDPSession(page);await client.send('Emulation.setCPUThrottlingRate',{rate:config.throttle});
 if(baselineSource)await page.route('**/games/tanks/render.js',route=>route.fulfill({contentType:'text/javascript',body:baselineSource}));
 await page.route('**/games/tanks/perf-harness.html',route=>route.fulfill({contentType:'text/html',body:`<!doctype html><style>body{margin:0}canvas{width:100vw;height:100vh;display:block}</style><canvas id="c"></canvas><script type="module">
 import {createWorld} from './world.js';import {createRenderer} from './render.js';
 let uploadBytes=0,uploads=0,staticBytes=0,staticUploads=0,draws=0,vertices=0;
 const proto=WebGLRenderingContext.prototype,original=proto.bufferData,originalSub=proto.bufferSubData,originalDraw=proto.drawArrays;
 proto.bufferData=function(target,data,usage){const bytes=typeof data==='number'?data:data?.byteLength||0;if(usage===this.STATIC_DRAW){staticBytes+=bytes;staticUploads++;}else{uploadBytes+=bytes;uploads++;}return original.call(this,target,data,usage);};
 proto.bufferSubData=function(target,offset,data){uploadBytes+=data?.byteLength||0;uploads++;return originalSub.call(this,target,offset,data);};
 proto.drawArrays=function(mode,first,count){draws++;vertices+=count;return originalDraw.call(this,mode,first,count);};
 const world=createWorld(()=>.5);world.enemies=Array.from({length:7},(_,i)=>({x:(i-3)*4,z:-8,yaw:0,turret:0,hp:100,maxHp:100}));
 world.pickups=Array.from({length:36},(_,i)=>({id:i,x:(i%6-2.5)*6,z:(Math.floor(i/6)-2.5)*7,type:['speed','shield','damage','double','repair'][i%5],phase:i}));
 world.bullets=Array.from({length:32},(_,i)=>({x:(i%8-4)*3,z:Math.floor(i/8)*4,vx:1,vz:1}));world.player.skills={shield:20};
 const renderer=createRenderer(document.getElementById('c'));
 const gl=document.getElementById('c').getContext('webgl'),ext=gl.getExtension('WEBGL_debug_renderer_info');
 window.gpu=ext?gl.getParameter(ext.UNMASKED_RENDERER_WEBGL):'unknown';
 window.benchmark=async()=>{
 const timings=[],frames=[];let last=0;
 for(let i=0;i<150;i++){
  await new Promise(resolve=>requestAnimationFrame(resolve));const start=performance.now();
  if(i===30){uploadBytes=uploads=draws=vertices=0;}
  world.time+=1/60;world.player.x=Math.sin(i/90)*3;renderer.render(world,1/60);
  if(i>=30){timings.push(performance.now()-start);if(last)frames.push(start-last);last=start;}
 }
 const summarize=a=>{a.sort((a,b)=>a-b);return {median:+a[Math.floor(a.length*.5)].toFixed(2),p95:+a[Math.floor(a.length*.95)].toFixed(2),max:+a.at(-1).toFixed(2)};};
 return {renderMs:summarize(timings),frameMs:summarize(frames),dynamicBytesPerFrame:Math.round(uploadBytes/120),uploadsPerFrame:uploads/120,drawsPerFrame:draws/120,verticesPerFrame:vertices/120,staticBytes,staticUploads,gpu:window.gpu};
 };
 </script>`}));
 await page.goto('http://127.0.0.1:5150/games/tanks/perf-harness.html');await page.waitForFunction(()=>!!window.benchmark);
 const result=await page.evaluate(()=>window.benchmark());results.push({scenario:config.name,...result});console.log(JSON.stringify(results.at(-1)));
 await page.screenshot({path:join(output,label+'-'+config.name+'.png')});
 await page.close();
}
await writeFile(join(output,label+'.json'),JSON.stringify(results,null,2));
}finally{await browser.close();}
