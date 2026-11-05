# Архитектура

Worker компонует Application/Infrastructure/Telegram через DI. Domain содержит сущности и жизненный цикл. Application использует интерфейсы IOpenCodeClient/IPathPolicy/IManagerDbFactory/IFileManager. EF query API доступен через IManagerDb как прагматичный выбор без репозитория для каждой сущности; конкретный DbContext остаётся в Infrastructure.

TaskEngine — один сериализованный dispatcher. Уникальный частичный индекс активного LockKey — вторая защита от конкуренции, отдельный file lock предотвращает два процесса над одной БД. EF SaveChanges транзакционно сохраняет задачу и TaskAttempt до сетевого запроса. Сообщения Telegram и передачи имеют самостоятельные записи. Переходы после потери соединения сверяются с REST; очередь не зависит от Channel/RAM.

FileSystemBrowserService перечисляет один уровень и хранит только текущую страницу/ограниченный отсортированный набор. DirectoryNavigation хранит корни/избранное/недавние. FileManager разделяет исходные файлы и временные вложения, проверяет политику передачи и содержимое, создаёт ZIP с ограничениями и подготавливает реальные PromptInput.FileAttachment. Исходные файлы никогда не меняются при скачивании.

SQLite WAL, foreign_keys, busy_timeout 15 секунд. DbContextFactory создаёт независимый контекст на каждую операцию. Даты UTC. TaskEvent ограничен 300 значимыми событиями на попытку, токены SSE не сохраняются. Отдельный счётчик инструментов переживает обрезку событий.

Host OpenCode находится за Basic Auth на loopback, Docker manager имеет host network, ограниченные mounts и непривилегированного пользователя. SessionAutonomy проверяет активацию host-плагина и отмечает сессию метаданными до отправки промпта. Без адаптера автономная задача не отправляется. Профиль наследуется дочерними сессиями; остальные сессии OpenCode не затрагиваются.

Host-плагин разделён на integration.mjs (SDK hooks и обёртка shell), policy.mjs (решения allow/deny), sandbox.py (bubblewrap и ресурсы), workspace.py (копия проекта и возврат изменений). Shell работает без сети и host home, изменения OpenCode-конфигурации и секретов не публикуются. Конфликты с изменениями пользователя блокируют возврат. Полной атомарности нескольких файлов и защиты от гонок host-процессов нет. Нативные инструменты OpenCode остаются в процессе хоста; проверки путей — дополнительная политика, а не полная изоляция сервера. Подробности — [autonomy.md](autonomy.md).

В автономном режиме permission hook разрешает безопасные проектные операции и отклоняет неизвестные. PermissionManager также отклоняет оставшиеся неподтверждённые host-shell запросы старых задач, не разрешая их задним числом. В ручном режиме сохраняется owner approval. Native always не используется.

## Разделение исходников

- Domain: отдельные файлы сущностей, enum и правил состояний в Common/Projects/Sessions/Tasks/Permissions/Files/Navigation/Telegram.
- Application: Configuration, Projects/ProjectManager, Sessions/SessionManager, Tasks/TaskManager, Models/ModelManager, Agents/AgentManager, Navigation/NavigationService. Отправка, сверка и запись событий — TaskDispatcher, TaskReconciler, TaskEventRecorder; TaskEngine только координирует очередь.
- Infrastructure: Persistence/Configurations — отдельные EF configurations; OpenCode — клиент и SSE parser; Files — LocalFileService, FileAccessPolicy, AttachmentStorageService, ArchiveService, TaskAttachmentFactory, ImageHeaderReader. FileManager — тонкая реализация внешнего интерфейса, не набор всей файловой логики.
- Telegram: Routing проверяет авторизацию и выбирает обработчик; Handlers содержит отдельные меню проектов/сессий/задач/каталогов/файлов/загрузок; Transport отвечает за отправку и throttling, Navigation — callback registry и редактирование сообщения, Notifications — статусы/permissions, Workers — polling и фоновые публикации.
- Worker: Program — точка композиции; Configuration, Hosting, Commands и Services разделены. В Program нет JSON-отчётов, разбора секретов и бизнес-логики.

Применённые EF миграции и persisted state сохранены. Рефакторинг не требует новой миграции.
