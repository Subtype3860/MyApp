import { OBSTACLES, HALF_SIZE } from './world.js';
const COLORS={speed:[.4,.84,.67],shield:[.44,.74,.93],damage:[1,.7,.36],double:[.77,.63,.95],repair:[.93,.53,.6]};
function normalize(a){const l=Math.hypot(...a)||1;return a.map(x=>x/l);}
const cross=(a,b)=>[a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]];
const dot=(a,b)=>a.reduce((s,v,i)=>s+v*b[i],0);
function multiply(a,b){const out=new Float32Array(16);for(let c=0;c<4;c++)for(let r=0;r<4;r++)for(let k=0;k<4;k++)out[c*4+r]+=a[k*4+r]*b[c*4+k];return out;}
function perspective(aspect){const f=1/Math.tan(Math.PI/7);const n=.2,far=160;return new Float32Array([f/aspect,0,0,0,0,f,0,0,0,0,(far+n)/(n-far),-1,0,0,2*far*n/(n-far),0]);}
function view(eye,target){const z=normalize(eye.map((v,i)=>v-target[i])),x=normalize(cross([0,1,0],z)),y=cross(z,x);return new Float32Array([x[0],y[0],z[0],0,x[1],y[1],z[1],0,x[2],y[2],z[2],0,-dot(x,eye),-dot(y,eye),-dot(z,eye),1]);}
// Cube faces carry separate normals for crisp, directional low-poly lighting.
const FACES=[[[1,0,0],[[.5,-.5,-.5],[.5,.5,-.5],[.5,.5,.5],[.5,-.5,.5]]],[[-1,0,0],[[-.5,-.5,.5],[-.5,.5,.5],[-.5,.5,-.5],[-.5,-.5,-.5]]],[[0,1,0],[[-.5,.5,-.5],[-.5,.5,.5],[.5,.5,.5],[.5,.5,-.5]]],[[0,-1,0],[[-.5,-.5,.5],[-.5,-.5,-.5],[.5,-.5,-.5],[.5,-.5,.5]]],[[0,0,1],[[.5,-.5,.5],[.5,.5,.5],[-.5,.5,.5],[-.5,-.5,.5]]],[[0,0,-1],[[-.5,-.5,-.5],[-.5,.5,-.5],[.5,.5,-.5],[.5,-.5,-.5]]]];
export function createRenderer(canvas){const gl=canvas.getContext('webgl',{antialias:true,alpha:false,powerPreference:'high-performance'});if(!gl)throw Error('WebGL unavailable');
  function shader(type,source){const s=gl.createShader(type);gl.shaderSource(s,source);gl.compileShader(s);if(!gl.getShaderParameter(s,gl.COMPILE_STATUS))throw Error('Shader compilation failed');return s;}
  const program=gl.createProgram();gl.attachShader(program,shader(gl.VERTEX_SHADER,`attribute vec3 aPosition;attribute vec3 aNormal;attribute vec3 aColor;uniform mat4 uMatrix;varying vec3 vColor;varying float vFog;void main(){vec4 p=uMatrix*vec4(aPosition,1.);gl_Position=p;float light=.42+.58*max(dot(normalize(aNormal),normalize(vec3(-.4,1.,.5))),0.);vColor=aColor*light;vFog=clamp((p.w-30.)/85.,0.,.65);}`));gl.attachShader(program,shader(gl.FRAGMENT_SHADER,`precision mediump float;varying vec3 vColor;varying float vFog;void main(){gl_FragColor=vec4(mix(vColor,vec3(.065,.115,.14),vFog),1.);}`));gl.linkProgram(program);if(!gl.getProgramParameter(program,gl.LINK_STATUS))throw Error('Shader link failed');gl.useProgram(program);
  const staticBuffer=gl.createBuffer(),dynamicBuffer=gl.createBuffer();
  const attributes=['aPosition','aNormal','aColor'].map(name=>gl.getAttribLocation(program,name));
  function bind(buffer){gl.bindBuffer(gl.ARRAY_BUFFER,buffer);for(let i=0;i<attributes.length;i++){gl.enableVertexAttribArray(attributes[i]);gl.vertexAttribPointer(attributes[i],3,gl.FLOAT,false,36,i*12);}}
  const matrixLoc=gl.getUniformLocation(program,'uMatrix');gl.enable(gl.DEPTH_TEST);gl.clearColor(.065,.115,.14,1);
  let vertices=new Float32Array(65536),vertexLength=0,reservedBytes=0,cameraX=0,cameraZ=0,eye=[0,27,24],target=[0,0,0],aspect=1;
  const indices=[0,1,2,0,2,3],planes=new Float32Array(24);
  function reserve(count){if(vertexLength+count<=vertices.length)return;const grown=new Float32Array(Math.max(vertices.length*2,vertexLength+count));grown.set(vertices);vertices=grown;}
  function vertex(x,y,z,nx,ny,nz,color){vertices[vertexLength++]=x;vertices[vertexLength++]=y;vertices[vertexLength++]=z;vertices[vertexLength++]=nx;vertices[vertexLength++]=ny;vertices[vertexLength++]=nz;vertices[vertexLength++]=color[0];vertices[vertexLength++]=color[1];vertices[vertexLength++]=color[2];}
  function box(x,y,z,w,h,d,color,yaw=0){reserve(324);const c=Math.cos(yaw),s=Math.sin(yaw);for(const[n,points]of FACES){const nx=n[0]*c+n[2]*s,nz=-n[0]*s+n[2]*c;for(const i of indices){const p=points[i],px=p[0]*w,pz=p[2]*d;vertex(x+px*c+pz*s,y+p[1]*h,z-px*s+pz*c,nx,n[1],nz,color);}}}
  const circle=Array.from({length:25},(_,i)=>[Math.sin(i/24*Math.PI*2),Math.cos(i/24*Math.PI*2)]);
  function ring(x,z,r,color,y=.08,width=.08){reserve(24*6*9);const outer=r+width/2,inner=r-width/2;for(let i=0;i<24;i++){const a=circle[i],b=circle[i+1];vertex(x+a[0]*outer,y,z+a[1]*outer,0,1,0,color);vertex(x+b[0]*outer,y,z+b[1]*outer,0,1,0,color);vertex(x+a[0]*inner,y,z+a[1]*inner,0,1,0,color);vertex(x+a[0]*inner,y,z+a[1]*inner,0,1,0,color);vertex(x+b[0]*outer,y,z+b[1]*outer,0,1,0,color);vertex(x+b[0]*inner,y,z+b[1]*inner,0,1,0,color);}}
  function shadow(x,z,w,d){reserve(54);const color=[.075,.12,.12],left=x-w/2,right=x+w/2,front=z-d/2,back=z+d/2;vertex(left,.025,front,0,1,0,color);vertex(right,.025,front,0,1,0,color);vertex(left,.025,back,0,1,0,color);vertex(left,.025,back,0,1,0,color);vertex(right,.025,front,0,1,0,color);vertex(right,.025,back,0,1,0,color);}
  function updateFrustum(m){let offset=0;for(let axis=0;axis<3;axis++)for(const sign of [-1,1]){const a=m[3]+sign*m[axis],b=m[7]+sign*m[4+axis],c=m[11]+sign*m[8+axis],d=m[15]+sign*m[12+axis],length=Math.hypot(a,b,c);planes[offset++]=a/length;planes[offset++]=b/length;planes[offset++]=c/length;planes[offset++]=d/length;}}
  function visible(x,y,z,radius){for(let i=0;i<24;i+=4)if(planes[i]*x+planes[i+1]*y+planes[i+2]*z+planes[i+3]<-radius)return false;return true;}
  function tank(t,color,isPlayer=false){shadow(t.x+.2,t.z+.2,2.25,2.7);const a=t.yaw,c=Math.cos(a),s=Math.sin(a);function local(x,y,z,w,h,d,col){box(t.x+x*c+z*s,y,t.z-x*s+z*c,w,h,d,col,a);}
    local(-.91,.4,0,.48,.65,2.65,[.095,.14,.15]);local(.91,.4,0,.48,.65,2.65,[.095,.14,.15]);for(const side of [-1,1])for(let k=0;k<5;k++)local(side*.93,.67,-1.04+k*.51,.52,.07,.15,[.23,.29,.28]);
    local(0,.8,0,1.6,.6,2.3,color);local(0,1.13,-.7,1.25,.08,.45,color.map(v=>v*.72));
    box(t.x,1.4,t.z,.95,.55,1.15,color.map(v=>Math.min(1,v*1.14)),t.turret);box(t.x+Math.sin(t.turret)*1.05,1.48,t.z+Math.cos(t.turret)*1.05,.2,.19,1.75,[.36,.43,.39],t.turret);box(t.x+Math.sin(t.turret)*1.95,1.48,t.z+Math.cos(t.turret)*1.95,.29,.29,.3,[.16,.22,.22],t.turret);box(t.x,1.73,t.z-.05,.42,.1,.42,[.22,.34,.31],t.turret);
    if(isPlayer){ring(t.x,t.z,1.8,[.36,.68,.52]);if(t.skills.shield>0||t.protectedSpawn){ring(t.x,t.z,2.1,[.4,.72,.94],.4,.1);ring(t.x,t.z,2,[.3,.55,.7],.8,.06);}if(t.skills.speed>0){local(-.5,.5,-1.7,.25,.17,.8,[.5,.93,.73]);local(.5,.5,-1.7,.25,.17,.8,[.5,.93,.73]);}}else{box(t.x,2.4,t.z,1.5,.08,.15,[.2,.2,.19]);box(t.x-(1-t.hp/(t.maxHp||60))*.7,2.41,t.z,Math.max(.1,1.5*Math.min(1,t.hp/(t.maxHp||60))),.09,.16,[.89,.4,.27]);}}
  // Terrain is immutable: build and upload once, rather than rebuilding it each frame.
  function buildTerrain(){
    box(0,-.28,0,HALF_SIZE*2+3,.5,HALF_SIZE*2+3,[.2,.29,.25]);box(0,-.015,0,8,.02,HALF_SIZE*2-2,[.24,.29,.27]);box(0,-.012,0,HALF_SIZE*2-2,.02,7,[.24,.29,.27]);for(let i=-HALF_SIZE+4;i<=HALF_SIZE-4;i+=4){box(i,.006,0,1.6,.025,.11,[.43,.47,.37]);box(0,.007,i,.11,.025,1.6,[.43,.47,.37]);}
    for(let i=-HALF_SIZE;i<=HALF_SIZE;i+=4){box(i,.25,-HALF_SIZE,3.6,.5,.4,[.33,.4,.34]);box(i,.25,HALF_SIZE,3.6,.5,.4,[.33,.4,.34]);box(-HALF_SIZE,.25,i,.4,.5,3.6,[.33,.4,.34]);box(HALF_SIZE,.25,i,.4,.5,3.6,[.33,.4,.34]);}
    // Repeated field patches and trees are deterministic; no art or network downloads.
    for(let i=0;i<70;i++){const x=Math.sin(i*72.13)*(HALF_SIZE-3),z=Math.cos(i*31.7)*(HALF_SIZE-3);if(Math.abs(x)<5||Math.abs(z)<5)continue;box(x,-.005,z,2.3,.025,1.2,[.23,.32,.26],i);if(i%3===0){shadow(x,z,2.4,2.4);box(x,.7,z,.35,1.4,.35,[.3,.25,.17]);box(x,1.9,z,1.7,1.8,1.7,[.15,.3,.23],i);box(x,2.9,z,1.1,1.1,1.1,[.2,.36,.27],i+.4);}}
    for(const o of OBSTACLES){shadow(o.x+.4,o.z+.4,o.w+.4,o.d+.4);box(o.x,o.h/2,o.z,o.w,o.h,o.d,[.38,.43,.37]);box(o.x,o.h+.15,o.z,o.w+.2,.3,o.d+.2,[.47,.49,.4]);for(let i=0;i<3;i++)box(o.x-o.w/3+i*o.w/3,o.h*.55,o.z+o.d/2+.015,.35,.5,.03,[.13,.22,.24]);}
  }
  buildTerrain();const staticVertexCount=vertexLength/9;bind(staticBuffer);gl.bufferData(gl.ARRAY_BUFFER,vertices.subarray(0,vertexLength),gl.STATIC_DRAW);
  vertices=new Float32Array(65536);vertexLength=0;
  function render(world,dt=0,aim){const ratio=Math.min(window.devicePixelRatio||1,1.7);const width=Math.round(canvas.clientWidth*ratio),height=Math.round(canvas.clientHeight*ratio);if(canvas.width!==width||canvas.height!==height){canvas.width=width;canvas.height=height;gl.viewport(0,0,width,height);}aspect=width/height;
    const follow=1-Math.exp(-Math.min(dt,.1)*8);cameraX+=(world.player.x-cameraX)*follow;cameraZ+=(world.player.z-cameraZ)*follow;const distance=aspect<1?37:27;eye=[cameraX,distance,cameraZ+distance*.88];target=[cameraX,0,cameraZ-2];const matrix=multiply(perspective(aspect),view(eye,target));gl.uniformMatrix4fv(matrixLoc,false,matrix);updateFrustum(matrix);vertexLength=0;
    for(const item of world.pickups){if(!visible(item.x,1,item.z,2))continue;const color=COLORS[item.type],a=world.time+item.phase,bob=Math.sin(a*2)*.16;shadow(item.x,item.z,1.5,1.5);ring(item.x,item.z,1.15,color);box(item.x,.85+bob,item.z,.8,.8,.8,color,a*.6);box(item.x,.85+bob,item.z,.9,.14,.9,[.85,.92,.8],a*.6);if(item.type==='repair'){box(item.x,1.33+bob,item.z,.2,.03,.58,[1,.9,.9],a*.6);box(item.x,1.35+bob,item.z,.58,.03,.2,[1,.9,.9],a*.6);}}
    for(const e of world.enemies)if(visible(e.x,1,e.z,3))tank(e,[.63,.3,.21]);if(world.player.hp>0)tank(world.player,[.32,.63,.49],true);
    for(const b of world.bullets){if(!visible(b.x,1.4,b.z,1))continue;box(b.x,1.4,b.z,.18,.18,.65,b.enemy?[1,.53,.26]:[.94,.92,.55],Math.atan2(b.vx,b.vz));}
    for(const f of world.particles){if(!visible(f.x,f.y,f.z,.5))continue;const size=(1-f.age/.8)*.28;box(f.x,f.y,f.z,size,size,size,f.color,f.age*6);}
    if(aim)ring(aim.x,aim.z,.55,[.75,.87,.72],.12,.07);
    gl.clear(gl.COLOR_BUFFER_BIT|gl.DEPTH_BUFFER_BIT);bind(staticBuffer);gl.drawArrays(gl.TRIANGLES,0,staticVertexCount);
    bind(dynamicBuffer);if(reservedBytes<vertices.byteLength){reservedBytes=vertices.byteLength;gl.bufferData(gl.ARRAY_BUFFER,reservedBytes,gl.DYNAMIC_DRAW);}
    gl.bufferSubData(gl.ARRAY_BUFFER,0,vertices.subarray(0,vertexLength));gl.drawArrays(gl.TRIANGLES,0,vertexLength/9);
  }
  function groundPoint(clientX,clientY){const rect=canvas.getBoundingClientRect(),nx=(clientX-rect.left)/rect.width*2-1,ny=1-(clientY-rect.top)/rect.height*2;const forward=normalize(target.map((v,i)=>v-eye[i])),right=normalize(cross(forward,[0,1,0])),up=cross(right,forward),scale=Math.tan(Math.PI/7);const dir=normalize(forward.map((v,i)=>v+right[i]*nx*scale*aspect+up[i]*ny*scale));if(dir[1]>=-.01)return null;const t=-eye[1]/dir[1];return{x:eye[0]+dir[0]*t,z:eye[2]+dir[2]*t};}
  return {render,groundPoint};
}
