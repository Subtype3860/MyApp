const clamp=(value,min,max)=>Math.max(min,Math.min(max,value));
const angle=(from,to,t)=>from+Math.atan2(Math.sin(to-from),Math.cos(to-from))*t;

/** Smooth display positions between server packets; never changes authoritative game state. */
export class TankMotion {
  constructor(){this.clear();}
  clear(){this.latest=null;this.previous=new Map();this.received=0;this.duration=50;}
  push(snapshot,now){
    const displayed=this.sample(now);
    this.previous=new Map((displayed?.players||snapshot.players).map(player=>[player.id,player]));
    this.duration=this.latest?clamp(now-this.received,30,100):50;
    this.latest=snapshot;this.received=now;
  }
  sample(now){
    if(!this.latest)return null;
    const elapsed=Math.max(0,now-this.received),t=clamp(elapsed/this.duration,0,1);
    return {...this.latest,time:this.latest.time+Math.min(elapsed/1000,.1),players:this.latest.players.map(player=>{
      const from=this.previous.get(player.id);
      // Death, spawn and teleports must snap, not slide across the battlefield.
      if(!from||from.hp<=0||player.hp<=0||Math.hypot(player.x-from.x,player.z-from.z)>6)return player;
      return {...player,x:from.x+(player.x-from.x)*t,z:from.z+(player.z-from.z)*t,yaw:angle(from.yaw,player.yaw,t),turret:angle(from.turret,player.turret,t)};
    }),bullets:this.latest.bullets.map(bullet=>({...bullet,x:bullet.x+bullet.vx*Math.min(elapsed/1000,.05),z:bullet.z+bullet.vz*Math.min(elapsed/1000,.05)}))};
  }
}
