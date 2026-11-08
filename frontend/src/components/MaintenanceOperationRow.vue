<script setup>
const props=defineProps({operation:Object,answer:Object,media:Array,disabled:Boolean,defaultExecutor:String,defectId:String})
const emit=defineEmits(['change','attach','preview','defect'])
function update(field,value){emit('change',{[field]:value})}
function toggle(checked){
 emit('change',{status:checked?'done':'',...(checked&&!props.answer.executor?.trim()?{executor:props.defaultExecutor||''}:{})})
}
</script>

<template>
 <article class="operation" :aria-label="operation.text">
  <div class="operation-main">
   <div class="operation-description">
    <span class="mobile-caption">Описание операции</span>
    <p>{{operation.text}}</p>
    <small v-if="operation.target">{{operation.target}}</small>
    <small v-if="operation.control" class="control-note">Заключительный контроль</small>
    <small v-if="answer.status==='not-done'||answer.status==='na'" class="exception-note">{{answer.status==='na'?'Не применимо':'Не выполнено'}}: {{answer.reason}}</small>
    <small v-if="answer.condition==='defect'" class="exception-note">Обнаружен дефект: {{answer.note}}</small>
   </div>
   <div class="operation-media">
    <span class="mobile-caption">Медиа выполнения</span>
    <label v-if="!disabled" class="attach-button">Прикрепить медиа
     <input type="file" accept="image/jpeg,image/png,video/mp4,video/webm" :aria-label="'Медиа выполнения: '+operation.text" @change="emit('attach',$event)" />
    </label>
    <small v-if="!disabled">Фото до 4 МБ · видео до 25 МБ</small>
    <span v-if="!media?.length" class="empty-media">Нет вложений</span>
    <button v-for="(file,index) in media" :key="file.id" type="button" class="media-link" @click="emit('preview',file)">{{file.contentType?.startsWith('video/')?'Видео':'Фото'}} {{index+1}} · Открыть</button>
   </div>
   <div class="operation-completion">
    <label class="done-check"><input type="checkbox" :checked="answer.status==='done'" :disabled="disabled" :aria-label="'Выполнено: '+operation.text" @change="toggle($event.target.checked)" /><span>Выполнено</span></label>
    <label class="executor-label">Кто выполнял<input :value="answer.executor" :disabled="disabled" :aria-label="'Исполнитель: '+operation.text" maxlength="200" placeholder="ФИО исполнителя" @input="update('executor',$event.target.value)" /></label>
   </div>
  </div>
  <details class="operation-extra">
   <summary>Замечания, замер или причина невыполнения</summary>
   <fieldset :disabled="disabled">
    <div class="extra-grid"><label>Статус операции<select :value="answer.status" @change="update('status',$event.target.value)"><option value="">Не заполнено</option><option value="done">Выполнено</option><option value="not-done">Не выполнено</option><option value="na">Не применимо</option></select></label>
     <label>Состояние узла<select :value="answer.condition" @change="update('condition',$event.target.value)"><option value="">Не указано</option><option value="ok">Исправен</option><option value="defect">Обнаружен дефект</option></select></label></div>
    <label v-if="['not-done','na'].includes(answer.status)">Причина<input :value="answer.reason" @input="update('reason',$event.target.value)" /></label>
    <label>Замечание<textarea :value="answer.note" rows="2" @input="update('note',$event.target.value)" /></label>
    <div class="extra-grid"><label>Замер<input :value="answer.measurement" type="number" step="any" @input="update('measurement',$event.target.value===''?null:Number($event.target.value))" /></label><label>Единица замера<input :value="answer.unit" @input="update('unit',$event.target.value)" /></label></div>
   </fieldset>
   <p v-if="defectId">Дефект зарегистрирован: {{defectId}}</p>
   <button v-else-if="!disabled&&answer.condition==='defect'" type="button" @click="emit('defect')">Создать заявку без остановки техники</button>
  </details>
 </article>
</template>

<style scoped>
.operation{padding:16px 0;border-bottom:1px solid var(--border);min-width:0}.operation-main{display:grid;grid-template-columns:minmax(0,1.8fr) minmax(150px,1fr) minmax(170px,1fr);gap:20px;align-items:start}.operation-description p{margin:0;line-height:1.5;overflow-wrap:anywhere}.operation-description small{display:block;margin-top:6px}.control-note,.empty-media{color:var(--text-muted,var(--text-main));font-size:12px}.exception-note{color:var(--text-main);font-weight:600}.operation-media,.operation-completion{display:grid;gap:8px;min-width:0}.operation-media small{font-size:11px;line-height:1.4}.attach-button{position:relative;display:flex;align-items:center;justify-content:center;padding:10px;border:1px dashed var(--border);border-radius:8px;min-height:44px;box-sizing:border-box;cursor:pointer;font-size:13px;overflow:hidden}.attach-button input{position:absolute;inset:0;width:100%;opacity:0;cursor:pointer}.attach-button:focus-within{outline:2px solid var(--text-main);outline-offset:2px}.done-check{display:flex;align-items:center;gap:10px;min-height:44px;cursor:pointer;font-weight:600}.done-check input{width:22px;height:22px;accent-color:#1675d1;margin:0}.executor-label{display:grid;gap:6px;font-size:12px}.executor-label input,fieldset input,fieldset textarea,fieldset select{box-sizing:border-box;width:100%;min-width:0;padding:9px;border:1px solid var(--border);border-radius:8px;background:var(--bg-main);color:var(--text-main);font:inherit}.media-link,button{min-height:40px;padding:8px 10px;border:1px solid var(--border);border-radius:8px;background:var(--bg-surface);color:var(--text-main);cursor:pointer}.operation-extra{margin-top:8px}.operation-extra summary{font-size:12px;padding:8px 0;cursor:pointer;color:var(--text-muted,var(--text-main))}fieldset{border:0;min-width:0;padding:10px 0;display:grid;gap:10px}fieldset label{display:grid;gap:5px;font-size:13px}.extra-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:12px}.mobile-caption{display:none}.operation :disabled{opacity:.7;cursor:default}@media(max-width:760px){.operation-main{grid-template-columns:minmax(0,1fr);gap:12px}.mobile-caption{display:block;font-size:11px;font-weight:600;color:var(--text-muted,var(--text-main))}.operation-completion{grid-template-columns:auto minmax(0,1fr);align-items:end;gap:16px}.operation-media{justify-items:start}.operation-media .attach-button{width:100%}.extra-grid{grid-template-columns:minmax(0,1fr)}}
</style>
