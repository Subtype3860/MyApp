# Резервное копирование медиафайлов

Медиа MyApp лежит на сетевой шаре и локальном staging-диске. Скрипт
`scripts/deploy/media-replica.sh` раз в 10 минут догоняет изменения на втором
сервере или внешнем диске через `rsync`.

## Что копируется

| Источник | Назначение на резерве |
|---|---|
| `/mnt/dietpi/img` | `img/` |
| `/mnt/dietpi/video` | `video/` |
| `/opt/myapp/media-staging/img` | `staging/img/` |
| `/opt/myapp/media-staging/video` | `staging/video/` |

Staging-каталоги копируются, чтобы не потерять только что загруженные файлы,
которые ещё не успели переехать в постоянное хранилище.

## Требования

- `rsync` 3.2.3 или новее (используется `--mkpath`).
- На резервном сервере — каталог `/srv/myapp-media` и пользователь `backup`.
- SSH-ключ пользователя `myapp` без пароля, только для `rsync`.
- Свободное место не меньше объёма медиа на основном сервере.

## Установка

На основном сервере:

```bash
sudo install -m 0755 scripts/deploy/media-replica.sh /usr/local/bin/myapp-media-replica
sudo install -d -m 0755 /etc/myapp
sudo install -m 0640 -g myapp deploy/media-replica.env.example /etc/myapp/media-replica.env
sudo editor /etc/myapp/media-replica.env
```

Создайте ключ и разрешите вход на резерв:

```bash
sudo -u myapp ssh-keygen -t ed25519 -N '' -f /home/myapp/.ssh/myapp-media-replica
sudo -u myapp ssh-copy-id -i /home/myapp/.ssh/myapp-media-replica.pub backup@10.8.8.9
```

На резервном сервере:

```bash
sudo install -d -o backup -g backup -m 0755 /srv/myapp-media
```

Установите unit-файлы из `deploy/systemd/`:

```bash
sudo install -m 0644 deploy/systemd/myapp-media-replica.service /etc/systemd/system/
sudo install -m 0644 deploy/systemd/myapp-media-replica.timer /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now myapp-media-replica.timer
```

## Проверка

Сначала пробный запуск без изменений:

```bash
sudo -u myapp DRY_RUN=1 /usr/local/bin/myapp-media-replica
```

Затем боевой запуск и статус:

```bash
sudo systemctl start myapp-media-replica.service
systemctl status myapp-media-replica.service
journalctl -u myapp-media-replica.service -n 50
systemctl list-timers myapp-media-replica.timer
```

На резерве проверьте, что появились файлы и их количество совпадает:

```bash
find /srv/myapp-media/img -type f | wc -l
```

## Восстановление

Если основной сервер потерян, скопируйте медиа обратно **до** запуска API:

```bash
rsync -a /srv/myapp-media/img/ /mnt/dietpi/img/
rsync -a /srv/myapp-media/video/ /mnt/dietpi/video/
rsync -a /srv/myapp-media/staging/img/ /opt/myapp/media-staging/img/
rsync -a /srv/myapp-media/staging/video/ /opt/myapp/media-staging/video/
```

Пути в базе данных хранят абсолютные имена файлов, поэтому восстанавливать
нужно именно в те каталоги, которые указаны в `MediaStorage`.

## Связь с очисткой по сроку и объёму

Приложение удаляет старые файлы (см. настройки «Хранение медиафайлов»), а
`rsync --delete-delay` повторяет удаление на резерве. Это осознанно: резерв
является копией, а не архивом.

Если нужен архив без удалений, создайте отдельную копию с `ALLOW_DELETE=0`
и другим `TARGET` — она будет только дополняться.

## Ограничения

- Скрипт не шифрует данные; для передачи через интернет используйте SSH/VPN.
- При недоступном источнике, отсутствующей точке монтирования или пустом
  постоянном каталоге запуск завершается ошибкой и **не** удаляет файлы на
  резерве. Staging-каталоги могут быть пустыми — для них удаление разрешено.
- `rsync` не является транзакционным: файл, записанный в момент копирования,
  может попасть на резерв при следующем запуске.
