import { collides } from './world.js';

function move(position,input,dt,peers,speed){
  const length=Math.max(1,Math.hypot(input.x,input.z)),dx=input.x/length*speed*dt,dz=input.z/length*speed*dt;
  const blocked=(x,z)=>collides(x,z)||peers.some(peer=>peer.hp>0&&Math.hypot(peer.x-x,peer.z-z)<1.7);
  if(!blocked(position.x+dx,position.z))position.x+=dx;
  if(!blocked(position.x,position.z+dz))position.z+=dz;
  if(Math.abs(input.x)+Math.abs(input.z)>.01)position.yaw=Math.atan2(input.x,input.z);
}

/** Local presentation only: replay unconfirmed inputs, then reconcile to the server. */
export class LocalTankPrediction {
  constructor(){this.clear();}
  clear(){this.authoritative=null;this.position=null;this.pending=[];this.received=0;this.error={x:0,z:0};this.peers=[];}
  record(seq,input,now){
    this.pending.push({seq,input:{x:input.x,z:input.z},time:now});
    this.pending=this.pending.filter(command=>now-command.time<=500).slice(-20);
  }
  accept(player,players,now){
    const previous=this.authoritative;
    this.peers=players.filter(peer=>peer.id!==player.id&&peer.hp>0);
    this.received=now;this.authoritative=player;
    // Backward compatibility: do not guess input acknowledgement for an older server.
    if(!Number.isSafeInteger(player.ackSeq)){this.position=null;return;}
    const reset=!previous||previous.hp<=0||player.hp<=0||previous.spawnId!==player.spawnId;
    if(reset){this.pending=[];this.position={x:player.x,z:player.z,yaw:player.yaw,turret:player.turret};this.error={x:0,z:0};return;}
    this.pending=this.pending.filter(command=>command.seq>player.ackSeq&&now-command.time<=300);
    const target={x:player.x,z:player.z,yaw:player.yaw,turret:player.turret};
    const speed=player.skills.speed>0?10.5:6;
    for(let i=0;i<this.pending.length;i++){
      const command=this.pending[i],end=this.pending[i+1]?.time??now;
      let duration=Math.max(0,Math.min(end-command.time,100))/1000;
      while(duration>0){const step=Math.min(duration,.05);move(target,command.input,step,this.peers,speed);duration-=step;}
    }
    if(!this.position||Math.hypot(target.x-this.position.x,target.z-this.position.z)>2){this.position=target;this.error={x:0,z:0};}
    else {this.error={x:target.x-this.position.x,z:target.z-this.position.z};this.position.turret=player.turret;}
  }
  advance(dt,input,now){
    const player=this.authoritative;
    if(!player||!this.position||!Number.isSafeInteger(player.ackSeq))return null;
    dt=Math.max(0,Math.min(dt,.05));
    // A frozen connection must never let the local tank drive away indefinitely.
    if(player.hp>0&&now-this.received<=300){
      move(this.position,input,dt,this.peers,player.skills.speed>0?10.5:6);
      const blend=1-Math.exp(-dt*12),dx=this.error.x*blend,dz=this.error.z*blend;
      if(!collides(this.position.x+dx,this.position.z+dz)&&!this.peers.some(peer=>Math.hypot(peer.x-this.position.x-dx,peer.z-this.position.z-dz)<1.7)){this.position.x+=dx;this.position.z+=dz;}
      this.error.x-=dx;this.error.z-=dz;
      if(input.aim)this.position.turret=Math.atan2(input.aim.x-this.position.x,input.aim.z-this.position.z);
    }
    return {...player,...this.position};
  }
}
