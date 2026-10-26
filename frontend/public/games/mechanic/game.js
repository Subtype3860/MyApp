const KEY = 'smena.mechanic.v1';
const systems = ['Электрика', 'Охлаждение', 'Тормоза', 'Топливная система'];
const faults = [
  { system: 0, name: 'Изношенный аккумулятор', part: 'Аккумулятор 12 В', cost: 180, symptom: 'После ночной стоянки стартер едва крутит. Водитель просит проверить машину перед выездом.', evidence: 'Напряжение под нагрузкой падает. Аккумулятор не держит заряд.' },
  { system: 1, name: 'Негерметичный патрубок', part: 'Патрубок и антифриз', cost: 120, symptom: 'Температура двигателя растёт на подъёмах. Под машиной появились следы жидкости.', evidence: 'При опрессовке обнаружена течь на патрубке радиатора.' },
  { system: 2, name: 'Износ тормозных колодок', part: 'Комплект колодок', cost: 160, symptom: 'При торможении слышен металлический скрежет. Педаль стала менее отзывчивой.', evidence: 'Толщина накладок ниже допустимого значения. Диски в порядке.' },
  { system: 3, name: 'Засорённый топливный фильтр', part: 'Топливный фильтр', cost: 90, symptom: 'Двигатель теряет тягу под нагрузкой и работает с перебоями. Проблема появилась после заправки.', evidence: 'Давление подачи низкое. На фильтрующем элементе обнаружены загрязнения.' },
  { system: 0, name: 'Неисправный генератор', part: 'Регулятор напряжения', cost: 140, symptom: 'На панели горит индикатор зарядки. При включении фар заметно падает яркость.', evidence: 'Генератор выдаёт нестабильное напряжение. Неисправен регулятор.' },
  { system: 1, name: 'Заклинивший термостат', part: 'Термостат', cost: 110, symptom: 'Двигатель быстро перегревается, хотя уровень охлаждающей жидкости нормальный.', evidence: 'Термостат не открывает большой круг циркуляции.' },
];
const names = ['Вездеход «Север»', 'Сервисный внедорожник', 'Патрульный вездеход', 'Экспедиционный пикап', 'Дежурная машина', 'Лесной вездеход'];
const $ = id => document.getElementById(id);
const fresh = () => ({version:1,money:600,reputation:50,completed:0,upgraded:false,job:null,logs:['Мастерская открыта. Первый клиент уже ждёт.'],goalPaid:false});
let state = fresh(), saveError = false;
try {
  const saved = JSON.parse(localStorage.getItem(KEY));
  if(saved && saved.version===1 && Number.isFinite(saved.money) && saved.money>=0 && Number.isInteger(saved.completed) && saved.completed>=0 && Number.isFinite(saved.reputation) && Array.isArray(saved.logs) && saved.logs.every(x=>typeof x==='string') && typeof saved.upgraded==='boolean' && typeof saved.goalPaid==='boolean') {
    const j=saved.job;
    if(!j || (Number.isInteger(j.fault) && faults[j.fault] && ['diagnose','parts','repair','ready'].includes(j.stage) && Array.isArray(j.checked) && j.checked.every(n=>Number.isInteger(n)&&n>=0&&n<4) && Number.isFinite(j.time) && j.time>=0 && Number.isInteger(j.hits) && j.hits>=0 && j.hits<=3 && Number.isInteger(j.misses) && j.misses>=0)) state=saved;
  }
} catch { saveError=true; }
let muted=true,audioContext,frame=0,started=0,position=0,noticeTimer;
function save(){try{localStorage.setItem(KEY,JSON.stringify(state));saveError=false;}catch{saveError=true;} $('save-status').textContent=saveError?'Сохранение недоступно. Не закрывайте вкладку.':'Прогресс сохраняется в этом браузере · Игровая диагностика условная';}
function log(message){state.logs.unshift(message);state.logs=state.logs.slice(0,5);}
function announce(message){$('notice').textContent=message;$('notice').classList.add('show');clearTimeout(noticeTimer);noticeTimer=setTimeout(()=>$('notice').classList.remove('show'),3500);}
function beep(good=true){if(muted)return;try{audioContext??=new(window.AudioContext||window.webkitAudioContext)();audioContext.resume();const o=audioContext.createOscillator(),g=audioContext.createGain();o.connect(g);g.connect(audioContext.destination);o.frequency.value=good?620:180;g.gain.setValueAtTime(.05,audioContext.currentTime);g.gain.exponentialRampToValueAtTime(.001,audioContext.currentTime+.15);o.start();o.stop(audioContext.currentTime+.16);}catch{}}
function action(label,fn,kind='primary'){const button=document.createElement('button');button.className=kind+' wide';button.textContent=label;button.addEventListener('click',fn);return button;}
function add(html){$('order-content').insertAdjacentHTML('beforeend',html);}
const currency=n=>n.toLocaleString('ru-RU')+' ₽';
function startJob(){if(state.job)return;state.job={fault:state.completed%faults.length,stage:'diagnose',checked:[],time:0,hits:0,misses:0};log('Принят заказ №'+(state.completed+1)+'. Клиент рассчитывает на вас.');save();render();}
function diagnose(system){const j=state.job;if(!j||j.stage!=='diagnose'||j.checked.includes(system))return;j.checked.push(system);j.time+=15;if(system===faults[j.fault].system){j.stage='parts';log('Найдено: '+faults[j.fault].name+'.');beep();}else{log(systems[system]+': отклонений не обнаружено.');announce('Система исправна. Сопоставьте симптомы с другой системой.');}save();render();}
function buy(){const j=state.job;if(!j||j.stage!=='parts')return;const f=faults[j.fault];if(state.money<f.cost)return;state.money-=f.cost;j.time+=10;j.stage='repair';log('Приобретено: '+f.part+' за '+currency(f.cost)+'.');save();render();}
function zone(){return state.upgraded?[31,69]:[39,61];}
function hit(){const j=state.job;if(!j||j.stage!=='repair')return;const [lo,hi]=zone();if(position>=lo&&position<=hi){j.hits++;j.time+=10;beep();announce('Точно! '+j.hits+' / 3');}else{j.misses++;j.time+=10;beep(false);announce('Мимо зоны. Потрачено ещё 10 минут, попробуйте снова.');}if(j.hits===3){j.stage='ready';log('Ремонт завершён. Контрольная проверка пройдена.');}save();render();}
function quote(){const j=state.job;return 430+faults[j.fault].cost+Math.min(state.completed,10)*25;}
function bonus(){return state.job.time<=90?120:0;}
function finish(){const j=state.job;if(!j||j.stage!=='ready')return;const earned=quote()+bonus();state.money+=earned;state.reputation=Math.min(100,state.reputation+(bonus()?5:2)+(j.misses===0?3:0));state.completed++;state.job=null;log('Заказ выдан. Получено '+currency(earned)+'.');if(state.completed>=5&&!state.goalPaid){state.goalPaid=true;state.money+=1000;log('Цель смены выполнена! Премия '+currency(1000)+'.');announce('Пять машин снова в строю! Премия 1 000 ₽. Можно продолжать.');}save();render();beep();}
function upgrade(){if(state.upgraded||state.money<700||state.job)return;state.money-=700;state.upgraded=true;log('Куплен точный инструмент. Зона успешного ремонта стала шире.');save();render();}
function tick(now){if(state.job?.stage!=='repair')return;if(!started)started=now;position=(1-Math.cos((now-started)/850))/2*100;const needle=$('needle');if(needle)needle.style.left=position+'%';frame=requestAnimationFrame(tick);}
function render(){cancelAnimationFrame(frame);started=0;position=0;
  $('money').textContent=currency(state.money);$('reputation').textContent=Math.round(state.reputation)+' / 100';$('completed').textContent=state.completed;
  $('upgrade').textContent=state.upgraded?'✓ Точный инструмент установлен':'↑ Улучшить инструмент · 700 ₽';$('upgrade').disabled=state.upgraded||state.money<700||!!state.job;
  $('upgrade').title=state.job?'Улучшения доступны между заказами':'Расширяет зелёную зону ремонта';
  $('goal-dots').replaceChildren(...Array.from({length:5},(_,i)=>{const dot=document.createElement('span');if(i<state.completed)dot.className='done';return dot;}));
  $('goal-title').textContent=state.goalPaid?'Смена удалась. Отличная работа!':'Пять машин. Одна репутация.';
  $('goal-text').textContent=state.goalPaid?'Премия 1 000 ₽ уже в кассе. Продолжайте принимать заказы и развивать мастерскую.':'Выполните 5 заказов и получите премию 1 000 ₽. Сдавайте за 90 игровых минут для бонуса.';
  $('journal').replaceChildren(...state.logs.map(message=>{const li=document.createElement('li');li.textContent=message;return li;}));
  $('order-content').replaceChildren();const j=state.job;
  $('vehicle-art').style.opacity=j?'1':'.4';$('fault-marker').style.display=j&&j.stage==='diagnose'?'':'none';
  $('vehicle-name').textContent=j?names[j.fault]:'Бокс свободен';$('plate').textContent=j?'М '+String(101+state.completed).padStart(3,'0')+' СМ':'— — —';$('job-id').textContent=j?'№ '+String(state.completed+1).padStart(3,'0'):'ОЖИДАНИЕ';
  $('bay-status').textContent=j?'В РАБОТЕ':'СВОБОДЕН';$('progress-bar').style.width=j?({diagnose:25,parts:50,repair:65,ready:100}[j.stage])+'%':'0%';
  if(!j){add('<div class="welcome-symbol" aria-hidden="true">⚒</div><h2>Работа найдётся.</h2><p class="description">Клиент оставит машину и расскажет о симптомах. Выберите систему для проверки, найдите неисправность и выполните ремонт.</p><p class="hint">Диагностика — 15 минут. Покупка детали — 10 минут. Каждая попытка ремонта — 10 минут. Время идёт только от ваших действий.</p>');$('order-content').append(action('Принять заказ →',startJob));return;}
  const f=faults[j.fault];add('<span class="next-step">'+({diagnose:'Шаг 1 / Найдите причину',parts:'Шаг 2 / Подготовьте запчасть',repair:'Шаг 3 / Выполните ремонт',ready:'Готово к выдаче'}[j.stage])+'</span><h2>'+({diagnose:'Что вышло из строя?',parts:'Причина найдена.',repair:'Точность решает.',ready:'Можно возвращать ключи.'}[j.stage])+'</h2><p class="description">'+f.symptom+'</p><div class="quote"><span>ОПЛАТА РАБОТЫ И ДЕТАЛИ<strong>'+currency(quote())+'</strong></span><span>БОНУС ЗА СРОК<strong>+120 ₽</strong></span></div>');
  if(j.stage==='diagnose'){const list=document.createElement('div');list.className='diagnostics';systems.forEach((name,i)=>{const b=action(name,()=>diagnose(i),'diagnostic');b.classList.toggle('checked',j.checked.includes(i));b.disabled=j.checked.includes(i);const small=document.createElement('small');small.textContent=b.disabled?'✓ Исправно':'Проверить · 15 мин';b.append(small);list.append(b);});$('order-content').append(list);}
  if(j.stage==='parts'){add('<div class="finding"><strong>'+f.name+'</strong><br>'+f.evidence+'</div>');$('order-content').append(action('Купить: '+f.part+' · '+currency(f.cost),buy));}
  if(j.stage==='repair'){const [lo,hi]=zone();add('<p class="hint">Остановите указатель в зелёной зоне 3 раза. Нажмите кнопку или пробел, когда указатель внутри зоны. Спешить не нужно.</p><div class="meter" aria-hidden="true"><span class="target" style="left:'+lo+'%;width:'+(hi-lo)+'%"></span><span class="needle" id="needle"></span></div><div class="repair-hits" aria-label="Точных действий: '+j.hits+' из 3">'+Array.from({length:3},(_,i)=>'<span class="'+(i<j.hits?'done':'')+'"></span>').join('')+'</div>');const b=action('Зафиксировать · Пробел',hit);b.id='repair-button';$('order-content').append(b);frame=requestAnimationFrame(tick);}
  if(j.stage==='ready'){add('<div class="result">'+f.name+' — устранено<strong>'+currency(quote()+bonus())+'</strong><span class="hint">'+(bonus()?'Включая 120 ₽ за сдачу в срок.':'Без премии за срок. Основная оплата сохранена.')+'</span></div>');$('order-content').append(action('Выдать машину и получить оплату →',finish));}
  add('<div class="time"><span>Затрачено: '+j.time+' мин</span><span>'+(j.time<=90?'До премии: '+(90-j.time)+' мин':'Срок премии истёк')+'</span></div>');
}
$('upgrade').addEventListener('click',upgrade);
$('sound').addEventListener('click',()=>{muted=!muted;$('sound').textContent='Звук: '+(muted?'выкл.':'вкл.');$('sound').setAttribute('aria-pressed',String(!muted));beep();});
$('reset').addEventListener('click',()=>$('reset-dialog').showModal());
$('cancel-reset').addEventListener('click',()=>$('reset-dialog').close());
$('confirm-reset').addEventListener('click',()=>{$('reset-dialog').close();state=fresh();save();render();announce('Началась новая смена.');});
document.addEventListener('keydown',event=>{if(event.code==='Space'&&!event.repeat&&state.job?.stage==='repair'&&!$('reset-dialog').open&&(event.target===document.body||event.target.id==='repair-button')){event.preventDefault();hit();}});
// Cross-tab play would otherwise overwrite an active order with an older save.
window.addEventListener('storage',event=>{if(event.key===KEY){cancelAnimationFrame(frame);location.reload();}});
save();render();
