# Атлас

Нативный Android-клиент находится в [android](android/README.md).
Текущее покрытие и полный объём офлайн-версии: [матрица Android](docs/android/coverage.md).

Бэкенд состоит из трёх проектов:

| Проект | Ответственность |
| --- | --- |
| `src/MyApp.API` | HTTP-контроллеры, авторизация запросов, конфигурация и запуск приложения. |
| `src/MyApp.Application` | Бизнес-сценарии (`Services`), модели предметной области (`Models`), DTO, права доступа и интерфейсы репозиториев (`Abstractions`). |
| `src/MyApp.Infrastructure` | Доступ к PostgreSQL через Entity Framework Core, реализации репозиториев, хранение файлов, JWT и хеширование паролей. |

Application не зависит от API и Infrastructure. Infrastructure ссылается на
Application и реализует его интерфейсы. API подключает оба проекта и связывает
интерфейсы с реализациями через внедрение зависимостей.

Обычный путь запроса: контроллер → сервис Application → репозиторий
Infrastructure → PostgreSQL. В текущей реализации некоторые контроллеры,
например `GtoSettingsController`, также обращаются к базе напрямую.

Модели бывшего проекта Domain находятся в `MyApp.Application/Models`
с пространством имён `MyApp.Application.Models`.
Отдельный проект `MyApp.Infrastructure.Tests` содержит тесты бэкенда.

Сборка и запуск тестов:

```shell
dotnet build src/MyApp.API.sln
dotnet test src/MyApp.API.sln --filter "Category!=Integration"
```

Интеграционные тесты запускаются отдельно и требуют настройки тестовой
PostgreSQL-базы через `MYAPP_TEST_POSTGRES`.
