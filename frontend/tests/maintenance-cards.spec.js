import { test, expect } from '@playwright/test'

for(const width of [390,1440])test('card draft, review and acceptance at '+width,async({page})=>{
 test.setTimeout(60000)
 await page.setViewportSize({width,height:1000})
 await page.addInitScript(()=>{localStorage.setItem('myapp.authToken','test');localStorage.setItem('myapp.userRole','administrator')})
 const vehicle={id:'v1',modelName:'SANY SY1250H',garageNumber:98,typeName:'Экскаватор',engineCount:1}
 const plan={name:'Цикл',steps:[{name:'ТО-1000',interval:250},{name:'ТО-250',interval:250}],nextStepIndex:0,lastDate:'2026-10-01',lastService:'ТО-500',engines:[{number:1,hours:750}],runIns:[]}
 const template={model:vehicle.modelName,service:'ТО-1000',source:'test',reviewNotes:'',operations:[{id:'o1',section:'ДВС',text:'Проверить масло',target:'',control:false},{id:'o2',section:'Контроль',text:'Запуск двигателя',target:'',control:true}],materials:[{id:'m1',assembly:'ДВС',name:'Масло',partNumber:'',unit:'л',quantity:80,note:''}]}
 const templates=[{id:'t1',revision:1,published:true,template}]
 const materialSources=[{id:'equipment1',name:'Экскаваторы',intervals:[{id:'interval1',name:'ТО-1000',items:[{id:'m1',materialName:'Масло из ТО',unit:'л',quantity:90,availableQuantity:0}]}]}]
 let card,revision=1,accepted=0,conflict=false
 const media=[]
 const png=Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l9sAAAAASUVORK5CYII=','base64')
 await page.route('**/api/**',async route=>{
  const path=new URL(route.request().url()).pathname,method=route.request().method()
  if(path==='/api/maintenance-plans/card-template-options')return route.fulfill({json:{canManage:true,canFill:true,canUseLibrary:true,templates}})
  if(path==='/api/maintenance-plans/card-templates')return route.fulfill({json:{canManage:true,canFill:true,templates,materialSources}})
  if(path==='/api/maintenance-plans')return route.fulfill({json:{canManage:true,canUseLibrary:true,rows:[{vehicle,plan,revision,due:[],incomplete:false}]}})
  if(path.endsWith('/history'))return route.fulfill({json:[]})
  if(path.endsWith('/readings'))return route.fulfill({json:{engines:[{number:1,hours:1000}],missing:[]}})
  if(path.endsWith('/v1/cards')&&method==='POST'){
   const body=route.request().postDataJSON();card={id:body.id,vehicleId:'v1',vehicleLabel:'SANY SY1250H №98',templateId:'t1',templateRevision:1,planRevision:1,status:'draft',revision:1,template,data:{date:'2026-10-30',driver:'',engineModel:'',mileage:null,stoppedAt:null,startedAt:'2026-10-30T00:00:00Z',finishedAt:null,releasedAt:null,notes:'',answers:template.operations.map(o=>({id:o.id,status:'',condition:'',executor:'',reason:'',note:'',measurement:null,unit:''})),materials:[{id:'m1',actual:null,substitute:'',executor:''}]},audit:[],defects:{},readings:[]};return route.fulfill({json:card})
  }
  if(path.endsWith('/v1/cards'))return route.fulfill({json:card?[card]:[]})
  if(path.endsWith('/refresh-materials')){card.revision++;card.template={...card.template,materialSource:'Экскаваторы / ТО-1000',materialIntervalId:'interval1',materials:[{...card.template.materials[0],name:'Масло из ТО',quantity:90}]};return route.fulfill({json:card})}
  if(path.endsWith('/photos'))return route.fulfill({json:media})
  if(path.includes('/photos/')&&method==='POST'){
   media.push({id:path.split('/').at(-1),operationId:'o1',contentType:'image/png'});card.revision++
   return route.fulfill({json:{id:media.at(-1).id}})
  }
  if(path.includes('/photos/'))return route.fulfill({body:png,contentType:'image/png'})
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
 await page.getByRole('button',{name:'Библиотека шаблонов',exact:true}).click()
 await page.getByRole('button',{name:'Редактировать SANY SY1250H ТО-1000',exact:true}).click()
 await expect(page.getByLabel('Агрегат / ДВС',{exact:true})).toHaveCount(0)
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true)
 await page.screenshot({path:`test-results/template-editor-${width}.png`,fullPage:true})
 await expect(page.getByLabel('Источник материалов').locator('option')).toHaveText(['Автоматически по модели / типу техники и виду ТО','Экскаваторы / ТО-1000 · 1 материалов'])
 await page.getByRole('button',{name:'Закрыть редактор',exact:true}).click()
 await page.getByRole('button',{name:'ТО по картам',exact:true}).click()
 await page.getByRole('button',{name:'Начать ТО по карте',exact:true}).click()
 const panel=page.locator('.card-editor')
 await expect(panel).toContainText('Заполнено 0 из 2')
 await panel.getByRole('button',{name:'Загрузить материалы из технического обслуживания',exact:true}).click()
 await expect(panel).toContainText('Источник материалов: Экскаваторы / ТО-1000')
 await expect(panel).toContainText('Масло из ТО')
 await expect(panel).toContainText('Норма: 90 л')
 await panel.getByRole('checkbox',{name:'Выполнено: Проверить масло',exact:true}).check()
 await expect.poll(()=>card?.data.answers[0].executor).toBe('Тест')
 await panel.getByRole('checkbox',{name:'Выполнено: Проверить масло',exact:true}).uncheck()
 await expect.poll(()=>card?.data.answers[0].status).toBe('')
 await expect(panel.getByLabel('Исполнитель: Проверить масло',{exact:true})).toHaveValue('Тест')
 await panel.getByLabel('Исполнитель: Проверить масло',{exact:true}).fill('Иванов')
 await expect.poll(()=>card?.data.answers[0].executor).toBe('Иванов')
 await panel.getByLabel('Исполнитель для незаполненных строк').fill('Иванов')
 await panel.getByRole('button',{name:'Назначить исполнителя',exact:true}).click()
 await expect.poll(()=>card?.data.answers[0].executor).toBe('Иванов')
 await panel.getByRole('checkbox',{name:'Выполнено: Проверить масло',exact:true}).check()
 await expect.poll(()=>card.data.answers[0].status).toBe('done')
 await panel.getByRole('checkbox',{name:'Выполнено: Запуск двигателя',exact:true}).check()
 await expect.poll(()=>card.data.answers[1].status).toBe('done')
 await panel.getByLabel('Медиа выполнения: Проверить масло',{exact:true}).setInputFiles({name:'work.png',mimeType:'image/png',buffer:png})
 await panel.getByRole('button',{name:'Фото 1 · Открыть',exact:true}).click()
 await expect(page.getByRole('dialog',{name:'Медиа выполнения'}).getByRole('img')).toBeVisible()
 await page.getByRole('button',{name:'Закрыть медиа',exact:true}).click()
 await expect(panel.getByLabel('Использованный аналог',{exact:true})).toHaveCount(0)
 const material=panel.locator('article').filter({has:page.getByLabel('Факт, л',{exact:true})})
 await material.getByLabel('Исполнитель',{exact:true}).fill('')
 await expect.poll(()=>card.data.materials[0].executor).toBe('')
 await panel.getByLabel('Факт, л',{exact:true}).fill('0')
 await expect.poll(()=>card.data.materials[0].actual).toBe(0)
 await expect.poll(()=>card.data.materials[0].executor).toBe('Тест')
 await material.getByLabel('Исполнитель',{exact:true}).fill('Петров')
 await expect.poll(()=>card.data.materials[0].executor).toBe('Петров')
 await panel.getByLabel('Факт, л',{exact:true}).fill('79')
 await expect.poll(()=>card.data.materials[0].actual).toBe(79)
 await expect(material.getByLabel('Исполнитель',{exact:true})).toHaveValue('Петров')
 await expect(panel.getByRole('button',{name:'Загрузить материалы из технического обслуживания',exact:true})).toBeDisabled()
 const startTime=panel.getByLabel('Начало работ',{exact:true})
 await startTime.focus()
 await startTime.evaluate(input=>{input.value='2026-10-30T12:30';input.dispatchEvent(new Event('input',{bubbles:true}))})
 await page.waitForTimeout(1600)
 await expect(startTime).toBeEnabled()
 await expect(startTime).toHaveValue('2026-10-30T12:30')
 await startTime.blur()
 await expect.poll(()=>card.data.startedAt).toContain('30:00')
 await expect(startTime).toHaveValue('2026-10-30T12:30')
 const finish=panel.getByLabel('Окончание работ',{exact:true})
 await finish.focus()
 // Native date controls can emit input before change, while individual segments are edited.
 await finish.evaluate(input=>{input.value='2026-10-30T14:00';input.dispatchEvent(new Event('input',{bubbles:true}))})
 await page.waitForTimeout(1600)
 await expect(finish).toBeEnabled()
 await expect(finish).toHaveValue('2026-10-30T14:00')
 await finish.blur()
 await expect.poll(()=>card.data.finishedAt).not.toBeNull()
 for(const [label,field,value] of [['Остановка техники','stoppedAt','2026-10-30T11:30'],['Возврат в работу','releasedAt','2026-10-30T15:00']]){
  const input=panel.getByLabel(label,{exact:true})
  await input.focus()
  await input.evaluate((element,value)=>{element.value=value;element.dispatchEvent(new Event('input',{bubbles:true}))},value)
  await page.waitForTimeout(1600)
  await expect(input).toBeEnabled()
  await expect(input).toHaveValue(value)
  await input.blur()
  await expect.poll(()=>card.data[field]).not.toBeNull()
  await expect(input).toHaveValue(value)
 }

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
 await expect(panel.getByRole('checkbox',{name:'Выполнено: Проверить масло',exact:true})).toBeDisabled()
 await expect(panel.getByRole('checkbox',{name:'Выполнено: Проверить масло',exact:true})).toBeChecked()
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true)
 await page.screenshot({path:'test-results/maintenance-card-'+width+'.png',fullPage:true})
})

 test('employee without library access can start from an approved template', async ({ page }) => {
  let libraryRequested = false
  await page.route('**/api/**', route => {
    const path = new URL(route.request().url()).pathname
    if (path.endsWith('/card-templates')) libraryRequested = true
    return route.fulfill({ json: path.endsWith('/card-template-options') ? {
      canManage: false, canFill: true, canUseLibrary: false,
      templates: [{ id: 't1', revision: 1, published: true, template: { model: 'SANY', service: 'ТО-700', source: 'test' } }],
    } : [] })
  })
  await page.goto('/')
  await page.evaluate(async () => {
    const { createApp, h } = await import(performance.getEntriesByType('resource').find(e => e.name.includes('/.vite/deps/vue.js?')).name)
    const { default: Cards } = await import('/src/components/MaintenanceCards.vue')
    document.body.innerHTML = '<div id="cards-test"></div>'
    createApp({ render: () => h(Cards, { token: 'test', vehicle: { id: 'v1', modelName: 'SANY' }, plan: { steps: [{ name: 'ТО-700' }], nextStepIndex: 0, runIns: [] }, planRevision: 1 }) }).mount('#cards-test')
  })
  await expect(page.getByRole('button', { name: 'Начать ТО по карте' })).toBeEnabled()
  await expect(page.getByRole('button', { name: 'Библиотека шаблонов' })).toHaveCount(0)
  expect(libraryRequested).toBe(false)
})
