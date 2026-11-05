# Отчёт проверки

Дата: 2026-11-05. Проект `/mnt/data/Projects/Csharp/OpenCodeTelegramManager`, один solution, пять production-проектов и два test-проекта. Git main, автор этапов `hatikk <avelsemenov@gmail.com>`. Push не выполнялся.

## Сборка и зависимости

.NET SDK 10.0.114. Прямые NuGet: Microsoft.EntityFrameworkCore 10.0.14, Microsoft.EntityFrameworkCore.Sqlite 10.0.14, Microsoft.EntityFrameworkCore.Design 10.0.14 (PrivateAssets), SQLitePCLRaw.bundle_e_sqlite3 3.0.5, Telegram.Bot 22.5.1. Тесты: Microsoft.NET.Test.Sdk 17.14.1, xunit 2.9.3, xunit.runner.visualstudio 3.1.4, Moq 4.20.72. FrameworkReference Microsoft.AspNetCore.App для Hosting/DI/Options/HttpClientFactory.

- PASS: restore, build без warnings/errors, Release publish.
- PASS: 15 unit + 36 integration tests, 51 всего. В integration входят Node-проверка permission hook/обёртки и шесть Python-проверок настоящего bubblewrap (сеть, секреты, БД, рискованные команды, .NET build, запрет публикации конфигурации OpenCode).
- PASS: audit NuGet включён; `dotnet list src/OpenCodeManager.Worker package --vulnerable --include-transitive` не нашёл известных уязвимых зависимостей.
- PASS: после рефакторинга EF не видит pending model changes. Применённые миграции не меняют схему.

Покрыты: owner/private/chat/callback/file authorization, callback syntax, состояния задач, symlink escape, SQLite constraints/миграции/повторное открытие, FIFO/max concurrency/tree lock, идемпотентность входящих Telegram update, неоднозначная отправка промпта, рестарт в Dispatching, собственный message + idle marker, HTTP errors, SSE frames/cancellation, недоступность OpenCode до отправки, повторная синхронизация main session, lazy navigation, секреты под другим именем, streaming download, upload/overwrite, ZIP exclusions/limits, тип/размер изображения, связывание файлов с задачами и v2 payload, DI граф Telegram.

## SQLite

`/home/hatikk/.local/share/opencode-manager/manager.db`, WAL, busy timeout 15 секунд, foreign keys включены. `integrity_check=ok`, `foreign_key_check=[]`. Таблицы Projects, Sessions, Tasks, Attempts, Events, TelegramMessages, Permissions, Navigation, Roots, Bookmarks, Attachments, Transfers, TaskFiles и служебные EF.

Миграции:

1. `20261105000658_InitialCreate`
2. `20261105001142_NavigationAttachments`
3. `20261105001402_LocalFileBrowser`
4. `20261105002757_FileTransfers`
5. `20261105003603_TaskDraftAndLimits`

Зарегистрирован реальный `/mnt/data/Projects`, четыре технологические категории, четыре независимые Main-сессии. Вложенные каталоги не индексируются массово. Остальные существующие OpenCode-сессии вне разрешённых корней сохраняются на сервере и не открываются ботом автоматически.

## OpenCode

- PASS: версия 2.2.5, Basic authentication, GET info/project/provider/model/agent/session/location.
- PASS: 95 моделей, 16 агентов, 4 провайдера на момент проверки; каталог не доказывает генерацию.
- PASS: отдельная сессия во временной папке, фильтр directory, правильный location, история, rename, смена модели/агента, список permissions, interrupt idle.
- PASS: global SSE server.connected/session.created.
- PASS: PNG принят реальным `/prompt` с `resume=false`, появился в inbox и отменён. Сессия архивирована, существующие ID сохранены.
- SKIPPED: vision-генерация, реальное выполнение пользовательской задачи, проверки на платных моделях — квота не расходовалась.

## Docker, сеть и автозапуск

- PASS: Docker Engine 29.9.2, Compose plugin 5.6.1, rootful daemon active/enabled.
- PASS: Dockerfile собирается; SDK только в build stage; runtime не содержит SDK.
- PASS: compose config/schema/security checks; сеть host, user 1000:1000, root filesystem read-only, ALL capabilities dropped, no-new-privileges, без privileged/socket/ports.
- PASS: контейнер запущен, heartbeat healthcheck работает. Здоровье процесса не равно настройке Telegram.
- PASS: OpenCode API, SQLite и HTTPS api.telegram.org доступны из контейнера.
- PASS: таблица маршрутизации и публичный исходящий адрес совпали у хоста и контейнера. Default route хоста через существующий happ-xray; VPN не изменялся, дополнительные прокси не добавлены.
- PASS: сохранены IDs Roots/Projects/Sessions/Tasks после force-recreate, выполнен restart контейнера.
- PASS: opencode.service enabled/active; Linger=yes по отдельному разрешению пользователя; новый сервер не запущен рядом с существующим. Unit проверен systemd-analyze.
- SKIPPED: реальная перезагрузка ПК, restart всего Docker daemon и отключение VPN — не выполнялись, чтобы не прерывать другие процессы. Сбой API и сохранение очереди проверены mocks.

## Telegram и файлы

- IMPLEMENTED: команды/inline keyboards, страницы, owner-only guard, сессии, каталог моделей/агентов, очередь, статусы, логи/diff/результат, отмена, permissions, браузер, избранное/недавние, mkdir, поиск файлов, выбор/скачивание, временная загрузка, подтверждение overwrite, ZIP, изображения и вложения задач.
- PASS: env шаблон есть, рабочий `.env` исключён Git/Docker, реальный файл конфигурации 0600, секрет передаётся file mount.
- PASS: Telegram token/Owner ID настроены в файле 0600, настоящий getMe успешен. Пользователь подтвердил работу бота. Полный повтор всех действий с телефона в этой проверке не проводился.
- SKIPPED: реальная Telegram-доставка ZIP, фото и документов, интерактивный permission roundtrip; соответствующая локальная логика проверена автоматическими тестами.

## Оставшиеся ограничения

Автономный профиль установлен и активен для manager-сессий. Shell работает в ограниченной копии проекта через bubblewrap, без сети; новые зависимости вне локальных кэшей и внешние интеграции могут быть блокером. Это не полная изоляция host OpenCode и всех его нативных инструментов. Лимит циклов проверок — инструкция агенту; время/число инструментов ограничиваются кодом. Неопределённая отправка не имеет кнопки «слепо повторить», может потребовать ручной сверки через OpenCode. Фильтр секретов — эвристика, System.IO не даёт полной TOCTOU-изоляции. PDF OCR, автоматическое извлечение ZIP и fallback посреди неизвестной операции отсутствуют.

## Проверка автономного профиля

- PASS: сборка без предупреждений и ошибок, 51 тест после изменений.
- PASS: настоящий API OpenCode — активный плагин, safe read разрешён, sudo и внешний путь отклонены; отдельная временная сессия без pending permissions, без model generations.
- PASS: настоящий shell через публичный API создал проверенный файл в изолированной среде и вернул результат на диск. Сессия архивирована, временная папка удалена.
- PASS: Docker image пересобран, контейнер пересоздан; doctor из контейнера подтвердил OpenCode 2.2.5, SQLite, каталог моделей/агентов и наличие Telegram-конфигурации.
- PASS: настоящий Telegram getMe успешен. Работа разрешений на платной AI-задаче не проверялась; никаких тестовых промптов в пользовательские сессии не отправлялось.

Пределы и управление профилем описаны в [autonomy.md](autonomy.md).
