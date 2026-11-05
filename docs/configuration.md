# Конфигурация

Настройки .NET Options читаются из защищённого env-файла, затем переменные окружения могут переопределять их. Docker secret передаёт файл как `/run/secrets/manager_env`, без интерполяции секрета в compose environment. Формат `KEY=value` без shell-команд, без кавычек, пустые значения пропускаются. Файл не исполняется.

`Telegram__Token`, `BotSecurity__OwnerUserId` обязательны для polling/dispatcher. `BotSecurity__OwnerChatId` по умолчанию равен OwnerUserId. Owner должен явно известен заранее. Группы запрещены.

`OpenCode__BaseUrl` по умолчанию http://127.0.0.1:49374. Для удалённых адресов требуется HTTPS. `OpenCode__Username=opencode`, пароль импортирован из существующей service.json без печати. Менеджер не меняет OpenCode Go, провайдеры или прокси.

`Storage__DatabasePath`, `Files__DataDirectory`, `Projects__AllowedRoots__0` указаны в Compose. Корни на хосте и в контейнере должны совпадать; при переносе пути измените одновременно source/target и Options. Не монтируйте только symlink без его конечной директории.

`TaskExecution__MaxConcurrentTasks=2`, `MaxDurationMinutes=60`, `MaxToolCalls=200`, `MaxVerificationCycles=8`, `PollSeconds=3`, `MaxEventsPerAttempt=300`. `MaxVerificationCycles` — инструкция в промпте, остальные лимиты проверяются Worker.

`Telegram__MaxAttachmentBytes=5242880`, `Telegram__StatusIntervalSeconds=5`. FileStorageOptions связывается с секцией `Files`: MaxDownloadBytes, MaxArchiveBytes, MaxArchiveFiles, MaxArchiveDepth, MaxArchiveSeconds, MaxImagePixels. Данные uploads переживают пересоздание; непривязанные вложения по умолчанию истекают через 7 дней, привязанные к задачам сохраняются для воспроизводимости. Очистка выполняется при запуске. Неуспевшие передачи становятся NeedsReconciliation и автоматически не повторяются.

Скрипт `scripts/configure-secrets.py` вводит токен скрыто, проверяет getMe и пересоздаёт контейнер. Создание бота в BotFather и личный `/start` выполняет владелец.
