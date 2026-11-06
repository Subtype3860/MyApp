import {test,expect} from '@playwright/test'
for(const width of [390,1440]) {
 test('action menu supports keyboard, disabled actions and viewport boundaries at '+width,async({page})=>{
  await page.setViewportSize({width,height:800})
  await page.goto('/')
  await page.evaluate(async()=>{
   const {createApp,h}=await import(performance.getEntriesByType('resource').find(entry=>entry.name.includes('/.vite/deps/vue.js?')).name)
   const {default:Menu}=await import('/src/components/FleetActionMenu.vue')
   const root=document.createElement('div');root.style.cssText='position:fixed;bottom:4px;right:4px;width:44px;height:44px;overflow:hidden;z-index:9999';document.body.append(root)
   window.menuActions=0
   createApp({render:()=>h(Menu,{label:'Тестовое меню'},{default:()=>[h('button',{disabled:true},'Недоступно'),h('button',{onClick:()=>window.menuActions++},'Первое действие'),h('button',{},'Второе действие')]})}).mount(root)
  })
  const trigger=page.getByRole('button',{name:'Тестовое меню',exact:true})
  await trigger.focus();await trigger.press('ArrowDown')
  await expect(page.getByRole('button',{name:'Первое действие',exact:true})).toBeFocused()
  await page.keyboard.press('ArrowDown')
  await expect(page.getByRole('button',{name:'Второе действие',exact:true})).toBeFocused()
  const rect=await page.locator('[popover]:popover-open').boundingBox()
  expect(rect.x).toBeGreaterThanOrEqual(0);expect(rect.y).toBeGreaterThanOrEqual(0);expect(rect.x+rect.width).toBeLessThanOrEqual(width);expect(rect.y+rect.height).toBeLessThanOrEqual(800)
  await page.keyboard.press('Escape');await expect(trigger).toBeFocused();await expect(trigger).toHaveAttribute('aria-expanded','false')
  await trigger.click();await page.getByRole('button',{name:'Первое действие',exact:true}).click()
  expect(await page.evaluate(()=>window.menuActions)).toBe(1)
  await expect(trigger).toHaveAttribute('aria-expanded','false')
  await trigger.click();await page.mouse.click(10,10);await expect(trigger).toHaveAttribute('aria-expanded','false')
 })
}
