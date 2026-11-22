# Release 20261122-chapel

Published to https://boora.ru/ on 2026-11-22 (Asia/Sakhalin).

## «Форпост»: модель Martinskapelle_LOD0 как часовня

- Модель Martinskapelle_LOD0 из Games/martinskapelle/out/export импортирована в Unity-проект
  (Games/Frostfall/Assets/Resources/Models/Buildings/Chapel.fbx, LOD0).
- Скрипт Assets/Scripts/Editor/ChapelSetup.cs импортирует модель, сжимает текстуры до 1024,
  включает mesh compression и собирает префаб Resources/Models/Buildings/Chapel.prefab
  (высота приведена к 4.2 юнита; исходная высота 36.7, масштаб 0.114).
- Игра сама подхватывает префаб по ключу Models/Buildings/Chapel (ModelBank) — процедурная
  постройка заменяется моделью.
- WebGL-сборка пересобрана: Build/WebGL.data 7.3 МБ, WebGL.wasm 14.6 МБ, итог ~22 МБ.

## Verification

- Unity batchmode: импорт и сборка — [Frostfall] WebGL build Succeeded: 22 MB, 0 errors, 0 warnings.
- Сборка выложена в /opt/myapp/www/games/fortpost (бэкап прежней — /var/backups/myapp/release-20261122-chapel/games-fortpost),
  контрольная сумма архива сверена (947359df…).
- Живая проверка в браузере: https://boora.ru/games/fortpost/index.html загружается,
  __fortpostReady = true, канвас 960×600, новых ошибок нет (остались прежние предупреждения
  Unity про отсутствующие классы коллайдеров — это особенность сборки, не связана с моделью).
- В данных сборки присутствует ресурс Chapel.

## Что проверить вручную

Визуально часовню в headless-браузере не увидеть — нужно в игре построить часовню и убедиться,
что отображается модель, а не процедурная постройка.