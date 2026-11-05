# OpenCode Telegram Manager

Telegram-интерфейс для локального OpenCode v2: проекты, сессии, модели и агенты, долговременная очередь задач, статусы, файлы и вложения. .NET 10, EF Core + SQLite, Telegram.Bot. OpenCode и VPN работают на хосте, менеджер — в Docker с сетью хоста.

## Конфигурация

Все секреты и параметры конкретного компьютера задаются в `.env`. Файл исключён из Git и Docker image. В текущей установке `.env` — ссылка на защищённый файл вне репозитория; существующие настройки сохранены.

Для новой установки:

```bash
cp .env.example .env
chmod 600 .env
```

Заполните файл редактором:

- `Telegram__Token`, `BotSecurity__OwnerUserId` — токен BotFather и числовой ID владельца. При необходимости `BotSecurity__OwnerChatId`.
- `OpenCode__BaseUrl`, `OpenCode__Username`, `OpenCode__Password` — существующий локальный сервер OpenCode.
- `MANAGER_PROJECTS_ROOT` — существующая разрешённая директория проектов, абсолютный путь.
- `MANAGER_DATA_DIR` — постоянная директория SQLite и вложений, абсолютный путь; создайте её перед запуском.
- `MANAGER_UID`, `MANAGER_GID` — UID/GID владельца файлов (`id -u`, `id -g`).

Используйте простой формат `KEY=value`, без кавычек и shell-команд. Не добавляйте реальные значения в `.env.example`. Compose передаёт env-файл приложению через read-only secret mount; токен и пароль не входят в compose environment. После изменения настроек выполните `docker compose up -d --force-recreate`.

## Запуск

Нужны Docker Engine, Compose и существующий OpenCode v2 на loopback. Для автономного режима один раз установите host-плагин (Python 3, bubblewrap):

```bash
python3 scripts/install-autonomy.py
docker compose up -d --build
```

Менеджер проверяет активацию плагина перед отправкой автономной задачи. Подробности и ограничения: [автономный режим](docs/autonomy.md).

```bash
docker compose up -d
docker compose down
docker compose restart
docker compose logs -f
docker compose ps
docker compose exec opencode-manager dotnet OpenCodeManager.Worker.dll --doctor
```

Compose использует `restart: unless-stopped`; для старта после включения ПК должен запускаться Docker daemon. Контейнер работает без root, с read-only root filesystem и без capabilities/Docker socket. Корень проектов и хранилище монтируются с одинаковыми абсолютными путями на хосте и в контейнере. SQLite (`manager.db`), uploads и downloads сохраняются в `MANAGER_DATA_DIR` при пересоздании контейнера.

## Использование

Откройте личный чат бота и `/start`. Доступ разрешён только указанному владельцу, private chat и проверенному Chat ID; группы и посторонние пользователи игнорируются.

| Команда | Назначение |
|---|---|
| `/start`, `/help` | Меню и справка |
| `/status` | Состояние API, SQLite и очереди |
| `/projects`, `/sessions` | Выбор проекта и сессии |
| `/tasks` | Очередь, результаты, логи, diff и остановка |
| `/models`, `/agents` | Выбор модели и агента |
| `/files` | Файловый менеджер |
| `/cancel` | Сброс ввода |

Файловый менеджер позволяет переходить по разрешённым каталогам, создавать папки, скачивать файлы и ZIP, сохранять загрузки и добавлять вложения к задачам. Загрузка сначала попадает во временное хранилище; перезапись требует отдельного подтверждения. Для оригинала изображения отправляйте его как документ. ZIP автоматически не распаковывается. Фильтрация секретных файлов — эвристика.

SQLite хранит очередь и попытки до отправки промпта. Неопределённый POST не повторяется автоматически: задача сверяется с историей OpenCode. Одновременно запускается не больше заданного числа задач и одной изменяющей задачи на Git-дерево.

## Разработка

`src/`: Domain — сущности; Application — бизнес-логика и интерфейсы; Infrastructure — API, SQLite, System.IO; Telegram — транспорт и меню; Worker — запуск, конфигурация и фоновые сервисы. `deploy/autonomy/` содержит только исполняемые модули host-плагина, `tests/` — все проверки.

```bash
dotnet restore
dotnet build
dotnet test
python3 tests/deployment/test_compose.py
dotnet tool restore
dotnet ef migrations add Name --project src/OpenCodeManager.Infrastructure --output-dir Persistence/Migrations
```

Миграции применяются при старте. Для локального запуска задайте `Manager__EnvironmentFile` в окружении процесса: `.env` автоматически читает только Compose и установщик host-плагина.

```bash
Manager__EnvironmentFile="$PWD/.env" dotnet run --project src/OpenCodeManager.Worker
```

Тесты используют mocks и временные директории, без платных запросов к моделям. Для тестов автономного режима нужны Node.js, Python 3, bubblewrap и поддержка Linux user namespaces.
