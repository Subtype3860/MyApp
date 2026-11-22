# Release 20261122-chars

Published to https://boora.ru/ on 2026-11-22 (Asia/Sakhalin).

## Персонажи и рейдеры из набора внедрены через Blender

Blender 5.3 (c:\Users\boora\Total\Soft\Blender) использован для конвертации glTF/.blend в FBX:
_convert_blender.py → Games/Frostfall/_converted/*.fbx.

Импортированы и собраны префабы:

| Ключ игры | Модель | Высота |
|---|---|---|
| Raiders/Vampire | vamoir.glb → FBX | 2,2 м |
| Raiders/Spirit | SpiritGirl.gltf → FBX | 2,0 м |
| Survivor | peasant .blend → FBX | 1,8 м |

Плюс ранее внедрённые постройки и орк. Скрипт импорта: Assets/Scripts/Editor/PackSetup.cs.

## Проверка

- Unity: WebGL build Succeeded: 27 MB, 0 errors.
- WebGL.data на сервере совпадает с локальной сборкой (4235dec…).
- Игра грузится (__fortpostReady = true), новых ошибок нет.
- Бэкап прежней сборки — /var/backups/myapp/release-20261122-chars/games-fortpost.

## Отложено

- Костёр (CAMPFIRE.blend) и палисад (palisade.blend) сконвертированы в FBX, но в игре
  нет подходящих слотов (это декор, а не постройки) — можно добавить как украшения лагеря.
- Лесопилка Sawmill.rar — RAR без 7-Zip на машине не распаковать; слот лесопилки занят
  Woodcutter Hut.