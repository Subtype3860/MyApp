import { test, expect } from '@playwright/test'

function server() {
  const reports = new Map()
  let fail = false, liveLoads = 0
  const vehicle = { id:'v1', modelName:'Экскаватор', garageNumber:94, operationalState:'stopped' }
  async function attach(page, editor = true) {
    await page.addInitScript(editor => {
      localStorage.setItem('myapp.authToken', `test.${btoa(JSON.stringify({sub:editor?'mechanic':'reader'}))}.test`)
      localStorage.setItem('myapp.userRole', editor?'Administrator':'User')
      localStorage.setItem('myapp.userPermissions', JSON.stringify(['menu.vehicles','vehicles.report']))
    }, editor)
    await page.route('**/api/**', route => {
      const req=route.request(),url=new URL(req.url()),path=url.pathname
      if(path==='/api/gto-reports')return route.fulfill({json:[...reports.entries()].map(([key,value])=>({date:key.split('/')[0],shift:Number(key.split('/')[1]),revision:value.revision,updatedAt:value.updatedAt})).reverse()})
      if(path==='/api/gto-reports/defaults') {
        const key=url.searchParams.get('date')+'/'+url.searchParams.get('shift')
        const prior=[...reports.keys()].filter(item=>item<key).sort().at(-1)
        return route.fulfill({json:prior?reports.get(prior).snapshot:{rows:[]}})
      }
      if(path.startsWith('/api/gto-reports/')) {
        const key=path.slice('/api/gto-reports/'.length), saved=reports.get(key)
        if(req.method()==='GET')return saved?route.fulfill({json:saved}):route.fulfill({status:404,json:{}})
        if(!editor)return route.fulfill({status:403})
        if(fail)return route.fulfill({status:503,json:{message:'Не удалось сохранить отчёт.'}})
        const body=req.postDataJSON()
        if(body.revision!==(saved?.revision??0))return route.fulfill({status:409,json:{message:'Отчёт изменён другим пользователем.'}})
        const next={revision:body.revision+1,updatedAt:new Date().toISOString(),snapshot:body.snapshot}
        reports.set(key,next);return route.fulfill({json:next})
      }
      if(path==='/api/vehicles/fleet-options')return route.fulfill({json:{canManage:editor,canManageHours:editor}})
      if(path==='/api/profile')return route.fulfill({json:{lastName:'Иванов',firstName:'Иван'}})
      if(path==='/api/gto-settings')return route.fulfill({json:{organisation:'Компания',site:'Участок 1',vehicles:[],plans:[],engines:[]}})
      if(path==='/api/vehicles/repair-journal'){liveLoads++;return route.fulfill({json:[{vehicle,defects:[{id:'d1',symptoms:'Сохранённая неисправность'}],works:[],hours:[]}]})}
      if(path==='/api/vehicles')return route.fulfill({json:[vehicle]})
      return route.fulfill({json:[]})
    })
  }
  return {reports,attach,setFail(value){fail=value},get liveLoads(){return liveLoads}}
}

async function open(page) {
  await page.goto('/')
  await page.getByRole('button',{name:'Отчёты',exact:true}).click()
  await page.getByRole('button',{name:'Выход ГТО',exact:true}).click()
  await expect(page.getByLabel('КТГ, %',{exact:true})).toBeVisible()
}

test('autosave survives a separate browser, carries KTG and maintenance forward and freezes history', async ({page,browser}) => {
  const api=server();await api.attach(page);await open(page)
  await page.getByLabel('КТГ, %',{exact:true}).fill('87.5')
  await page.getByLabel('Вид ТО',{exact:true}).fill('ТО-2')
  await expect.poll(()=>[...api.reports.values()][0]?.snapshot.rows[0].maintenance).toBe('ТО-2')
  const firstKey=[...api.reports.keys()][0]
  const original=structuredClone(api.reports.get(firstKey))
  expect(original.snapshot.rows[0].ktg).toBe(87.5)

  const context=await browser.newContext()
  const reader=await context.newPage();await api.attach(reader,false);await open(reader)
  await expect(reader.getByLabel('КТГ, %',{exact:true})).toHaveValue('87.5')
  await expect(reader.getByLabel('КТГ, %',{exact:true})).toBeDisabled()
  await expect(reader.getByLabel('Вид ТО',{exact:true})).toHaveValue('ТО-2')
  expect(api.liveLoads).toBe(1)
  await reader.getByLabel('Меню Выход ГТО',{exact:true}).click()
  const download=reader.waitForEvent('download')
  await reader.getByRole('button',{name:'Скачать Excel',exact:true}).click()
  expect((await download).suggestedFilename()).toContain('.xlsx')
  await context.close()

  await page.getByLabel('Дата прогноза',{exact:true}).fill('2099-01-01')
  await expect(page.getByLabel('Вид ТО',{exact:true})).toHaveValue('ТО-2')
  await page.getByLabel('КТГ, %',{exact:true}).fill('75')
  await expect.poll(()=>[...api.reports.entries()].find(([key])=>key.startsWith('2099'))?.[1].snapshot.rows[0].ktg).toBe(75)
  expect(api.reports.get(firstKey)).toEqual(original)
  await page.getByLabel('История отчётов',{exact:true}).selectOption(firstKey)
  await page.getByRole('button',{name:'Открыть отчёт',exact:true}).click()
  await expect(page.getByLabel('КТГ, %',{exact:true})).toHaveValue('87.5')
  await expect(page.getByLabel('КТГ, %',{exact:true})).toBeDisabled()
  await expect(page.getByText('Сохранённая неисправность',{exact:true})).toBeVisible()
  expect(api.liveLoads).toBe(2)
})

test('failed autosave retries without losing edits and conflicts do not overwrite another user', async ({page}) => {
  const api=server();await api.attach(page);await open(page)
  await expect.poll(()=>api.reports.size).toBe(1)
  const key=[...api.reports.keys()][0]
  api.setFail(true)
  await page.getByLabel('Вид ТО',{exact:true}).fill('ТО-3')
  await expect(page.getByText('Не удалось сохранить отчёт.',{exact:true})).toBeVisible()
  expect(api.reports.get(key).snapshot.rows[0].maintenance).toBe('')
  api.setFail(false)
  await page.getByRole('button',{name:'Повторить сохранение',exact:true}).click()
  await expect.poll(()=>api.reports.get(key).snapshot.rows[0].maintenance).toBe('ТО-3')
  api.reports.get(key).revision++
  api.reports.get(key).snapshot.rows[0].maintenance='Изменено другим пользователем'
  await page.getByLabel('Вид ТО',{exact:true}).fill('Моя правка')
  await expect(page.getByText('Отчёт изменён другим пользователем.',{exact:true})).toBeVisible()
  expect(api.reports.get(key).snapshot.rows[0].maintenance).toBe('Изменено другим пользователем')
  page.once('dialog',dialog=>dialog.accept())
  await page.getByRole('button',{name:'Открыть серверную версию',exact:true}).click()
  await expect(page.getByLabel('Вид ТО',{exact:true})).toHaveValue('Изменено другим пользователем')
})

test('missing past reports are not reconstructed from current repair data', async ({page}) => {
  const api=server();await api.attach(page);await open(page)
  await expect.poll(()=>api.reports.size).toBe(1)
  await page.getByLabel('Дата прогноза',{exact:true}).fill('2000-01-01')
  await expect(page.getByRole('alert')).toContainText('отчёт не был сохранён')
  expect(api.reports.size).toBe(1)
  expect(api.liveLoads).toBe(1)
  await expect(page.locator('.gto-row')).toHaveCount(0)
})
