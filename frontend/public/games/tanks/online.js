import {createWorld, SKILLS} from './world.js';
import {createRenderer} from './render.js';
import {TanksNetwork} from './network.js';
import {TankMotion} from './motion.js';
import {LocalTankPrediction} from './prediction.js';
const $=id=>document.getElementById(id),canvas=$('arena');
let renderer;
try{renderer=createRenderer(canvas);}catch{$('unsupported').hidden=false;$('overlay').hidden=true;}
if(renderer){
let world=createWorld(),mode='lobby',selfId=null,keys=new Set(),mouse=null,firing=false,stick={x:0,z:0},stickId=null,fireId=null,lastFrame=0,toastUntil=0,previousCollected=0,lastSnapshot=null,hudDeadline=0,skillsKey='',boardKey='',nextIdleDraw=0;
const motion=new TankMotion();
const prediction=new LocalTankPrediction();
let sequence=0;
function text(id,value){const el=$(id);if(el.textContent!==String(value))el.textContent=value;}
world.enemies=[];world.pickups=[];
$('wave').parentElement.firstChild.textContent='ИГРОКИ';$('enemies').parentElement.firstChild.textContent='ПОБЕДЫ';$('pause').textContent='↩';$('pause').setAttribute('aria-label','Покинуть арену');
$('menu-eyebrow').textContent='СЕТЕВАЯ АРЕНА / ДО 8 ИГРОКОВ';$('menu-title').textContent='Танки против танков.';$('menu-description').textContent='Общая арена для зарегистрированных пользователей Atlas. Для входа нужен допуск администратора. Собирайте усиления и сражайтесь с другими игроками.';$('start').textContent='Подключиться к арене';$('best').innerHTML='<a href="/?game=tanks" style="color:inherit">Войти в Atlas / вернуться на сайт ↗</a>';
$('briefing').innerHTML='<div><b>01</b><span>Доступ выдаёт администратор</span></div><div><b>02</b><span>Усиления общие для всех игроков</span></div><div><b>03</b><span>После попадания — возрождение через 3 секунды</span></div>';
document.querySelector('.mobile-instructions').textContent='На телефоне: джойстик и огонь с автоприцелом. В сетевой игре паузы нет: выходите через кнопку ↩.';
document.querySelector('.controls-hint span:last-child').innerHTML='<kbd>ESC</kbd> выйти';
const board=document.createElement('aside');board.className='online-board';board.setAttribute('aria-label','Игроки арены');document.body.append(board);
function resetInput(){keys.clear();mouse=null;firing=false;stick={x:0,z:0};stickId=null;fireId=null;$('stick-thumb').style.transform='';}
function toast(message){$('toast').textContent=message;$('toast').classList.add('show');toastUntil=performance.now()+3000;}
function showLobby(message){mode='lobby';resetInput();$('overlay').hidden=false;$('menu-description').textContent=message;$('start').disabled=false;$('start').textContent='Подключиться к арене';$('pause').disabled=true;board.replaceChildren();}
function disconnected(message){net.disconnect();lastSnapshot=null;motion.clear();prediction.clear();skillsKey='';boardKey='';world.enemies=[];world.bullets=[];world.pickups=[];$('wave').textContent='—';$('enemies').textContent='—';$('hp-text').textContent='Нет связи';$('health-fill').style.width='0%';$('skills').replaceChildren();$('mission-title').textContent='Нет соединения с ареной';showLobby(message);}
function updateHud(data){const self=data.players.find(p=>p.id===selfId);if(!self)return;
  text('wave',data.players.length+' / 8');text('enemies',self.kills);text('score',String(self.score).padStart(4,'0'));text('hp-text',Math.ceil(self.hp)+' / 100');text('collected',self.collected);
  const health=Math.ceil(self.hp)+'%';if($('health-fill').style.width!==health)$('health-fill').style.width=health;
  text('mission-title',self.hp<=0?'Возрождение через '+Math.ceil(self.respawnIn)+' сек.':data.players.length<2?'Ожидаем соперников. Собирайте усиления.':'Сетевая арена · Каждый сам за себя');
  text('mission-subtitle',self.protectedSpawn?'Защита после возрождения · 3 секунды или до первого выстрела.':'Подберите контейнер, проехав рядом с ним.');
  const nextSkillsKey=JSON.stringify(Object.entries(self.skills).map(([key,time])=>[key,Math.ceil(time)]));
  if(nextSkillsKey!==skillsKey){skillsKey=nextSkillsKey;
  $('skills').replaceChildren(...Object.entries(self.skills).map(([key,time])=>{const skill=SKILLS[key];const el=document.createElement('div');el.className='skill';el.style.setProperty('--color',skill?.color||'#fff');const text=document.createElement('span');text.textContent=skill?.name||key;const timer=document.createElement('time');timer.textContent=Math.ceil(time)+'с';el.append(text,timer);return el;}));}
  if(self.collected>previousCollected)toast('Усиление подобрано!');previousCollected=self.collected;
  const sorted=[...data.players].sort((a,b)=>b.score-a.score);const nextBoardKey=JSON.stringify(sorted.map(p=>[p.id,p.name,p.score]));
  if(nextBoardKey!==boardKey){boardKey=nextBoardKey;board.replaceChildren(...sorted.map(p=>{const row=document.createElement('div');if(p.id===selfId)row.className='self';const name=document.createElement('span');name.textContent=p.name+(p.id===selfId?' · вы':'');const score=document.createElement('b');score.textContent=p.score;row.append(name,score);return row;}));}
}
const net=new TanksNetwork({onJoined(data){selfId=data.selfId;mode='playing';sequence=0;prediction.clear();previousCollected=0;skillsKey='';boardKey='';hudDeadline=0;$('overlay').hidden=true;$('pause').disabled=false;canvas.focus({preventScroll:true});},onState(data){const now=performance.now();lastSnapshot=data;motion.push(data,now);const player=data.players.find(p=>p.id===selfId);if(player)prediction.accept(player,data.players,now);},onClose:disconnected});
$('start').addEventListener('click',async()=>{$('start').disabled=true;$('start').textContent='Подключение…';try{await net.connect();}catch(error){disconnected(error.message||'Сервер недоступен.');}});
function leave(){net.disconnect();lastSnapshot=null;motion.clear();prediction.clear();showLobby('Вы вышли из боя. Можно подключиться снова.');}
$('pause').addEventListener('click',leave);
function readInput(){
  const x=Math.max(-1,Math.min(1,Number(keys.has('KeyD')||keys.has('ArrowRight'))-Number(keys.has('KeyA')||keys.has('ArrowLeft'))+stick.x));
  const z=Math.max(-1,Math.min(1,Number(keys.has('KeyS')||keys.has('ArrowDown'))-Number(keys.has('KeyW')||keys.has('ArrowUp'))+stick.z));
  const point=mouse?renderer.groundPoint(mouse.x,mouse.y):null;
  const aim=point?{x:Math.max(-99,Math.min(99,point.x)),z:Math.max(-99,Math.min(99,point.z))}:null;
  return {x,z,aim,fire:firing||keys.has('Space')};
}
function submitInput(input){if(!net.connected)return;const seq=++sequence;prediction.record(seq,input,performance.now());net.send({x:input.x,z:input.z,aimX:input.aim?.x??null,aimZ:input.aim?.z??null,fire:input.fire,seq});}
const sendTimer=setInterval(()=>{if(mode!=='playing')return;if(performance.now()-net.lastState>4000){disconnected('Сервер перестал отвечать. Повторите подключение.');return;}submitInput(readInput());},50);
function frame(now){
  const dt=Math.min((now-(lastFrame||now))/1000,.05);lastFrame=now;
  if(mode==='playing'&&lastSnapshot){
    const displayed=motion.sample(now),p=prediction.advance(dt,readInput(),now)||displayed.players.find(p=>p.id===selfId);
    if(p)world={time:displayed.time,player:p,enemies:displayed.players.filter(p=>p.id!==selfId&&p.hp>0).map(p=>({...p,maxHp:100})),bullets:displayed.bullets.map(b=>({...b,enemy:b.owner!==selfId})),pickups:displayed.pickups,particles:[]};
    if(now>=hudDeadline){updateHud(lastSnapshot);hudDeadline=now+100;}
  }
  if(toastUntil&&now>toastUntil){$('toast').classList.remove('show');toastUntil=0;}
  if(mode==='playing'||now>=nextIdleDraw){renderer.render(world,mode==='playing'?dt:0,mode==='playing'&&mouse?renderer.groundPoint(mouse.x,mouse.y):null);nextIdleDraw=now+250;}
  requestAnimationFrame(frame);
}requestAnimationFrame(frame);
document.addEventListener('keydown',e=>{if(mode!=='playing')return;if(['KeyW','KeyA','KeyS','KeyD','ArrowUp','ArrowDown','ArrowLeft','ArrowRight','Space'].includes(e.code)){e.preventDefault();const changed=!keys.has(e.code);keys.add(e.code);if(changed)submitInput(readInput());}if(e.code==='Escape')leave();});document.addEventListener('keyup',e=>{if(keys.delete(e.code)&&mode==='playing')submitInput(readInput());});
canvas.addEventListener('pointermove',e=>{if(e.pointerType!=='touch')mouse={x:e.clientX,y:e.clientY};});canvas.addEventListener('pointerdown',e=>{if(mode==='playing'&&e.button===0&&e.pointerType!=='touch'){mouse={x:e.clientX,y:e.clientY};firing=true;canvas.setPointerCapture(e.pointerId);}});for(const event of ['pointerup','pointercancel','lostpointercapture'])canvas.addEventListener(event,()=>firing=false);canvas.addEventListener('contextmenu',e=>e.preventDefault());
const pad=$('stick');function moveStick(e){const r=pad.getBoundingClientRect();let x=(e.clientX-r.left-r.width/2)/40,z=(e.clientY-r.top-r.height/2)/40;const n=Math.max(1,Math.hypot(x,z));stick={x:x/n,z:z/n};$('stick-thumb').style.transform='translate('+stick.x*29+'px,'+stick.z*29+'px)';}pad.addEventListener('pointerdown',e=>{if(mode!=='playing'||stickId!==null)return;e.preventDefault();stickId=e.pointerId;mouse=null;pad.setPointerCapture(e.pointerId);moveStick(e);});pad.addEventListener('pointermove',e=>{if(stickId===e.pointerId)moveStick(e);});for(const event of ['pointerup','pointercancel','lostpointercapture'])pad.addEventListener(event,e=>{if(stickId===e.pointerId){stickId=null;stick={x:0,z:0};$('stick-thumb').style.transform='';}});
$('fire').addEventListener('pointerdown',e=>{if(mode!=='playing')return;e.preventDefault();fireId=e.pointerId;mouse=null;firing=true;$('fire').setPointerCapture(e.pointerId);});for(const event of ['pointerup','pointercancel','lostpointercapture'])$('fire').addEventListener(event,e=>{if(fireId===e.pointerId){fireId=null;firing=false;}});
window.addEventListener('blur',()=>{resetInput();if(net.connected)submitInput({x:0,z:0,aim:null,fire:false});});document.addEventListener('visibilitychange',()=>{if(document.hidden&&mode==='playing')leave();});window.addEventListener('pagehide',()=>{clearInterval(sendTimer);net.disconnect();});
$('start').disabled=false;
canvas.addEventListener('webglcontextlost',e=>{e.preventDefault();net.disconnect();$('unsupported').hidden=false;$('overlay').hidden=true;});
}
