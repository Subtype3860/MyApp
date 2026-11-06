import { test, expect } from '@playwright/test'
test('switching regions keeps separate fleet, organisation and saved forecasts', async ({page})=>{
 await page.addInitScript(()=>{localStorage.setItem('myapp.authToken','test.token');localStorage.setItem('myapp.userRole','Administrator')})
 const reports=new Map(), regions=[{id:'a',name:'Север'},{id:'b',name:'Юг'}]
 await page.route('**/api/**',async route=>{
  const req=route.request(),url=new URL(req.url()),path=url.pathname,r=url.searchParams.get('regionId')
  if(path==='/api/fleet-regions')return route.fulfill({json:{regions}})
  if(path==='/api/fleet-regions/vehicles')return route.fulfill({json:[{vehicleId:'v1',regionId:'a'},{vehicleId:'v2',regionId:'b'}]})
  if(path==='/api/vehicles/fleet-options')return route.fulfill({json:{canManage:true}})
  if(path==='/api/gto-settings')return route.fulfill({json:{organisation:'Не использовать',site:'Не использовать'}})
  if(path==='/api/vehicles/repair-journal')return route.fulfill({json:['v1','v2'].map((id,i)=>({vehicle:{id,modelName:'Техника '+id,garageNumber:911+i,operationalState:'stopped'},defects:[{id:'d'+i,symptoms:'Ремонт'}],works:[],hours:[]}))})
  if(path==='/api/gto-reports')return route.fulfill({json:[]})
  if(path==='/api/gto-reports/defaults')return route.fulfill({json:{rows:[]}})
  if(path.startsWith('/api/gto-reports/')){
   if(req.method()==='PUT'){const body=req.postDataJSON();reports.set(r,{revision:body.revision+1,snapshot:body.snapshot,updatedAt:new Date().toISOString()});return route.fulfill({json:reports.get(r)})}
   return reports.has(r)?route.fulfill({json:reports.get(r)}):route.fulfill({status:404,json:{}})
  }
  return route.fulfill({json:{}})
 })
 await page.goto('/');await page.getByRole('button',{name:'Отчёты',exact:true}).click();await page.getByRole('button',{name:'Выход ГТО',exact:true}).click()
 const select=page.getByLabel('Организация / участок',{exact:true})
 await expect(page.locator('.gto-row')).toHaveCount(0)
 await select.selectOption('a');await expect(page.locator('.gto-row')).toContainText('911')
 await page.getByLabel('Вид ТО',{exact:true}).fill('ТО Север')
 await expect.poll(()=>reports.get('a')?.snapshot.rows[0].maintenance).toBe('ТО Север')
 await select.selectOption('b');await expect(page.locator('.gto-row')).toContainText('912')
 await expect.poll(()=>reports.get('b')?.snapshot.organisation).toBe('Юг')
 expect(reports.get('a').snapshot.organisation).toBe('Север')
 expect(reports.get('a').snapshot.rows.map(x=>x.vehicle.id)).toEqual(['v1'])
 expect(reports.get('b').snapshot.rows.map(x=>x.vehicle.id)).toEqual(['v2'])
 await select.selectOption('a');await expect(page.getByLabel('Вид ТО',{exact:true})).toHaveValue('ТО Север')
})
