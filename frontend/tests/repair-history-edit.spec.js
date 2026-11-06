import {test,expect} from '@playwright/test'
for (const [position,creator,allowed] of [['Механик','other',true],['Старший механик','other',true],['Слесарь','employee',true],['Слесарь','other',false]]) {
 test('history request editing for '+position+' author '+creator,async({page})=>{
  await page.addInitScript(()=>{localStorage.setItem('myapp.authToken','test.'+btoa(JSON.stringify({sub:'employee',role:'user'}))+'.test');localStorage.setItem('myapp.userRole','User');localStorage.setItem('myapp.userPermissions',JSON.stringify(['menu.vehicles','vehicles.works']))})
  const vehicle={id:'v1',modelName:'SANY',garageNumber:901}
  const defect={id:'d1',createdById:creator,symptoms:'Исходное описание',createdAt:'2026-10-22T01:00:00Z'}
  let fail=true,writes=0
  await page.route('**/api/**',route=>{
   const req=route.request(),path=new URL(req.url()).pathname
   if(req.method()==='PUT' && path==='/api/vehicles/history/defects/d1') {writes++;if(fail)return route.fulfill({status:500,json:{}});defect.symptoms=req.postDataJSON().description;return route.fulfill({status:204})}
   if(path==='/api/profile')return route.fulfill({json:{id:'employee',position}})
   if(path==='/api/vehicles')return route.fulfill({json:[vehicle]})
   if(path==='/api/vehicles/repair-journal')return route.fulfill({json:[{vehicle,defects:[defect],works:[]}]})
   return route.fulfill({json:[]})
  })
  await page.goto('/')
  await page.getByRole('button',{name:'Транспорт',exact:true}).click()
  await page.getByRole('button',{name:'История ремонта',exact:true}).click()
  await page.locator('.repair-history__row').first().click()
  await expect(page.locator('.repair-request__description')).toHaveText('Исходное описание')
  const edit=page.getByRole('button',{name:'Редактировать заявку',exact:true})
  if(!allowed){await expect(edit).toHaveCount(0);expect(writes).toBe(0);return}
  await edit.click()
  const dialog=page.getByRole('dialog',{name:'Редактировать заявку'})
  await dialog.getByLabel('Описание',{exact:true}).fill('Исправленный текст\nВторая строка')
  await dialog.getByRole('button',{name:'Сохранить',exact:true}).click()
  await expect(dialog.getByRole('alert')).toContainText('Не удалось сохранить')
  await expect(dialog.getByLabel('Описание',{exact:true})).toHaveValue('Исправленный текст\nВторая строка')
  fail=false
  await dialog.getByRole('button',{name:'Сохранить',exact:true}).click()
  await expect(dialog).not.toBeVisible()
  await expect(page.locator('.repair-request__description')).toHaveText('Исправленный текст\nВторая строка')
  await edit.click()
  await dialog.getByLabel('Описание',{exact:true}).fill('Не сохранять')
  await dialog.getByRole('button',{name:'Отмена',exact:true}).click()
  expect(writes).toBe(2)
  await page.getByRole('dialog',{name:'Заявки техники'}).getByRole('button',{name:'Закрыть',exact:true}).click()
  await page.getByLabel('Меню истории ремонта',{exact:true}).click();await page.getByRole('button',{name:'Обновить',exact:true}).click()
  await page.locator('.repair-history__row').first().click()
  await expect(page.locator('.repair-request__description')).toHaveText('Исправленный текст\nВторая строка')
  await expect(page.getByLabel('Действия с заявкой',{exact:true})).toHaveCount(0)
 })
}
