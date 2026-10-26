import { createWorld, stepWorld, SKILLS } from './world.js';
import { createRenderer } from './render.js';
const $=id=>document.getElementById(id),canvas=$('arena');
let renderer;
try{renderer=createRenderer(canvas);}catch{$('unsupported').hidden=false;$('overlay').hidden=true;}
if(renderer){
let world=createWorld(),mode='intro',lastTime=0,best=0,keys=new Set(),mouse=null,firing=false,stick={x:0,z:0},stickPointer=null,firePointer=null,toastTimer=0,hudTimer=0,skillKey='',raf;
try{best=Math.max(0,Number(localStorage.getItem('bronya.best.v1'))||0);}catch{}
$('best').textContent='Рекорд: '+best.toLocaleString('ru-RU');
function resetInput(){keys.clear();firing=false;stick={x:0,z:0};stickPointer=null;firePointer=null;$('stick-thumb').style.transform='';}
function toast(text){$('toast').textContent=text;$('toast').classList.add('show');toastTimer=3;}
function record(){if(world.score>best){best=world.score;try{localStorage.setItem('bronya.best.v1',String(best));}catch{}}$('best').textContent='Рекорд: '+best.toLocaleString('ru-RU');}
function showMenu(title,description,label){$('menu-title').textContent=title;$('menu-description').textContent=description;$('start').replaceChildren(document.createTextNode(label));$('briefing').hidden=true;$('restart').hidden=mode!=='paused';$('overlay').hidden=false;$('pause').disabled=true;resetInput();}
function start(){if(mode==='paused'){mode='playing';}else{world=createWorld();mode='playing';skillKey='';toast('Зелёный контейнер справа — первое усиление.');}resetInput();$('overlay').hidden=true;$('pause').disabled=false;canvas.focus({preventScroll:true});updateHud();}
function pause(){if(mode!=='playing')return;mode='paused';showMenu('Передышка.','Бой остановлен. Продолжите, когда будете готовы.','Продолжить бой');}
function updateHud(){const p=world.player;$('wave').innerHTML=String(Math.max(1,world.wave)).padStart(2,'0')+' <span>/ 05</span>';$('enemies').textContent=world.enemies.length;$('score').textContent=String(world.score).padStart(4,'0');$('hp-text').textContent=Math.ceil(p.hp)+' / 100';$('health-fill').style.width=p.hp+'%';$('health-fill').style.background=p.hp<30?'#ed8895':'#91e0bc';$('collected').textContent=world.collected;
  const active=Object.entries(p.skills).map(([key,time])=>[key,Math.ceil(time)]);const nextKey=JSON.stringify(active);if(nextKey!==skillKey){skillKey=nextKey;$('skills').replaceChildren(...active.map(([key,time])=>{const s=SKILLS[key],el=document.createElement('div');el.className='skill';el.style.setProperty('--color',s.color);const icon=document.createElement('b');icon.textContent=s.icon;const label=document.createElement('span');label.textContent=s.name;const timer=document.createElement('time');timer.textContent=time+'с';el.append(icon,label,timer);return el;}));}
  $('mission-title').textContent=world.enemies.length?'Маневрируйте. Не стойте под огнём.':'Собирайте усиления перед следующей волной.';
}
function frame(now){const dt=Math.min((now-(lastTime||now))/1000,.05);lastTime=now;let aim=mouse?renderer.groundPoint(mouse.x,mouse.y):null;
 if(mode==='playing'){const x=Number(keys.has('KeyD')||keys.has('ArrowRight'))-Number(keys.has('KeyA')||keys.has('ArrowLeft'))+stick.x;const z=Number(keys.has('KeyS')||keys.has('ArrowDown'))-Number(keys.has('KeyW')||keys.has('ArrowUp'))+stick.z;stepWorld(world,dt,{x,z,aim,fire:firing||keys.has('Space')});for(const event of world.events){if(event.kind==='pickup'||event.kind==='wave')toast(event.text);}world.events.length=0;hudTimer-=dt;if(hudTimer<=0){updateHud();hudTimer=.1;}if(world.status!=='playing'){mode='ended';record();showMenu(world.status==='won'?'Сектор за вами.':'Броня не выдержала.',world.status==='won'?'Пять волн пройдены! Собрано усилений: '+world.collected+'. Очки: '+world.score+'.':'Уничтожено танков: '+world.kills+'. Очки: '+world.score+'. Собирайте щиты и ремонт, чтобы продержаться дольше.','Новый бой');updateHud();}}
 if(toastTimer>0){toastTimer-=dt;if(toastTimer<=0)$('toast').classList.remove('show');}
 renderer.render(world,mode==='playing'?dt:0,mode==='playing'?aim:null);raf=requestAnimationFrame(frame);
}
$('start').addEventListener('click',start);$('pause').addEventListener('click',pause);$('restart').addEventListener('click',()=>{mode='intro';start();});
document.addEventListener('keydown',e=>{if(['KeyW','KeyA','KeyS','KeyD','ArrowUp','ArrowDown','ArrowLeft','ArrowRight','Space'].includes(e.code)&&mode==='playing'){e.preventDefault();keys.add(e.code);}if((e.code==='KeyP'||e.code==='Escape')&&!e.repeat){if(mode==='playing')pause();else if(mode==='paused')start();}});
document.addEventListener('keyup',e=>keys.delete(e.code));
canvas.addEventListener('pointermove',e=>{if(e.pointerType!=='touch')mouse={x:e.clientX,y:e.clientY};});
canvas.addEventListener('pointerdown',e=>{if(mode==='playing'&&e.button===0&&e.pointerType!=='touch'){mouse={x:e.clientX,y:e.clientY};firing=true;canvas.setPointerCapture(e.pointerId);}});
canvas.addEventListener('pointerup',()=>firing=false);canvas.addEventListener('pointercancel',()=>firing=false);canvas.addEventListener('pointerleave',()=>{mouse=null;if(!canvas.hasPointerCapture?.(1))firing=false;});canvas.addEventListener('contextmenu',e=>e.preventDefault());
const pad=$('stick');function updateStick(e){const r=pad.getBoundingClientRect();let x=(e.clientX-r.left-r.width/2)/40,z=(e.clientY-r.top-r.height/2)/40;const l=Math.hypot(x,z);if(l>1){x/=l;z/=l;}stick={x,z};$('stick-thumb').style.transform='translate('+x*29+'px,'+z*29+'px)';}
pad.addEventListener('pointerdown',e=>{if(mode!=='playing'||stickPointer!==null)return;e.preventDefault();stickPointer=e.pointerId;mouse=null;pad.setPointerCapture(e.pointerId);updateStick(e);});pad.addEventListener('pointermove',e=>{if(e.pointerId===stickPointer)updateStick(e);});function releaseStick(e){if(e.pointerId===stickPointer){stickPointer=null;stick={x:0,z:0};$('stick-thumb').style.transform='';}}pad.addEventListener('pointerup',releaseStick);pad.addEventListener('pointercancel',releaseStick);pad.addEventListener('lostpointercapture',releaseStick);
$('fire').addEventListener('pointerdown',e=>{if(mode!=='playing')return;e.preventDefault();firePointer=e.pointerId;mouse=null;firing=true;$('fire').setPointerCapture(e.pointerId);});function releaseFire(e){if(e.pointerId===firePointer){firePointer=null;firing=false;}}for(const type of ['pointerup','pointercancel','lostpointercapture'])$('fire').addEventListener(type,releaseFire);
window.addEventListener('blur',()=>{resetInput();pause();});document.addEventListener('visibilitychange',()=>{if(document.hidden){resetInput();pause();}});
canvas.addEventListener('webglcontextlost',e=>{e.preventDefault();cancelAnimationFrame(raf);resetInput();$('unsupported').hidden=false;$('overlay').hidden=true;});
raf=requestAnimationFrame(frame);updateHud();$('start').disabled=false;$('start').textContent='В бой ↗';
}
