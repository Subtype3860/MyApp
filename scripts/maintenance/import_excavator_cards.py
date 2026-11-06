"""Extract reference cards as unpublished templates; never alter source workbooks."""
import json, pathlib, re, sys, uuid, zipfile
import xml.etree.ElementTree as E
N = {'m': 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'}
def sheets(path):
    with zipfile.ZipFile(path) as z:
        strings = [''.join(x.itertext()) for x in E.fromstring(z.read('xl/sharedStrings.xml')).findall('m:si', N)]
        for i, sheet in enumerate(E.fromstring(z.read('xl/workbook.xml')).find('m:sheets', N), 1):
            rows = []
            for row in E.fromstring(z.read(f'xl/worksheets/sheet{i}.xml')).findall('.//m:sheetData/m:row', N):
                values = {}
                for cell in row:
                    v = cell.find('m:v', N)
                    if v is not None:
                        text = strings[int(v.text)] if cell.get('t') == 's' else v.text
                        values[re.sub(r'\d', '', cell.get('r'))] = text.strip()
                rows.append((int(row.get('r')), values))
            yield sheet.get('name').strip(), rows

def extract(path):
    for sheet, rows in sheets(path):
        if not rows: continue
        if not sheet.startswith('ТО-'): continue  # matrix-shaped bulldozer card is handled separately below
        model = 'SANY SY1250H'
        operations, materials = [], []
        section, mode, control, filters = '', False, False, False
        for r, row in rows:
            title = row.get('B', '')
            if title.rstrip(':') in ['ДВС', 'Шасси']:
                section = title.rstrip(':'); continue
            if title.startswith('Замена смазочных'): mode = True; continue
            if title == 'Чек-лист': mode = False; control = True; section = 'Заключительный контроль'; continue
            if title.startswith('Примечание'): break
            if mode:
                if 'Каталожный' in ' '.join(row.values()): filters = True
                name = row.get('E' if filters else 'D', '')
                if not title or title in ['Агрегат', 'Смазывающие', 'Фильтры'] or not name: continue
                q = row.get('I', '')
                try: quantity = float(q.replace(',', '.'))
                except ValueError: quantity = None
                materials.append(dict(id=f'm{r}', assembly=title, name=name, partNumber=row.get('D','') if filters else '', unit=row.get('J',''), quantity=quantity, note=q if quantity is None else ''))
            elif section and title and not title.startswith('Наименование'):
                operations.append(dict(id=f'o{r}', section=section, text=title, target='', control=control))
        yield dict(model=model, service=sheet, source=pathlib.Path(path).name+' · '+sheet, reviewNotes='Проверить нормы, единицы, условные операции и измерения. Исходные личные данные и даты не переносятся в шаблон.', operations=operations, materials=materials)
    for sheet, rows in sheets(path):
        if sheet != '933': continue
        for col, service in zip('RSTUV', ['ТО-250','ТО-500','ТО-1000','ТО-2000','ТО-4000']):
            operations=[]; section='Плановые работы'
            for r,row in rows:
                if r < 12: continue
                if row.get('A','').startswith('II.'): break
                name=row.get('B','')
                if not name: continue
                if not any(row.get(c) for c in 'RSTUV'):
                    section=name; continue
                applicable=row.get(col)
                conditional=row.get('R')=='по необходимости'
                if applicable or conditional:
                    text=name
                    if row.get('Q'): text+=' (Количество в исходнике: '+row['Q']+')'
                    if conditional: text+=' — по необходимости'
                    operations.append(dict(id=f'o{r}',section=section,text=text,target='',control=False))
            yield dict(model='KOMATSU D375А-6',service=service,source=pathlib.Path(path).name+' · '+sheet,reviewNotes='В исходнике это бульдозер. Операции отобраны по отметкам соответствующего ТО; количество без единицы оставлено в тексте, расход материалов нужно настроить отдельно. Подтвердить условные операции и заключительный контроль.',operations=operations,materials=[])

if __name__=='__main__':
    target=pathlib.Path('src/MyApp.Infrastructure/Db/Seeds/maintenance-cards.json')
    seeds=json.loads(target.read_text(encoding='utf-8'))
    for template in extract(sys.argv[1]):
        key=str(uuid.uuid5(uuid.NAMESPACE_URL,'atlas-maintenance-card/'+template['model']+'/'+template['service']))
        seed=dict(id=key,revision=1,published=False,snapshot=json.dumps(template,ensure_ascii=False))
        seeds=[s for s in seeds if s['id']!=key]+[seed]
    target.write_text(json.dumps(seeds,ensure_ascii=False,indent=2),encoding='utf-8')
    print('Templates:',len(seeds))
