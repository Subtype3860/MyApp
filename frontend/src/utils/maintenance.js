/** Циклы хранят интервалы между ТО: 700 → 1000 означает 300 м/ч. */
export const maintenancePresets = [
  { name: 'Цикл 250–4000', steps: [250,500,250,1000,250,500,250,2000,250,500,250,3000,250,500,250,4000].map(value => ({ name: 'ТО-' + value, interval: 250 })) },
  { name: 'Цикл 350–4000', steps: [350,700,1000,350,700,2000,350,700,3000,350,700,4000].map((value,index) => ({ name: 'ТО-' + value, interval: index % 3 === 2 ? 300 : 350 })) },
]
export const hoursText = value => Number(value).toLocaleString('ru-RU', { maximumFractionDigits: 1 })
export function nearestMaintenance(rows) {
  return rows.filter(row => !row.incomplete && row.due?.length)
    .map(row => ({ ...row, next: [...row.due].sort((a,b) => a.remaining-b.remaining)[0] }))
    .sort((a,b) => a.next.remaining-b.next.remaining || String(a.vehicle.garageNumber ?? a.vehicle.stateNumber).localeCompare(String(b.vehicle.garageNumber ?? b.vehicle.stateNumber),'ru',{numeric:true}))
    .slice(0,5)
}
export function stepLabel(steps,index) {
  const mark = steps.slice(0,index+1).reduce((sum,step) => sum+Number(step.interval || 0),0)
  return (index+1)+'. '+steps[index].name+' · отметка цикла '+hoursText(mark)+' м/ч'
}

