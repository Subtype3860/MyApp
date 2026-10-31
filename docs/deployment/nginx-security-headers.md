# Заголовки безопасности nginx

Источник конфигурации для production-сервера `boora.ru`. Файл
`deploy/nginx/security-headers.conf` — это ровно тот snippet, который лежит
на сервере в `/etc/nginx/snippets/security-headers.conf`.

Состав:

| Заголовок | Значение | Зачем |
| --- | --- | --- |
| `Strict-Transport-Security` | `max-age=31536000` | браузер год не обращается к сайту по HTTP |
| `X-Content-Type-Options` | `nosniff` | запрет MIME-sniffing |
| `X-Frame-Options` | `SAMEORIGIN` | запрет встраивания в чужой iframe |
| `Referrer-Policy` | `strict-origin-when-cross-origin` | не утекает полный URL во внешние ресурсы |
| `Permissions-Policy` | `geolocation=(), microphone=(), camera=()` | отключены неиспользуемые API устройства |
| `Content-Security-Policy` | см. файл | запрет inline-скриптов, eval и внешних источников |

## Почему CSP именно такая

- `script-src 'self'` — в собранном фронтенде нет inline-скриптов, `eval` и
  `new Function`; всё грузится модулем из `/assets/`.
- `style-src 'self' 'unsafe-inline'` — Vue вставляет `<style>` в рантайме,
  поэтому inline-стили разрешены. Скриптов это послабление не касается.
- `img-src`/`media-src`/`worker-src` с `blob:` — canvas-текстуры и воркеры игр
  (`/games/tanks/`, `/games/mechanic/`) создаются из `blob:`-URL.
- `connect-src 'self'` покрывает `/api/*` и WebSocket `/api/tanks/socket`
  (и `wss://` к тому же хосту).
- `frame-ancestors 'self'` дублирует `X-Frame-Options` для современных браузеров.

## Как применять

```bash
install -m 644 security-headers.conf /etc/nginx/snippets/security-headers.conf
restorecon -v /etc/nginx/snippets/security-headers.conf
nginx -t && systemctl reload nginx
```

Snippet подключается в `/etc/nginx/conf.d/myapp.conf` **в трёх местах**:

```nginx
server {
    server_tokens off;
    include /etc/nginx/snippets/security-headers.conf;
    ...
    location ^~ /games/ {
        add_header Cache-Control "no-cache";
        include /etc/nginx/snippets/security-headers.conf;
    }
    location ~* \.(js|css|png|...)$ {
        add_header Cache-Control "public, immutable";
        include /etc/nginx/snippets/security-headers.conf;
    }
}
```

Повтор в `location` обязателен: nginx не наследует `add_header` из `server`,
если у самого `location` есть хотя бы один `add_header`. Без этого `/games/`
и статика оставались бы без CSP и остальных заголовков.

## Проверка

```bash
for u in / /login /games/tanks/ /games/tanks/online.js; do
  curl -skI "https://127.0.0.1$u" -H 'Host: boora.ru' \
    | grep -ci 'content-security-policy'
done
```

Все пути должны вернуть `1`. Дополнительно страницы проверялись в headless
Chromium (`/`, `/login`, `/games/tanks/`, `/games/mechanic/`): нарушений CSP и
ошибок консоли нет.

## Ограничения

- `Strict-Transport-Security` задан без `includeSubDomains`: поддомены, если
  появятся, останутся доступны по HTTP.
- При смене фронтенда, если появится inline-скрипт или внешний CDN, страница
  перестанет работать — политику нужно будет расширять осознанно, а не
  добавлением `'unsafe-inline'` в `script-src`.
- CSP дублируется в nginx, а не в репозитории исполнения: этот файл —
  эталон, на сервер его нужно доставлять при переустановке или смене конфига.
