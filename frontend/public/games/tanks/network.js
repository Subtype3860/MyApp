/** Native WebSocket transport. Only inputs leave the client; snapshots own game state. */
export class TanksNetwork {
  constructor({onState,onJoined,onClose}) { Object.assign(this,{onState,onJoined,onClose}); this.socket=null; this.connected=false; this.lastState=0; this.generation=0; }
  async connect() {
    this.disconnect(); const generation=++this.generation;
    const token=localStorage.getItem('myapp.authToken');
    if(!token) throw new Error('Войдите в Atlas зарегистрированной учётной записью.');
    const response=await fetch('/api/tanks/ticket',{method:'POST',headers:{Authorization:'Bearer '+token},signal:AbortSignal.timeout(8000)});
    if(generation!==this.generation)return;
    if(response.status===401) throw new Error('Сессия завершена. Войдите в Atlas заново.');
    if(response.status===403) throw new Error('Допуск к игре выдаёт администратор: Настройки → Пользователи → «Танки — доступ к сетевой игре». После выдачи войдите заново.');
    if(!response.ok) throw new Error(response.status===429?'Слишком много подключений. Подождите минуту.':'Игровой сервер недоступен. Повторите подключение.');
    const {ticket}=await response.json();
    if(generation!==this.generation)return;
    if(typeof ticket!=='string')throw new Error('Некорректный ответ игрового сервера.');
    const url=new URL('/api/tanks/socket',location.href);url.protocol=location.protocol==='https:'?'wss:':'ws:';
    const socket=new WebSocket(url);this.socket=socket;
    await new Promise((resolve,reject)=>{
      let joined=false;
      const timeout=setTimeout(()=>{reject(new Error('Сервер не подтвердил вход. Проверьте соединение.'));socket.close();},8000);
      socket.onopen=()=>socket.send(JSON.stringify({ticket}));
      socket.onmessage=event=>{
        if(generation!==this.generation)return;
        try {
          const data=JSON.parse(event.data);
          if(data.type==='joined'){joined=true;this.connected=true;this.lastState=performance.now();clearTimeout(timeout);this.onJoined(data);resolve();}
          if(data.type==='state'&&joined){this.lastState=performance.now();this.onState(data);}
        } catch {socket.close(1008,'invalid_snapshot');}
      };
      socket.onclose=event=>{
        clearTimeout(timeout);if(generation!==this.generation)return;this.connected=false;
        const message=({4401:'Сессия завершена. Войдите в Atlas заново.',4403:'Администратор отозвал допуск или сессия завершена. Для продолжения войдите заново.',4409:'Эта учётная запись уже играет в другой вкладке.',4429:'Арена заполнена: максимум 8 игроков.',1008:'Соединение отклонено сервером.'})[event.code]||'Связь с сервером потеряна. Бой остановлен на этом устройстве.';
        if(!joined)reject(new Error(message));else this.onClose(message);
      };
      socket.onerror=()=>{if(!joined){clearTimeout(timeout);reject(new Error('Не удалось подключиться к сетевой арене.'));}};
    });
  }
  send(input){if(this.connected&&this.socket?.readyState===WebSocket.OPEN)this.socket.send(JSON.stringify(input));}
  disconnect(){this.generation++;this.connected=false;this.socket?.close();this.socket=null;this.lastState=0;}
}
