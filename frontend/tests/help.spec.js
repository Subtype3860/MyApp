import { test, expect } from '@playwright/test'
import { helpArticles } from '../src/data/helpArticles'
for(const width of [390,1440]){
 test('help is accessible, searchable and illustrated at '+width,async({page})=>{
  await page.setViewportSize({width,height:900})
  await page.addInitScript(()=>{
   localStorage.setItem('myapp.authToken','test-token')
   localStorage.setItem('myapp.userRole','user')
   localStorage.setItem('myapp.userPermissions','[]')
  })
  await page.route('**/api/**',route=>route.fulfill({json:new URL(route.request().url()).pathname==='/api/profile'?{firstName:'Иван',hasAvatar:false}:[]}))
  await page.goto('/')
  if(width<760)await page.getByRole('button',{name:'Открыть меню',exact:true}).click()
  await page.getByRole('button',{name:'Помощь',exact:true}).click()
  await expect(page.getByRole('heading',{name:'Помощь',exact:true})).toBeVisible()
  for(const article of helpArticles){
   await page.getByRole('navigation',{name:'Темы помощи'}).getByRole('button',{name:article.title,exact:true}).click()
   await expect(page.locator('.help-article h2')).toHaveText(article.title)
   await expect.poll(()=>page.locator('.help-article img').evaluate(img=>img.complete&&img.naturalWidth>0)).toBe(true)
  }
  const search=page.getByRole('searchbox',{name:'Поиск по справочнику'})
  await search.fill('КТГ')
  await expect(page.getByRole('navigation',{name:'Темы помощи'}).getByRole('button',{name:'Выход ГТО: заполнение и выгрузка',exact:true})).toBeVisible()
  await search.fill('неттакойтемы123')
  await expect(page.getByText('По этому запросу ничего не найдено.',{exact:false})).toBeVisible()
  await search.fill('')
  await page.getByRole('navigation',{name:'Темы помощи'}).getByRole('button',{name:'Выход ГТО: заполнение и выгрузка',exact:true}).click()
  await page.getByRole('button',{name:'Увеличить схему'}).click()
  await expect(page.getByRole('dialog',{name:'Схема действий'})).toBeVisible()
  await page.keyboard.press('Escape')
  await expect(page.getByRole('dialog',{name:'Схема действий'})).not.toBeVisible()
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true)
  await page.screenshot({path:'test-results/help-'+width+'.png',fullPage:true})
 })
}
