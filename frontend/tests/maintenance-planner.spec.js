import { test,expect } from '@playwright/test'
import { nearestMaintenance,maintenancePresets } from '../src/utils/maintenance'
const vehicle=(id,number=Number(id))=>({id,modelName:'SET 150',garageNumber:number,typeName:'Самосвал',engineCount:2})
const due=remaining=>({kind:'scheduled',name:'ТО-700',interval:350,remaining,elapsed:350-remaining,engineNumber:2,startHours:350,currentHours:700-remaining,dueHours:700,readingDate:'2026-10-20'})
const rows=[100,-20,50,300,20,90].map((remaining,index)=>({vehicle:vehicle(String(index+1)),plan:{},revision:1,due:[due(remaining)],incomplete:false}))
test('nearest five use signed remaining and exclude missing data',()=>{
 const list=nearestMaintenance([...rows,{vehicle:vehicle('7'),incomplete:true,due:[due(-100)]}])
 expect(list.map(r=>r.vehicle.id)).toEqual(['2','5','3','6','1'])
 expect(maintenancePresets[1].steps.map(s=>s.interval)).toEqual([350,350,300,350,350,300,350,350,300,350,350,300])
 expect(maintenancePresets[1].steps.reduce((n,s)=>n+s.interval,0)).toBe(4000)
})
async function auth(page){
 await page.addInitScript(()=>{localStorage.setItem('myapp.authToken','test');localStorage.setItem('myapp.userRole','administrator')})
}
for(const width of [390,1440]){
 test('dashboard shows five maintenance timelines at '+width,async({page})=>{
  await page.setViewportSize({width,height:1000});await auth(page)
  await page.route('**/api/**',route=>{
   const path=new URL(route.request().url()).pathname
   return route.fulfill({json:path==='/api/maintenance-plans'?{canManage:true,rows}:path==='/api/vehicles'?rows.map(r=>r.vehicle):path==='/api/profile'?{firstName:'Тест'}:[]})
  })
  await page.goto('/')
  await expect(page.locator('.maintenance-timelines>li')).toHaveCount(5)
  await expect(page.locator('.maintenance-timelines>li').first()).toContainText('№ 2')
  await expect(page.locator('.maintenance-timelines>li').first()).toContainText('Просрочено на 20')
  await expect(page.locator('.dashboard-metric')).toHaveCount(0)
  await expect(page.getByRole('button',{name:/Создать заявку/})).toHaveCount(0)
  await expect(page.getByRole('link',{name:/Парк техники/})).toHaveCount(0)
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true)
  await page.screenshot({path:'test-results/maintenance-home-'+width+'.png',fullPage:true})
 })
 test('configure, register and handle concurrent changes at '+width,async({page})=>{
  await page.setViewportSize({width,height:1000});await auth(page)
  const row={vehicle:vehicle('1',901),plan:null,revision:0,readings:[],due:[],incomplete:false}
  let saved,completed,failSave=false
  await page.route('**/api/**',route=>{
   const path=new URL(route.request().url()).pathname,method=route.request().method()
   if(path==='/api/maintenance-plans/1' && method==='PUT'){
    if(failSave)return route.fulfill({status:409,json:{message:'График изменён другим пользователем.'}})
    saved=route.request().postDataJSON();row.plan=saved.plan;row.revision++
    return route.fulfill({json:{revision:row.revision}})
   }
   if(path.endsWith('/readings')){const date=new URL(route.request().url()).searchParams.get('date');return route.fulfill({json:{engines:date==='2026-10-01'?[{number:1,hours:20000},{number:2,hours:700}]:date==='2026-10-22'?[{number:1,hours:20300},{number:2,hours:1030,isEstimated:true,sourceDate:'2026-10-16',sourceHours:909}]:[{number:1,hours:20020},{number:2,hours:720}],missing:[]}})}
   if(path.endsWith('/complete')){completed=route.request().postDataJSON();row.revision++;return route.fulfill({json:{revision:row.revision}})}
   return route.fulfill({json:path==='/api/maintenance-plans'?{canManage:true,rows:[row]}:path==='/api/profile'?{firstName:'Тест'}:[]})
  })
  await page.goto('/')
  if(width<760)await page.getByRole('button',{name:'Открыть меню',exact:true}).click()
  await page.getByRole('button',{name:'Транспорт',exact:true}).click()
  await page.getByRole('button',{name:'Проведение ТО',exact:true}).click()
  await page.getByLabel('Техника',{exact:true}).selectOption('1')
  await page.getByRole('button',{name:'Действия с графиком ТО',exact:true}).click()
  await page.getByRole('button',{name:'Создать график',exact:true}).click()
  const graphDialog=page.getByRole('dialog',{name:'Создать график',exact:true})
  await graphDialog.getByLabel('Название нового графика').fill('Собственный цикл')
  for(const [index,mark] of [350,700,1000].entries()){
   if(index)await graphDialog.getByRole('button',{name:'Добавить шаг',exact:true}).click()
   await graphDialog.getByLabel('Вид ТО '+(index+1),{exact:true}).fill('ТО-'+mark)
   await graphDialog.getByLabel('Отметка цикла, м/ч',{exact:true}).nth(index).fill(String(mark))
  }
  await graphDialog.getByRole('button',{name:'Использовать график',exact:true}).click()
  await expect(graphDialog).not.toBeVisible()
  await expect(page.getByLabel('Название графика',{exact:true})).toHaveValue('Собственный цикл')
  await expect(page.locator('.maintenance-steps>li')).toHaveCount(3)
  await page.getByLabel('Дата последнего ТО',{exact:true}).fill('2026-10-01')
  await expect(page.locator('.maintenance-date-readings').first()).toContainText('700 м/ч')
  await page.getByRole('button',{name:'Сохранить график',exact:true}).click()
  await expect.poll(()=>saved?.plan.steps.map(step=>step.interval)).toEqual([350,350,300])
  await expect(page.getByRole('button',{name:'Зарегистрировать выполнение ТО'})).toBeVisible()
  // Return the mock to initialization to exercise the ready-made cycle as well.
  row.plan=null;row.revision=0;saved=undefined
  await page.getByRole('button',{name:'Действия с графиком ТО',exact:true}).click()
  await page.getByRole('button',{name:'Обновить',exact:true}).last().click()
  await page.getByRole('button',{name:'Цикл 350–4000',exact:true}).click()
  await page.getByLabel('Выполненный шаг цикла').selectOption('1')
  await page.getByLabel('Дата последнего ТО',{exact:true}).fill('2026-10-01')
  await expect(page.locator('.maintenance-date-readings').first()).toContainText('700 м/ч')
  await expect(page.getByLabel('Интервал, м/ч',{exact:true})).toHaveCount(0)
  await page.getByRole('button',{name:'Добавить обкаточное ТО'}).click()
  await expect(page.locator('.maintenance-runin .maintenance-date-readings')).toContainText('720 м/ч')
  await page.getByRole('button',{name:'Исключить ДВС № 1'}).click()
  await page.getByRole('button',{name:'Сохранить график',exact:true}).click()
  await expect.poll(()=>saved?.plan.nextStepIndex).toBe(2)
  expect(saved.plan.steps[2].interval).toBe(300)
  expect(saved.plan.runIns[0].engines).toEqual([{number:2,hours:720}])
  await page.getByRole('button',{name:'Зарегистрировать выполнение ТО'}).click()
  await page.getByLabel('Дата выполнения',{exact:true}).fill('2026-10-22')
  await expect(page.getByRole('dialog').locator('.maintenance-date-readings')).toContainText(/1.030/)
  await expect(page.getByRole('dialog').locator('.maintenance-date-readings')).toContainText('Расчётные моточасы')
  await page.getByLabel('Описание выполненного ТО').fill('Заменены масла и фильтры')
  await page.getByRole('button',{name:'Сохранить выполнение',exact:true}).click()
  await expect.poll(()=>completed?.engines[1].hours).toBe(1030)
  expect(completed.revision).toBe(1)
  await expect(page.getByRole('dialog')).not.toBeVisible()
  failSave=true
  await page.getByLabel('Название графика').fill('Новый график')
  await page.getByRole('button',{name:'Сохранить график',exact:true}).click()
  await expect(page.getByRole('alert')).toContainText('изменён другим')
  await expect(page.getByLabel('Название графика')).toHaveValue('Новый график')
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true)
 })
}

