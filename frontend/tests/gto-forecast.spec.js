import { test, expect } from '@playwright/test'
import { nextForecastSelection, buildForecastRows, forecastGroups, forecastNoteBlocks, forecastDocumentRow, forecastPdfNoteBlocks, responsibleShortName, engineHoursText } from '../src/utils/gtoForecast.js'

test('next shift follows the date rule and forecast needs mechanic choice', () => {
  expect(nextForecastSelection(new Date('2026-10-11T09:00:00'))).toEqual({ date:'2026-10-11', shift:'1' })
  expect(nextForecastSelection(new Date('2026-10-11T13:00:00'))).toEqual({ date:'2026-10-11', shift:'2' })
  const [row]=buildForecastRows([{vehicle:{id:'v1',operationalState:'stopped'},defects:[{id:'d1',symptoms:'Утечка'},{id:'d2',symptoms:'Исправлено'}],works:[{defectId:'d2',status:'repaired',completedAt:'2026-10-10T10:00:00Z'}],hours:[{engineHours:0,readingDate:'2026-10-10'}]}])
  expect(row.currentNotes).toBe('Утечка');expect(row.hours).toBe(0);expect(row.forecast).toBe('faulty')
})
for (const width of [390,1440]) {
 test(`forecast choices, draft and PDF at ${width}px`, async ({page}) => {
  await page.setViewportSize({width,height:1000})
  await page.addInitScript(()=>{localStorage.setItem('myapp.authToken',`test.${btoa(JSON.stringify({sub:'mechanic',role:'administrator'}))}.test`);localStorage.setItem('myapp.userRole','Administrator')})
  const journals=[{vehicle:{id:'v1',typeName:'Экскаватор',modelName:'SANY SY1250H',garageNumber:94,operationalState:'stopped'},defects:[{id:'d1',symptoms:'Утечка масла',operationalState:'stopped'}],partsRequests:[{id:'p1',defectId:'d1',requestNumber:'ЗЗП-123',description:'Закупка уплотнения',requestDate:'2026-10-11'}],works:[],hours:[{engineHours:20511,readingDate:'2026-10-10'}]},
   {vehicle:{id:'v2',typeName:'Самосвал',modelName:'SANY SET150',garageNumber:901,operationalState:'healthy'},defects:[],works:[],hours:[]}]
  const writes=[]
  await page.route('**/api/**',route=>{
   const request=route.request(),path=new URL(request.url()).pathname
   if(request.method()!=='GET')writes.push(path)
   if(path==='/api/vehicles/repair-journal')return route.fulfill({json:journals})
   if(path==='/api/vehicles')return route.fulfill({json:journals.map(row=>row.vehicle)})
   if(path==='/api/vehicles/fleet-options')return route.fulfill({json:{canManage:true}})
   if(path==='/api/gto-settings')return route.fulfill({json:{organisation:'',site:'',vehicles:[],plans:[]}})
   if(path==='/api/profile')return route.fulfill({json:{lastName:'Иванов',firstName:'Иван',position:'Механик'}})
   return route.fulfill({json:[]})
  })
  await page.goto('/')
  if(width<761)await page.getByRole('button',{name:'Открыть меню',exact:true}).click()
  await page.getByRole('button',{name:'Выход ГТО',exact:true}).click()
  await expect(page.locator('.gto-row')).toHaveCount(1)
  await page.getByLabel('Меню Выход ГТО',{exact:true}).click()
  await expect(page.getByRole('button',{name:'Скачать PDF',exact:true})).toBeEnabled()
  await page.keyboard.press('Escape')
  await page.getByLabel('Дата прогноза',{exact:true}).fill('2026-10-12')
  await page.getByLabel('Смена',{exact:true}).selectOption('2')
  const excavator=page.locator('.gto-row').filter({hasText:'№ 94'}),truck=page.locator('.gto-row').filter({hasText:'№ 901'})
  await excavator.getByLabel('Состояние к смене',{exact:true}).selectOption('healthy')
  await excavator.getByLabel('Примечание механика',{exact:true}).fill('Замена уплотнения')
  await expect(page.getByText('Исправен к смене: 2 из 2 · Неисправен: 0')).toHaveCount(0)
  await expect(excavator).toContainText('20511')
  await page.getByLabel('Поиск техники',{exact:true}).fill('94')
  await expect(page.locator('.gto-row')).toHaveCount(1)
  const download=page.waitForEvent('download');await page.getByLabel('Меню Выход ГТО',{exact:true}).click();await page.getByRole('button',{name:'Скачать PDF',exact:true}).click()
  const file=await download;expect(file.suggestedFilename()).toBe('Выход ГТО 2026-10-12 (2 смена).pdf')
  await file.saveAs(`test-results/gto-forecast-${width}.pdf`)
  expect(writes).toEqual([])
  const draft=await page.evaluate(()=>JSON.parse(localStorage.getItem('myapp.gtoDraft.mechanic')))
  expect(draft.rows.v1.forecast).toBe('healthy');expect(draft).not.toHaveProperty('date');expect(draft).not.toHaveProperty('shift')
  await page.getByLabel('Поиск техники',{exact:true}).fill('')
  await page.getByLabel('Меню Выход ГТО',{exact:true}).click();await page.getByRole('button',{name:'Обновить данные ремонта',exact:true}).click()
  await expect(excavator.getByLabel('Примечание механика',{exact:true})).toHaveValue('Замена уплотнения')
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true)
  await page.screenshot({path:`test-results/gto-forecast-${width}.png`,fullPage:true})
 })
}

test('PDF spans pages without losing long rows', async ({ page }) => {
  await page.goto('/')
  const bytes = await page.evaluate(async () => {
    const { createGtoForecastPdf } = await import('/src/utils/gtoForecastPdf.js')
    const rows = Array.from({length:39},(_,i)=>({vehicle:{id:String(i),modelName:`Проверочная машина ${i}`,garageNumber:i},hours:0,hoursDate:'2026-10-11',currentState:'stopped',forecast:'faulty',ktg:'',maintenance:'',currentNotes:('Длинное описание неисправности. ').repeat(25),note:`Конец строки ${i}`,plannedRelease:'После доставки деталей',climate:'unknown'}))
    const blob=await createGtoForecastPdf({date:'2026-10-12',shift:'1',mechanic:'Механик',organisation:'',site:'',groups:[{name:'Техника',rows,ready:0,unknown:0}]})
    return [...new Uint8Array(await blob.arrayBuffer())]
  })
  const { PDFDocument } = await import('pdf-lib')
  const document = await PDFDocument.load(Uint8Array.from(bytes))
  expect(document.getPageCount()).toBeGreaterThan(3)
})

test('open parts requests appear once, including requests without work records', () => {
 const open={id:'p1',requestNumber:'ЗЗП-123',requestDate:'2026-10-11',description:'Насос',requiredParts:'Уплотнение',completedDate:null}
 const closed={id:'p2',requestNumber:'ЗАКРЫТА',completedDate:'2026-10-11'}
 const [row]=buildForecastRows([{vehicle:{id:'v1',operationalState:'stopped'},defects:[],partsRequests:[open,closed],works:[{partsRequests:[open,closed]}]}])
 expect(row.currentNotes).toContain('ЗЗП-123');expect(row.currentNotes).toContain('11.10.2026')
 expect(row.currentNotes).toContain('Насос');expect(row.currentNotes).toContain('Уплотнение')
 expect(row.currentNotes).not.toContain('ЗАКРЫТА')
 expect(row.currentNotes.match(/ЗЗП-123/g)).toHaveLength(1)
 expect(row.isInRepair).toBe(true)
})

test('procurement body precedes its repair reason, which is bold and separated', () => {
 const [row]=buildForecastRows([{vehicle:{id:'v1',operationalState:'stopped'},defects:[{id:'d1',symptoms:'Течь гидроцилиндра'},{id:'d2',symptoms:'Отказ двигателя'}],partsRequests:[{id:'p1',defectId:'d1',description:'Заказать уплотнение'}],works:[{defectId:'d1',status:'awaiting_parts',description:'Течь гидроцилиндра'}]}])
 expect(row.noteBlocks[0].text).toBe('Течь гидроцилиндра')
 expect(row.noteBlocks[2].text).toBe('Заказать уплотнение - Течь гидроцилиндра')
 expect(row.noteBlocks[2].spans[1].italic).toBe(true)
 expect(row.noteBlocks.every(block=>!block.divider)).toBe(true)
})

test('forecast groups use group_car names across different vehicle types', () => {
 const groups=forecastGroups([
  {vehicle:{id:'v1',groupName:'Вспомогательная техника',typeName:'Автобус',garageNumber:20},forecast:'healthy'},
  {vehicle:{id:'v2',groupName:'Вспомогательная техника',typeName:'Грузовой автомобиль',garageNumber:10},forecast:'faulty'},
  {vehicle:{id:'v3',groupName:'Карьерная техника',typeName:'Самосвал'},forecast:'healthy'},
 ])
 const auxiliary=groups.find(group=>group.name==='Вспомогательная техника')
 expect(groups).toHaveLength(2)
 expect(auxiliary.rows.map(row=>row.vehicle.id)).toEqual(['v1','v2'])
 expect(auxiliary.ready).toBe(1)
})

test('identical request number and body are displayed once with the date', () => {
 const [row]=buildForecastRows([{vehicle:{id:'v1',operationalState:'stopped'},partsRequests:[{id:'p1',requestNumber:'проба пера',description:' проба пера ',requiredParts:'ПРОБА ПЕРА',requestDate:'2026-10-10'}]}])
 expect(row.currentNotes).toBe('проба пера от 10.10.2026')
})

test('procurement downtime uses latest awaiting-parts repair description, not initial defect', () => {
 const [row]=buildForecastRows([{vehicle:{id:'v1',operationalState:'stopped'},defects:[{id:'d1',symptoms:'Первичная заявка: не заводится'}],partsRequests:[{id:'p1',defectId:'d1',description:'Купить насос'}],works:[
  {defectId:'d1',status:'awaiting_parts',description:'Старая причина',completedAt:'2026-10-09T10:00:00Z'},
  {defectId:'d1',status:'awaiting_parts',description:'Нет топливного насоса',completedAt:'2026-10-10T10:00:00Z'},
  {defectId:'d1',status:'in_progress',description:'Позднейший осмотр',createdAt:'2026-10-11T10:00:00Z'},
  {defectId:'d2',status:'awaiting_parts',description:'Другая машина'},
 ]}])
 expect(row.currentNotes).toBe('Первичная заявка: не заводится\nКупить насос - Нет топливного насоса')
})
test('missing awaiting-parts stage does not substitute initial repair request', () => {
 const [row]=buildForecastRows([{vehicle:{id:'v1',operationalState:'stopped'},defects:[{id:'d1',symptoms:'Первичная заявка'}],partsRequests:[{id:'p1',defectId:'d1',description:'Купить насос'}]}])
 expect(row.currentNotes).toBe('Первичная заявка\nКупить насос')
})

test('completed repair request is not placed before procurement requests', () => {
 const [row]=buildForecastRows([{vehicle:{id:'v1',operationalState:'stopped'},defects:[{id:'d1',symptoms:'Завершённая неисправность',status:'repaired'}],partsRequests:[{id:'p1',defectId:'d1',description:'Закупочная заявка'}]}])
 expect(row.currentNotes).toBe('Закупочная заявка')
})

test('mechanic healthy forecast hides defects but keeps open procurement text', () => {
 const [row]=buildForecastRows([{vehicle:{id:'v1',operationalState:'stopped'},defects:[{id:'d1',symptoms:'Первичная неисправность'}],partsRequests:[{id:'p1',defectId:'d1',description:'Купить насос'}],works:[{defectId:'d1',status:'awaiting_parts',description:'Нет насоса'}]}])
 expect(forecastNoteBlocks(row).map(block=>block.text).join('\n')).toBe('Первичная неисправность\nКупить насос - Нет насоса')
 row.forecast='healthy'
 expect(forecastNoteBlocks(row).map(block=>block.text).join('\n')).toBe('Купить насос')
 row.forecast='faulty'
 expect(forecastNoteBlocks(row)).toHaveLength(2)
})

test('ordinary 39-vehicle report fits one A4 page with an intact summary', async ({page}) => {
 await page.route('**/api/**', route=>route.fulfill({json:[]}))
 await page.goto('/')
 const bytes=await page.evaluate(async()=>{
  const {createGtoForecastPdf}=await import('/src/utils/gtoForecastPdf.js')
  const groups=Array.from({length:6},(_,group)=>({name:`Группа ${group}`,ready:group===0?7:6,rows:Array.from({length:group===0?9:6},(_,i)=>({vehicle:{id:`${group}-${i}`,modelName:'SANY SY1250H',garageNumber:group*10+i},hours:20511,hoursDate:'2026-10-12',forecast:'healthy',ktg:'',maintenance:'',note:'',climate:'unknown',noteBlocks:[]}))}))
  const blob=await createGtoForecastPdf({date:'2026-10-12',shift:'1',mechanic:'Иванов Иван',organisation:'',site:'',groups})
  return [...new Uint8Array(await blob.arrayBuffer())]
 })
 const {PDFDocument}=await import('pdf-lib')
 const document=await PDFDocument.load(Uint8Array.from(bytes))
 expect(document.getPageCount()).toBe(1)
 const size=document.getPage(0).getSize()
 expect(Math.min(size.width,size.height)).toBeCloseTo(595.28,1)
 const {writeFile}=await import('node:fs/promises')
 await writeFile('test-results/gto-compact.pdf',Uint8Array.from(bytes))
})

test('overflow summary moves as a whole without a table header', async ({page}) => {
 test.setTimeout(60000)
 await page.route('**/api/**', route=>route.fulfill({json:[]}))
 await page.goto('/')
 const bytes=await page.evaluate(async()=>{
  const {createGtoForecastPdf}=await import('/src/utils/gtoForecastPdf.js')
  const groups=Array.from({length:6},(_,g)=>({name:`Проверочная группа ${g}`,ready:0,rows:Array.from({length:9},(_,i)=>({vehicle:{id:`${g}-${i}`,modelName:'SANY',garageNumber:i},forecast:'faulty',ktg:'',maintenance:'',note:('Подробное примечание. ').repeat(35),climate:'unknown',noteBlocks:[]}))}))
  const blob=await createGtoForecastPdf({date:'2026-10-12',shift:'1',mechanic:'Механик',organisation:'',site:'',groups})
  return [...new Uint8Array(await blob.arrayBuffer())]
 })
 const {writeFile}=await import('node:fs/promises')
 await writeFile('test-results/gto-summary-pages.pdf',Uint8Array.from(bytes))
 const {execFileSync}=await import('node:child_process')
 const pages=execFileSync('pdftotext',['-layout','test-results/gto-summary-pages.pdf','-'],{encoding:'utf8'}).split('\f')
 const summary=pages.findIndex(text=>text.includes('СВОДКА ВЫХОДА'))
 expect(summary).toBeGreaterThanOrEqual(0)
 for(let i=0;i<6;i++)expect(pages[summary]).toContain(`Проверочная группа ${i}:`)
 expect(pages.slice(summary+1).join('')).not.toMatch(/Проверочная группа/)
 if(!pages[summary].includes('Неисправен'))expect(pages[summary]).not.toContain('Неисправность / примечание')
})

test('persistent preferences and vehicle data, unrestricted search and healthy masking', async ({page}) => {
 await page.addInitScript(()=>{localStorage.setItem('myapp.authToken',`test.${btoa(JSON.stringify({sub:'mechanic',role:'administrator'}))}.test`);localStorage.setItem('myapp.userRole','Administrator')})
 let preferences={organisation:'Организация пользователя',site:'Участок пользователя',hasUserSettings:true}
 let climate='healthy',plan='15.10.2026',closed=false
 const writes=[]
 await page.route('**/api/**',route=>{
  const req=route.request(),path=new URL(req.url()).pathname
  if(req.method()==='PUT') {
   const body=req.postDataJSON();writes.push({path,body})
   if(path.endsWith('/user'))preferences={...preferences,...body}
   else {climate=body.climate;plan=body.plannedRelease}
   return route.fulfill({status:204})
  }
  const journals=[{vehicle:{id:'v1',garageNumber:94,modelName:'SANY',operationalState:'stopped'},defects:[{id:closed?'d2':'d1',symptoms:'Неисправность',operationalState:'stopped',downtimeStartedAt:'2026-10-01T08:00:00Z'}],works:[]},
   {vehicle:{id:'v2',garageNumber:901,modelName:'Самосвал',operationalState:'healthy'},defects:[],works:[]}]
  if(path==='/api/gto-settings')return route.fulfill({json:{...preferences,vehicles:[{vehicleId:'v1',climate}],plans:[{defectId:'d1',plannedRelease:plan}]}})
  if(path==='/api/vehicles/repair-journal')return route.fulfill({json:journals})
  if(path==='/api/vehicles')return route.fulfill({json:journals.map(item=>item.vehicle)})
  if(path==='/api/vehicles/fleet-options')return route.fulfill({json:{canManage:true}})
  return route.fulfill({json:{}})
 })
 await page.goto('/')
 await page.getByRole('button',{name:'Выход ГТО',exact:true}).click()
 await expect(page.getByLabel('Организация',{exact:true})).toHaveValue('Организация пользователя')
 await expect(page.getByLabel('Плановый выход из ремонта',{exact:true})).toHaveValue('15.10.2026')
 await page.getByLabel('Организация',{exact:true}).fill('Новая организация')
 await page.getByLabel('Организация',{exact:true}).blur()
 await expect.poll(()=>writes.length).toBe(1)
 await page.getByLabel('Кондиционирование и отопление',{exact:true}).selectOption('faulty')
 await expect.poll(()=>writes.length).toBe(2)
 await page.getByLabel('Плановый выход из ремонта',{exact:true}).fill('16.10.2026')
 await page.getByLabel('Плановый выход из ремонта',{exact:true}).blur()
 await expect.poll(()=>writes.length).toBe(3)
 await page.getByLabel('Состояние к смене',{exact:true}).selectOption('healthy')
 await expect(page.getByLabel('Плановый выход из ремонта',{exact:true})).toHaveCount(0)
 await expect(page.locator('.gto-current-notes')).toHaveCount(0)
 await page.getByLabel('Состояние к смене',{exact:true}).selectOption('faulty')
 await expect(page.getByLabel('Плановый выход из ремонта',{exact:true})).toHaveValue('16.10.2026')
 await page.getByLabel('Поиск техники',{exact:true}).fill('901')
 await expect(page.locator('.gto-row')).toHaveCount(1)
 await expect(page.locator('.gto-row')).toContainText('Самосвал')
 await page.getByLabel('Кондиционирование и отопление',{exact:true}).selectOption('absent')
 await expect.poll(()=>writes.length).toBe(4)
 await page.getByLabel('Поиск техники',{exact:true}).fill('')
 await page.getByLabel('Меню Выход ГТО',{exact:true}).click();await page.getByRole('button',{name:'Обновить данные ремонта',exact:true}).click()
 await expect(page.getByLabel('Участок',{exact:true})).toHaveValue('Участок пользователя')
 await expect(page.getByLabel('Организация',{exact:true})).toHaveValue('Новая организация')
 await expect(page.getByLabel('Кондиционирование и отопление',{exact:true})).toHaveValue('absent')
 closed=true
 await page.getByLabel('Меню Выход ГТО',{exact:true}).click();await page.getByRole('button',{name:'Обновить данные ремонта',exact:true}).click()
 await expect(page.getByLabel('Плановый выход из ремонта',{exact:true})).toHaveValue('')
})

test('healthy forecast hides dates temporarily without changing stored row', () => {
 const row={forecast:'healthy',downtimeStartedAt:'2026-10-01',plannedRelease:'2026-10-15'}
 expect(forecastDocumentRow(row)).toEqual({forecast:'healthy',downtimeStartedAt:'',plannedRelease:''})
 expect(row.plannedRelease).toBe('2026-10-15')
 row.forecast='faulty'
 expect(forecastDocumentRow(row).plannedRelease).toBe('2026-10-15')
})

test('report group priority is ascending, ties use group name', () => {
 const groups=forecastGroups([
  {vehicle:{id:'1',groupName:'Бульдозеры',groupReportSortOrder:10},forecast:'healthy'},
  {vehicle:{id:'2',groupName:'Экскаваторы',groupReportSortOrder:50},forecast:'healthy'},
  {vehicle:{id:'3',groupName:'Автосамосвалы',groupReportSortOrder:10},forecast:'healthy'},
  {vehicle:{id:'4',groupName:'Прочая техника',groupReportSortOrder:-1},forecast:'healthy'},
 ])
 expect(groups.map(group=>group.name)).toEqual(['Прочая техника','Автосамосвалы','Бульдозеры','Экскаваторы'])
})

test('mechanic note belongs to repair symptoms in parentheses, not procurement', () => {
 const row={forecast:'faulty',note:'/***/',noteBlocks:[{text:'Утечка топлива',kind:'defect',bold:true},{text:'проба пера от 10.10.2026 - требуется инжектор',spans:[{text:'проба пера от 10.10.2026'},{text:' - требуется инжектор',italic:true}]},{text:'перья пишут от 11.10.2026 - Требуется гидроцилиндр.',spans:[{text:'перья пишут от 11.10.2026'},{text:' - Требуется гидроцилиндр.',italic:true}]}]}
 const blocks=forecastPdfNoteBlocks(row)
 expect(blocks.map(block=>block.text)).toEqual(['Утечка топлива (/***/)','проба пера от 10.10.2026 - требуется инжектор','перья пишут от 11.10.2026 - Требуется гидроцилиндр.'])
 expect(row.noteBlocks[0].text).toBe('Утечка топлива')
})

test('within group vehicles cluster by type, then sort by number', () => {
 const [group]=forecastGroups([
  {vehicle:{id:'e10',groupName:'Техника',typeName:'Экскаватор',garageNumber:10},forecast:'healthy'},
  {vehicle:{id:'b2',groupName:'Техника',typeName:'Бульдозер',garageNumber:2},forecast:'healthy'},
  {vehicle:{id:'e2',groupName:'Техника',typeName:'Экскаватор',garageNumber:2},forecast:'healthy'},
 ])
 expect(group.rows.map(row=>row.vehicle.id)).toEqual(['b2','e2','e10'])
})

test('responsible name uses surname and initials', () => {
 expect(responsibleShortName('Иванов Иван Иванович')).toBe('Иванов И. И.')
 expect(responsibleShortName('  Крюков  Владимир Сергеевич ')).toBe('Крюков В. С.')
 expect(responsibleShortName('Иванов')).toBe('Иванов')
 expect(responsibleShortName('')).toBe('')
})

test('dump truck has two independent engines and replacement values', () => {
 const [row]=buildForecastRows([{vehicle:{id:'v1',typeName:'Карьерный самосвал'},hours:[{engineNumber:1,engineHours:875,readingDate:'2026-10-12'},{engineNumber:2,engineHours:852,readingDate:'2026-10-12'},{engineNumber:0,engineHours:18225,readingDate:'2026-10-13'}]}])
 expect(row.engines).toHaveLength(2)
 expect(engineHoursText(row)).toBe('875/852')
 row.engines[0].hoursAtReplacement=18225;row.engines[1].hoursAtReplacement=17900
 expect(engineHoursText(row,true)).toBe('18225/17900')
 row.engines[1].hours=null
 expect(engineHoursText(row)).toBe('875/')
 expect(row.legacyHours).toBe(18225)
})
test('single engine keeps historical legacy readings without inventing second engine', () => {
 const [row]=buildForecastRows([{vehicle:{id:'v1',typeName:'Экскаватор'},hours:[{engineHours:20511,readingDate:'2026-10-12'}]}])
 expect(row.engines).toHaveLength(1)
 expect(engineHoursText(row)).toBe('20511')
 row.engines[0].hoursAtReplacement=15000
 expect(engineHoursText(row,true)).toBe('15000')
})

test('two engine input values persist independently and appear as slash pairs in PDF', async ({page}) => {
 await page.addInitScript(()=>{localStorage.setItem('myapp.authToken',`test.${btoa(JSON.stringify({sub:'mechanic',role:'administrator'}))}.test`);localStorage.setItem('myapp.userRole','Administrator')})
 const values=new Map([[1,{engineNumber:1,engineHours:875,readingDate:'2026-10-12'}],[2,{engineNumber:2,engineHours:852,readingDate:'2026-10-12'}]]),replacements=new Map(),writes=[]
 await page.route('**/api/**',route=>{
  const req=route.request(),path=new URL(req.url()).pathname
  if(req.method()==='PUT' && path.includes('/engines/')) {
   const number=Number(path.split('/').at(-1)),body=req.postDataJSON()
   writes.push({number,...body});if(body.engineHours!=null)values.set(number,{engineNumber:number,engineHours:body.engineHours,readingDate:body.readingDate});replacements.set(number,{vehicleId:'v1',engineNumber:number,hoursAtReplacement:body.hoursAtReplacement})
   return route.fulfill({json:{...body,engineNumber:number}})
  }
  const vehicle={id:'v1',modelName:'SANY SET150',garageNumber:901,typeName:'Самосвал',engineCount:2,operationalState:'healthy'}
  if(path==='/api/gto-settings')return route.fulfill({json:{organisation:'',site:'',vehicles:[],plans:[],engines:[...replacements.values()]}})
  if(path==='/api/vehicles/repair-journal')return route.fulfill({json:[{vehicle,defects:[],works:[],hours:[...values.values()]}]})
  if(path==='/api/vehicles')return route.fulfill({json:[vehicle]})
  if(path==='/api/vehicles/fleet-options')return route.fulfill({json:{canManage:true}})
  return route.fulfill({json:{}})
 })
 await page.goto('/')
 await page.getByRole('button',{name:'Выход ГТО',exact:true}).click()
 await page.getByLabel('Поиск техники',{exact:true}).fill('901')
 await expect(page.locator('.gto-engine')).toHaveCount(2)
 for(const [index,hours,replacement] of [[0,'875','18225'],[1,'852','17900']]) {
  const engine=page.locator('.gto-engine').nth(index)
  await expect(engine.getByLabel('Наработка ДВС, м/ч',{exact:true})).toHaveCount(0)
  await expect(engine.getByLabel('Дата показаний',{exact:true})).toHaveCount(0)
  await engine.getByLabel('М/ч при замене ДВС',{exact:true}).fill(replacement)
  await engine.getByLabel('М/ч при замене ДВС',{exact:true}).blur()
  await expect.poll(()=>replacements.get(index+1)?.hoursAtReplacement).toBe(Number(replacement))
 }
 await page.getByLabel('Меню Выход ГТО',{exact:true}).click();await page.getByRole('button',{name:'Обновить данные ремонта',exact:true}).click()
 await expect(page.getByText('Наработка: 875/852 м/ч',{exact:false})).toBeVisible()
 const downloadPromise=page.waitForEvent('download')
 await page.getByLabel('Меню Выход ГТО',{exact:true}).click();await page.getByRole('button',{name:'Скачать PDF',exact:true}).click()
 await (await downloadPromise).saveAs('test-results/gto-two-engines.pdf')
 const {execFileSync}=await import('node:child_process')
 const text=execFileSync('pdftotext',['-layout','test-results/gto-two-engines.pdf','-'],{encoding:'utf8'})
 expect(text).toContain('875/852');expect(text).toContain('18225/17900')
 expect(writes.every(item=>[1,2].includes(item.number) && item.engineHours===null)).toBe(true)
})
