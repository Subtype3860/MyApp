import { test, expect } from '@playwright/test'
const catalog = [
  {key:'menu.tables',label:'Таблицы',children:[{key:'tables.v_full_ost',label:'Остатки на складе'},{key:'tables.v_meh_ost',label:'Остатки механиков'},{key:'tables.v_workers',label:'Работники'},{key:'tables.selected_components',label:'Выбранные компоненты'}]},
  {key:'menu.vehicles',label:'Транспорт',children:[{key:'vehicles.repair_request',label:'Заявка на ремонт'},{key:'vehicles.repair',label:'Ремонт'},{key:'vehicles.repair_history',label:'История ремонта'},{key:'vehicles.maintenance_execution',label:'Проведение ТО'},{key:'vehicles.parts_request',label:'Заявка на закупку ЗЧ'},{key:'vehicles.hours',label:'Моточасы'}]},
  {key:'menu.reports',label:'Отчёты',children:[{key:'reports.shift',label:'Отчёт за смену'},{key:'reports.gto',label:'Выход ГТО'}]},
  {key:'menu.auxiliary_works',label:'Вспомогательные работы',children:[]},
  {key:'menu.requirements',label:'Выписанные требования',children:[]},
  {key:'menu.maintenance',label:'Техническое обслуживание',children:[]},
]
for(const width of [390,1440]) {
 test('permission groups keep their own children at '+width,async({page})=>{
  await page.setViewportSize({width,height:1000})
  await page.route('**/api/**', route => {
   const path=new URL(route.request().url()).pathname
   return route.fulfill({json:path==='/api/admin/users/permissions'?catalog:path==='/api/admin/users'?[{id:'test-user',userName:'worker',firstName:'Иван',lastName:'Иванов',middleName:'',role:'user',permissions:[]}]:[]})
  })
  await page.goto('/')
  await page.evaluate(async()=>{
   const {createApp,h}=await import(performance.getEntriesByType('resource').find(e=>e.name.includes('/.vite/deps/vue.js?')).name)
   const {default:Settings}=await import('/src/components/SettingsView.vue')
   document.querySelector('#app').remove()
   const root=document.createElement('div');document.body.append(root)
   createApp({render:()=>h(Settings,{navigationCollapsed:true,selectedSection:'users',token:'test-token'})}).mount(root)
  })
  await page.getByRole('button',{name:'Редактировать пользователя worker'}).click()
  const permissions=page.locator('.user-permissions')
  for(const group of catalog) {
   const block=permissions.getByRole('group',{name:group.label,exact:true})
   await expect(block.getByRole('checkbox')).toHaveCount(1+group.children.length)
   const rect=await block.boundingBox()
   for(const child of group.children) {
    const checkbox=block.getByRole('checkbox',{name:child.label,exact:true})
    await expect(checkbox).toBeVisible()
    const childRect=await checkbox.boundingBox()
    expect(childRect.y).toBeGreaterThan(rect.y)
    expect(childRect.y+childRect.height).toBeLessThanOrEqual(rect.y+rect.height)
   }
  }
  await permissions.getByRole('checkbox',{name:'История ремонта',exact:true}).check()
  await expect(permissions.getByRole('checkbox',{name:'Отчёт за смену',exact:true})).not.toBeChecked()
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true)
  await permissions.screenshot({path:'test-results/permission-groups-'+width+'.png'})
 })
}
