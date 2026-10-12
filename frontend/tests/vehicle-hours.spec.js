import { test, expect } from '@playwright/test'
for (const width of [390,1440]) test(`engine entry and file import ${width}`,async({page})=>{
 await page.setViewportSize({width,height:1000})
 await page.addInitScript(()=>{localStorage.setItem('myapp.authToken',`test.${btoa(JSON.stringify({sub:'mechanic',role:'administrator'}))}.test`);localStorage.setItem('myapp.userRole','Administrator')})
 const journals=[{vehicle:{id:'v1',modelName:'SANY',garageNumber:901,typeName:'Самосвал',engineCount:2},hours:[],works:[],defects:[]},{vehicle:{id:'v2',modelName:'SANY',garageNumber:94,typeName:'Экскаватор',engineCount:1},hours:[],works:[],defects:[]}],writes=[]
 await page.route('**/api/**',route=>{const r=route.request(),p=new URL(r.url()).pathname;if(r.method()==='POST'){writes.push({path:p,data:r.postData()});return route.fulfill({json:p.endsWith('/import')?{imported:2,errors:[{line:4,message:'Техника не найдена.'}]}:'saved'})}return route.fulfill({json:p==='/api/vehicles/repair-journal'?journals:p==='/api/vehicles'?journals.map(x=>x.vehicle):p==='/api/vehicles/fleet-options'?{canManage:true,canManageHours:true}:[]})})
 await page.goto('/');if(width<761)await page.getByRole('button',{name:'Открыть меню',exact:true}).click()
 await page.getByRole('button',{name:'Транспорт',exact:true}).click();await page.getByRole('button',{name:'Моточасы',exact:true}).click()
 await expect(page.locator('.hours-row')).toHaveCount(2)
 const truck=page.locator('.hours-row').filter({hasText:'901'}),excavator=page.locator('.hours-row').filter({hasText:'94'})
 await excavator.locator('input').fill('20511');await truck.locator('input').nth(0).fill('875');await truck.locator('input').nth(1).fill('852')
 await truck.getByRole('button',{name:'Сохранить',exact:true}).click();await expect(page.getByRole('status').filter({hasText:'сохранены'})).toContainText('сохранены')
 expect(writes.slice(0,2).map(x=>JSON.parse(x.data).engineNumber)).toEqual([1,2]);await expect(excavator.locator('input')).toHaveValue('20511')
 await page.getByRole('button',{name:'Импорт Excel / CSV'}).click();await expect(page.getByRole('dialog')).toBeVisible()
 await page.getByRole('dialog').locator('input[type=file]').setInputFiles({name:'hours.csv',mimeType:'text/csv',buffer:Buffer.from('garage_number;model;engine_1;engine_2\n901;SANY;875;852')})
 await page.getByRole('button',{name:'Импортировать',exact:true}).click();await expect(page.getByRole('dialog')).toContainText('Сохранено показаний: 2');await expect(page.getByRole('dialog')).toContainText('Строка 4')
 expect(writes.at(-1).data).toContain('readingDate');await page.keyboard.press('Escape');expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true)
})
