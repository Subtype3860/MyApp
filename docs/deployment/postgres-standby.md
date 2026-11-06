# Горячий резерв PostgreSQL

Цель: второй сервер с полной копией базы `job`, который принимает записи
журнала в реальном времени. При аварии основного сервера резерв повышается до
основного, приложение переключается на него за несколько минут.

## Что это даёт

- **RPO** — от нуля до нескольких секунд: при асинхронной репликации последние
  транзакции могут не дойти. Включение синхронной репликации даёт RPO = 0, но
  останавливает записи, если резерв недоступен.
- **RTO** — обычно 5–15 минут ручного переключения.
- Резерв работает **только на чтение** (`hot standby`) и постоянно догоняет
  основной.

Автоматическое переключение (Patroni и т.п.) для двух узлов без третьего
свидетеля опасно split-brain, поэтому по умолчанию — ручной failover.

## Требования

- Второй сервер или VM с той же major-версией PostgreSQL 17.
- Диск не меньше основного плюс запас под WAL (минимум +20%).
- Сетевой доступ с резерва к основному по TCP 5432 и с основного к резерву
  для `pg_basebackup`.
- На обоих серверах — `podman` и образ `docker.io/library/postgres:17`.
- Резервное копирование (pg_dump) всё равно нужно: реплика повторяет и
  ошибочные `DELETE`.

## Схема

```
API (myapp)  --->  primary PostgreSQL 17 (10.8.8.8:5432, container myapp-postgres)
                             |
                             | streaming replication (async, physical slot)
                             v
                   standby PostgreSQL 17 (<standby>:5433, container myapp-postgres-standby)
```

## 1. Подготовка основного сервера

На основном сервере создайте файл с параметрами (не коммитьте его в Git):

```bash
sudo install -d -m 0755 /etc/myapp
sudo install -m 0600 /dev/null /etc/myapp/pg-replication.env
sudo editor /etc/myapp/pg-replication.env
```

Содержимое:

```bash
PG_CONTAINER=myapp-postgres
REPL_USER=replicator
REPL_PASSWORD=<длинный случайный пароль>
REPL_SLOT=myapp_standby
STANDBY_CIDR=10.8.8.9/32
```

Запуск:

```bash
sudo bash -c 'set -a; . /etc/myapp/pg-replication.env; set +a; \
  bash scripts/deploy/postgres-primary-prepare.sh'
```

Скрипт:

- включает `wal_level=replica` (если был `minimal`), `max_wal_senders`,
  `wal_keep_size=1GB`, `hot_standby=on`;
- создаёт роль `replicator` и физический слот `myapp_standby`;
- добавляет в `pg_hba.conf` строку для резерва и перезагружает конфигурацию.

Если скрипт сообщил о необходимости перезапуска — перезапустите контейнер в
согласованное окно:

```bash
podman restart myapp-postgres
```

Проверка:

```bash
podman exec myapp-postgres psql -U postgres -c "SHOW wal_level;"
podman exec myapp-postgres psql -U postgres -c "SELECT * FROM pg_replication_slots;"
```

## 2. Инициализация резерва

На резервном сервере:

```bash
sudo install -d -m 0755 /etc/myapp
sudo install -m 0600 /dev/null /etc/myapp/pg-standby.env
sudo editor /etc/myapp/pg-standby.env
```

```bash
PRIMARY_HOST=10.8.8.8
PRIMARY_PORT=5432
REPL_USER=replicator
REPL_PASSWORD=<тот же пароль>
REPL_SLOT=myapp_standby
PG_VOLUME=myapp-pg-standby-data
PG_CONTAINER=myapp-postgres-standby
STANDBY_PORT=5433
```

Запуск:

```bash
sudo bash -c 'set -a; . /etc/myapp/pg-standby.env; set +a; \
  bash scripts/deploy/postgres-standby-init.sh'
```

Скрипт делает `pg_basebackup` в новый том, настраивает `standby.signal` и
`primary_conninfo`, запускает контейнер на порту 5433 и проверяет
`pg_is_in_recovery() = true`.

> Порт 5433 выбран, чтобы резерв можно было поднять на том же хосте для
> проверки. На отдельном сервере можно использовать 5432.

## 3. Проверка репликации

На основном:

```bash
podman exec myapp-postgres psql -U postgres -c "
SELECT client_addr, state, sent_lsn, replay_lsn,
       write_lag, flush_lag, replay_lag
FROM pg_stat_replication;"
```

На резерве:

```bash
podman exec myapp-postgres-standby psql -U postgres -c "
SELECT pg_is_in_recovery() AS standby,
       pg_last_wal_receive_lsn() AS received,
       pg_last_wal_replay_lsn() AS replayed,
       now() - pg_last_xact_replay_timestamp() AS replay_delay;"
```

Норма: `standby = t`, `replay_delay` — секунды. Если `state` не `streaming`,
смотрите `podman logs myapp-postgres-standby` и `pg_hba.conf` на основном.

## 4. Failover при аварии основного

> Сначала убедитесь, что основной действительно недоступен, а не пропала сеть.
> Если оба сервера живы и оба считают себя основными — это split-brain;
> остановите один из них до продолжения.

1. Остановите API на сервере приложения:

   ```bash
   sudo systemctl stop myapp
   ```

2. Повысьте резерв:

   ```bash
   sudo bash -c 'set -a; . /etc/myapp/pg-standby.env; set +a; \
     bash scripts/deploy/postgres-promote.sh'
   ```

3. Переключите приложение на новый адрес. В env-файле сервиса `myapp`
   (там, где задан `ConnectionStrings__DefaultConnection`) замените хост и порт:

   ```text
   ConnectionStrings__DefaultConnection=Host=10.8.8.9;Port=5433;Database=job;Username=boora;Password=...
   ```

   Если используется DNS-имя `db.myapp.local`, достаточно переключить A-запись
   на резерв — приложение перезапускать не обязательно.

4. Запустите API и проверьте:

   ```bash
   sudo systemctl start myapp
   curl -fsS https://boora.ru/api/...
   ```

   `DatabaseInitializer` при старте выполнит идемпотентные `CREATE TABLE IF NOT
   EXISTS` / `ALTER TABLE`, это нормально для нового основного.

5. Восстановите медиа на новом сервере из резервной копии (см.
   [media-replica.md](media-replica.md)) и переключите `TARGET` rsync.

## 5. Возврат старого сервера

Не включайте старый основной как есть: его журнал разошёлся с новым.

Предпочтительный путь — пересоздать его как резерв:

1. Остановите и удалите контейнер и том старого основного **только после**
   проверки, что данные на новом основном корректны.
2. На старом сервере выполните `postgres-standby-init.sh`, указав
   `PRIMARY_HOST` = новый основной.
3. Проверьте `pg_stat_replication` на новом основном.

`pg_rewind` возможен, но требует `wal_log_hints=on` или контрольных сумм,
включённых **до** аварии. Без этого используйте пересоздание.

## 6. Ежедневные резервные копии

Реплика не заменяет бэкап. На резерве (или отдельном хосте) ежедневно:

```bash
sudo install -d -m 0700 /var/backups/myapp/postgres
podman exec myapp-postgres-standby \
  pg_dump -U postgres -Fc job > /var/backups/myapp/postgres/job-$(date +%F).dump
find /var/backups/myapp/postgres -name 'job-*.dump' -mtime +14 -delete
```

Проверяйте дамп восстановлением в одноразовый контейнер, как это уже делается
в `scripts/deploy/diagnose-isolated-api.sh`.

## 7. Настройки приложения

| Параметр | Значение | Когда менять |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | хост/порт основного | при failover |
| `Database__Initialize` | `true` | `false` только для API, подключённого к read-only резерву |
| `MediaStorage__RetentionEnabled` | `true` | `false` на резерве/отладочном экземпляре |

`Database:Initialize=false` нужен, если API временно смотрит в резерв на чтение:
иначе старт упадёт на `CREATE TABLE` в read-only транзакции.

## 8. Мониторинг

Минимальный набор проверок:

- `pg_stat_replication.state = streaming` на основном;
- `now() - pg_last_xact_replay_timestamp()` на резерве меньше 60 секунд;
- свободное место на резерве;
- успешный ежедневный `pg_dump`;
- `systemctl status myapp` и `journalctl -u myapp` после переключения.

## Чек-лист перед вводом в эксплуатацию

- [ ] На основном `wal_level=replica`, слот `myapp_standby` активен.
- [ ] Резерв запущен, `pg_is_in_recovery() = t`.
- [ ] `pg_stat_replication` показывает `streaming`.
- [ ] Проверено повышение резерва на тестовой копии.
- [ ] Описан и проверен способ переключения `ConnectionStrings`.
- [ ] Настроен ежедневный `pg_dump` и проверено восстановление.
- [ ] Настроен rsync медиа и проверено восстановление файлов.
- [ ] Известно, где лежат пароли и кто имеет доступ к контейнерам.
