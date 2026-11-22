# Release 20261122-sanctum

Published to https://boora.ru/ on 2026-11-22 (Asia/Sakhalin).

## «Форпост»: модель Mausoleum_LOD0 как Светилище

- Модель Mausoleum_LOD0 импортирована в Unity: Assets/Resources/Models/Buildings/Sanctum.fbx.
- Скрипт Assets/Scripts/Editor/SanctumSetup.cs: импорт, сжатие текстур до 1024, mesh compression,
  префаб Resources/Models/Buildings/Sanctum.prefab. Исходная высота 12.57 → масштаб 0.366
  (итог 4.6 юнита), чтобы вставать на площадку процедурной постройки.
- Игра подхватывает префаб по ключу Models/Buildings/Sanctum (ModelBank), процедурное светилище
  заменяется моделью.
- WebGL пересобран: data 11.1 МБ, wasm 14.6 МБ, итог ~26 МБ; выложен в /opt/myapp/www/games/fortpost
  (бэкап прежней сборки — /var/backups/myapp/release-20261122-sanctum/games-fortpost), архив сверен по SHA-256.

## Проверка

- Unity: [Frostfall] WebGL build Succeeded: 26 MB, 0 errors, 0 warnings.
- В данных сборки присутствуют ресурсы Sanctum и Chapel.
- Живая проверка: игра грузится (__fortpostReady = true), новых ошибок нет; остались прежние
  предупреждения Unity про отсутствующие классы коллайдеров.

## Не сделано (требует Blender/ручной работы)

- **UV2 под лайтмапы**: генерация в Unity падает/висит на плотной модели (Uv unwrap failed),
  поэтому в импорте отключена. Игра использует реальное время, лайтмапы не запекаются.
- **Запекание normal/AO с HighPoly на LOD0**: в экспорте есть только BaseColor; нужен Blender
  (HighPoly .blend есть в Games/mausoleum/out/blends). Blender на машине не установлен.
- **Отрезать нижнюю плиту**: правка геометрии в Blender.
- **Коллизия из LOD2**: LOD2 есть только в .glb (Unity без glTF-плагина не импортирует),
  и ModelBank.Spawn намеренно удаляет коллайдеры — игра считает попадания сама.