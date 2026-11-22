# Release 20261122-pack

Published to https://boora.ru/ on 2026-11-22 (Asia/Sakhalin).

## Модели из набора «ДляИгры» внедрены в «Форпост»

Импортированы FBX-модели и собраны префабы (масштаб по фактической высоте, посадка на землю):

| Постройка игры | Модель | Высота |
|---|---|---|
| Угольная шахта | MESH_Mine | 4,5 м |
| Убежище | MESH_Barracks | 3,0 м |
| Сторожевая башня | MESH_ScoutTower | 6,5 м |
| Склад | MESH_TownHall | 4,0 м |
| Лесопилка | MESH_WoodcutterHut | 3,4 м |
| Орк (рейдер) | warrior_animation | 2,2 м |

Скрипт импорта: Assets/Scripts/Editor/PackSetup.cs.

## Проверка

- Unity: WebGL build Succeeded: 26 MB, 0 errors.
- WebGL.data на сервере совпадает с локальной сборкой (9d197af6…).
- Игра грузится (__fortpostReady = true), новых ошибок нет.
- Бэкап прежней сборки — /var/backups/myapp/release-20261122-pack/games-fortpost.

## Не внедрено (нужна конвертация)

- Вампир amoir.glb и дух SpiritGirl.gltf — glTF, Unity без плагина не импортирует.
- Крестьянин peasant .blend, костёр, палисад, лесопилка (.blend/.rar) — нужен Blender.
- Текстуры forest tree pack — модели деревьев в .rar, игра строит деревья процедурно.