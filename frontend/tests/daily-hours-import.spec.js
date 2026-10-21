import { test, expect } from '@playwright/test'
import { zipSync, strToU8 } from 'fflate'
import { readFile } from 'node:fs/promises'
import { sheetDate, extractDailyRows } from '../src/utils/dailyHoursImport.js'

test('sheet dates tolerate punctuation and spaces without guessing ambiguous dates', () => {
 for (const name of ['08.10', ' 08 . 10.. ', '08,10', '08 — 10', '(08.10)', '08 10', '\u00a008\u202f.\u00a010! ', '8..10;']) {
  expect(sheetDate(name, 2026), name).toBe('2026-10-08')
 }
 for (const name of ['', '0810', '0 8.10', '08.10.2026', '08.10 (2)', '08 октября', '31..02', '00.10', '08.13']) {
  expect(sheetDate(name, 2026), name).toBeNull()
 }
 expect(sheetDate('29 . 02.', 2024)).toBe('2024-02-29')
 expect(sheetDate('29 . 02.', 2026)).toBeNull()
 const cells = { A1:{value:'Техника'}, B1:{value:'М/ч'}, A2:{value:'SET 901'}, B2:{value:100} }
 const parsed = extractDailyRows([{name:'08.10',cells},{name:' 08 , 10! ',cells}], 'A1:Z50', 2026)
 // Keep both sources: the server must see and reject the duplicate date/counter.
 expect(parsed.issues).toEqual([])
 expect(parsed.rows.map(r=>r.readingDate)).toEqual(['2026-10-08','2026-10-08'])
 expect(parsed.rows[1].source).toBe(' 08 , 10! !B2')
})

/** Создаёт XLSX с заданными листами для проверки импорта. */
function workbook() {
 const entries = {
  'xl/workbook.xml': '<workbook xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="08.10." r:id="rId1"/><sheet name="09.10" r:id="rId2"/></sheets></workbook>',
  'xl/_rels/workbook.xml.rels': '<Relationships><Relationship Id="rId1" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Target="worksheets/sheet2.xml"/></Relationships>',
 }
 for (const i of [1,2]) entries[`xl/worksheets/sheet${i}.xml`] = `<worksheet><sheetData><row r="20"><c r="Q20" t="inlineStr"><is><t>А/самосвалы</t></is></c><c r="T20" t="inlineStr"><is><t>М/ч ДВС1 начало смены</t></is></c><c r="V20" t="inlineStr"><is><t>М/ч ДВС2 начало смены</t></is></c></row><row r="21"><c r="Q21" t="inlineStr"><is><t>SET 901</t></is></c>${i===1?'<c r="T21"><v>100</v></c><c r="V21"><v>200</v></c>':''}</row></sheetData></worksheet>`
 return Buffer.from(zipSync(Object.fromEntries(Object.entries(entries).map(([key,value])=>[key,strToU8(value)]))))
}

test('client parser handles shifted blocks, dates, blanks and invalid cells', async ({page}) => {
 await page.goto('/')
 const result=await page.evaluate(async bytes=>{
  const m=await import('/src/utils/dailyHoursImport.js')
  const sheets=await m.readDailyWorkbook(new File([new Uint8Array(bytes)],'hours.xlsx'))
  const parsed=m.extractDailyRows(sheets,'A1:Z50',2026)
  sheets[1].cells.T21={value:'ошибка'}
  const invalid=m.extractDailyRows(sheets,'A1:Z50',2026).rows
  return {parsed,invalid,date:m.sheetDate('31.02',2026),number:m.garageNumber('Sany 1250/91'),model:m.garageNumber('Sany1250')}
 }, [...workbook()])
 expect(result.parsed.issues).toEqual([])
 expect(result.parsed.rows.map(r=>r.engineHours)).toEqual([100,200,null,null])
 expect(result.parsed.rows[0]).toMatchObject({readingDate:'2026-10-08',garageNumber:901,source:'08.10.!T21'})
 expect(result.invalid[2].invalid).toBe(true)
 expect(result.date).toBeNull();expect(result.number).toBe(91);expect(result.model).toBeNull()
})

for(const width of [390,1440]) test(`daily preview precedes JSON commit ${width}`,async({page})=>{
 await page.setViewportSize({width,height:1000})
 await page.addInitScript(()=>{localStorage.setItem('myapp.authToken',`test.${btoa(JSON.stringify({sub:'mechanic',role:'administrator'}))}.test`);localStorage.setItem('myapp.userRole','Administrator')})
 const writes=[]
 await page.route('**/api/**',route=>{
  const request=route.request(),path=new URL(request.url()).pathname
  if(request.method()==='POST') {
   const body=request.postDataJSON();writes.push({path,body,contentType:request.headers()['content-type']})
   if(path.endsWith('/preview'))return route.fulfill({json:{fingerprint:'verified',rows:body.rows.map(r=>({...r,vehicleId:'v1',engineHours:r.engineHours??(r.engineNumber===1?100:200),status:'ready',message:'Готово'}))}})
   return route.fulfill({json:{imported:4}})
  }
  return route.fulfill({json:path.endsWith('/fleet-options')?{canManageHours:true}:[]})
 })
 await page.goto('/');if(width<761)await page.getByRole('button',{name:'Открыть меню',exact:true}).click()
 await page.getByRole('button',{name:'Транспорт',exact:true}).click();await page.getByRole('button',{name:'Моточасы',exact:true}).click()
 await page.getByRole('button',{name:'Импорт Excel / CSV'}).click()
 await page.getByLabel('Ежедневный Excel').setInputFiles({name:'hours.xlsx',mimeType:'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',buffer:workbook()})
 await page.getByRole('button',{name:'Проверить файл',exact:true}).click()
 await expect(page.getByRole('button',{name:'Сохранить 4 показаний'})).toBeVisible()
 expect(writes).toHaveLength(1)
 await page.getByRole('button',{name:'Сохранить 4 показаний'}).click()
 await expect(page.getByRole('dialog')).toContainText('Сохранено показаний: 4')
 expect(writes).toHaveLength(2);expect(writes[1].contentType).toContain('application/json')
 expect(writes[1].body.fingerprint).toBe('verified')
 expect(writes[1].body.rows[2].engineHours).toBeNull()
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true)
})

test('supplied workbook has 448 values and 198 downtime cells',async({page})=>{
 test.skip(!process.env.HOURS_SAMPLE,'Set HOURS_SAMPLE to an external workbook for acceptance testing.')
 const bytes=await readFile(process.env.HOURS_SAMPLE)
 await page.goto('/')
 const result=await page.evaluate(async data=>{
  const m=await import('/src/utils/dailyHoursImport.js')
  const sheets=await m.readDailyWorkbook(new File([new Uint8Array(data)],'sample.xlsx'))
  const parsed=m.extractDailyRows(sheets,'A1:Z50',2026)
  return {sheets:sheets.length,issues:parsed.issues,values:parsed.rows.filter(r=>r.engineHours!==null).length,blanks:parsed.rows.filter(r=>r.engineHours===null).length,invalid:parsed.rows.filter(r=>r.invalid).length}
 },[...bytes])
 expect(result).toEqual({sheets:17,issues:[],values:448,blanks:198,invalid:0})
})
