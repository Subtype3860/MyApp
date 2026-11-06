import { test, expect } from '@playwright/test'

for(const width of [390,1440])test('card draft, review and acceptance at '+width,async({page})=>{
 await page.setViewportSize({width,height:1000})
 await page.addInitScript(()=>{localStorage.setItem('myapp.authToken','test');localStorage.setItem('myapp.userRole','administrator')})
 const vehicle={id:'v1',modelName:'SANY SY1250H',garageNumber:98,typeName:'Экскаватор',engineCount:1}
 const plan={name:'Цикл',steps:[{name:'ТО-1000',interval:250},{name:'ТО-250',interval:250}],nextStepIndex:0,lastDate:'2026-10-01',lastService:'ТО-500',engines:[{number:1,hours:750}],runIns:[]}
 const template={model:vehicle.modelName,service:'ТО-1000',source:'test',reviewNotes:'',operations:[{id:'o1',section:'ДВС',text:'Проверить масло',target:'',control:false},{id:'o2',section:'Контроль',text:'Запуск двигателя',target:'',control:true}],materials:[{id:'m1',assembly:'ДВС',name:'Масло',partNumber:'',unit:'л',quantity:80,note:''}]}
 const templates=[{id:'t1',revision:1,published:true,template}]
 let card,revision=1,accepted=0,conflict=false
 await page.route('**/api/**',async route=>{
  const path=new URL(route.request().url()).pathname,method=route.request().method()
  if(path==='/api/maintenance-plans/card-templates')return route.fulfill({json:{canManage:true,canFill:true,templates}})
  if(path==='/api/maintenance-plans')return route.fulfill({json:{canManage:true,rows:[{vehicle,plan,revision,due:[],incomplete:false}]}})
  if(path.endsWith('/history'))return route.fulfill({json:[]})
  if(path.endsWith('/readings'))return route.fulfill({json:{engines:[{number:1,hours:1000}],missing:[]}})
  if(path.endsWith('/v1/cards')&&method==='POST'){
   const body=route.request().postDataJSON();card={id:body.id,vehicleId:'v1',vehicleLabel:'SANY SY1250H №98',templateId:'t1',templateRevision:1,planRevision:1,status:'draft',revision:1,template,data:{date:'2026-10-30',driver:'',engineModel:'',mileage:null,stoppedAt:null,startedAt:'2026-10-30T00:00:00Z',finishedAt:null,releasedAt:null,notes:'',answers:template.operations.map(o=>({id:o.id,status:'',condition:'',executor:'',reason:'',note:'',measurement:null,unit:''})),materials:[{id:'m1',actual:null,substitute:'',executor:''}]},audit:[],defects:{},readings:[]};return route.fulfill({json:card})
  }
  if(path.endsWith('/v1/cards'))return route.fulfill({json:card?[card]:[]})
  if(path.endsWith('/photos'))return route.fulfill({json:[]})
  if(card&&path.endsWith('/cards/'+card.id)&&method==='PUT'){
   if(conflict)return route.fulfill({status:409,json:{message:'Карта изменена другим сотрудником.'}})
   const body=route.request().postDataJSON();card.data=body.data;card.revision++;if(body.action==='submit')card.status='submitted';return route.fulfill({json:card})
  }
  if(path.endsWith('/accept')){accepted++;card.status='accepted';card.acceptedAt='2026-10-30T03:00:00Z';card.acceptedBy='mechanic';card.readings=[{number:1,hours:1000}];card.revision++;plan.nextStepIndex=1;revision++;return route.fulfill({json:{revision}})}
  return route.fulfill({json:path==='/api/profile'?{firstName:'Тест'}:[]})
 })
 await page.goto('/')
 if(width<760)await page.getByRole('button',{name:'Открыть меню',exact:true}).click()
 await page.getByRole('button',{name:'Транспорт',exact:true}).click()
 await page.getByRole('button',{name:'Проведение ТО',exact:true}).click()
 await page.getByLabel('Техника',{exact:true}).selectOption('v1')
 await page.getByRole('button',{name:'Начать ТО по карте',exact:true}).click()
 const panel=page.locator('.card-editor')
 await expect(panel).toContainText('Заполнено 0 из 2')
 await panel.getByLabel('Исполнитель для незаполненных строк').fill('Иванов')
 await panel.getByRole('button',{name:'Назначить исполнителя',exact:true}).click()
 await expect.poll(()=>card?.data.answers[0].executor).toBe('Иванов')
 await panel.getByRole('combobox',{name:'Выполнение',exact:true}).nth(0).selectOption('done')
 await expect.poll(()=>card.data.answers[0].status).toBe('done')
 await panel.getByRole('combobox',{name:'Выполнение',exact:true}).nth(1).selectOption('done')
 await expect.poll(()=>card.data.answers[1].status).toBe('done')
 await panel.getByLabel('Факт, л',{exact:true}).fill('79')
 await expect.poll(()=>card.data.materials[0].actual).toBe(79)
 await panel.getByLabel('Окончание работ',{exact:true}).fill('2026-10-30T14:00')
 await expect.poll(()=>card.data.finishedAt).not.toBeNull()
 // Failed saves keep a recoverable local draft and never advance the plan.
 conflict=true
 await panel.getByLabel('Общие замечания').fill('Проверить после смены')
 await expect(page.getByRole('alert')).toContainText('Карта изменена')
 expect(accepted).toBe(0)
 expect(await page.evaluate(()=>Object.keys(localStorage).some(k=>k.startsWith('atlas.to-card.')))).toBe(true)
 conflict=false
 await panel.getByRole('button',{name:'Сохранить карту',exact:true}).click()
 await expect(page.getByRole('status')).toContainText('Сохранено на сервере')
 await panel.getByRole('button',{name:'Передать на проверку',exact:true}).click()
 await expect(panel).toContainText('На проверке')
 await panel.getByRole('button',{name:'Принять карту и завершить ТО',exact:true}).click()
 await expect(panel).toContainText('Карта принята')
 await expect(panel.locator('.maintenance-date-readings')).toContainText(/1.000 м\/ч/)
 expect(accepted).toBe(1);expect(plan.nextStepIndex).toBe(1)
 await expect(panel.getByLabel('Общие замечания')).toBeDisabled()
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true)
 await page.screenshot({path:'test-results/maintenance-card-'+width+'.png',fullPage:true})
})
