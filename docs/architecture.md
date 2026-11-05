# Архитектура

Worker компонует Application/Infrastructure/Telegram через DI. Domain содержит сущности и жизненный цикл. Application использует интерфейсы IOpenCodeClient/IPathPolicy/IManagerDbFactory/IFileManager. EF query API доступен через IManagerDb как прагматичный выбор без репозитория для каждой сущности; конкретный DbContext остаётся в Infrastructure.

TaskEngine — один сериализованный dispatcher. Уникальный частичный индекс активного LockKey — вторая защита от конкуренции, отдельный file lock предотвращает два процесса над одной БД. EF SaveChanges транзакционно сохраняет задачу и TaskAttempt до сетевого запроса. Сообщения Telegram и передачи имеют самостоятельные записи. Переходы после потери соединения сверяются с REST; очередь не зависит от Channel/RAM.

FileSystemBrowserService перечисляет один уровень и хранит только текущую страницу/ограниченный отсортированный набор. DirectoryNavigation хранит корни/избранное/недавние. FileManager разделяет исходные файлы и временные вложения, проверяет политику передачи и содержимое, создаёт ZIP с ограничениями и подготавливает реальные PromptInput.FileAttachment. Исходные файлы никогда не меняются при скачивании.

SQLite WAL, foreign_keys, busy_timeout 15 секунд. DbContextFactory создаёт независимый контекст на каждую операцию. Даты UTC. TaskEvent ограничен 300 значимыми событиями на попытку, токены SSE не сохраняются. Отдельный счётчик инструментов переживает обрезку событий.

Host OpenCode находится за Basic Auth на loopback, Docker manager имеет host network, но ограниченные mounts и непривилегированного пользователя. Граница container filesystem не ограничивает инструменты host OpenCode. Полный shell sandbox и git-worktree изоляция выполнения — отдельное невыполненное расширение. Глобальный конфиг OpenCode не ослабляется.

Автоподтверждение разрешений ограничено безопасными file read/edit. Для shell требуется владелец. Ограниченное правило разрешает точные проверенные файлы в текущей сессии; native always не используется, так как save patterns могут быть шире пользовательского выбора.

## Разделение исходников

- Domain: отдельные файлы сущностей, enum и правил состояний в Common/Projects/Sessions/Tasks/Permissions/Files/Navigation/Telegram.
- Application: Configuration, Projects/ProjectManager, Sessions/SessionManager, Tasks/TaskManager, Models/ModelManager, Agents/AgentManager, Navigation/NavigationService. Отправка, сверка и запись событий — TaskDispatcher, TaskReconciler, TaskEventRecorder; TaskEngine только координирует очередь.
- Infrastructure: Persistence/Configurations — отдельные EF configurations; OpenCode — клиент и SSE parser; Files — LocalFileService, FileAccessPolicy, AttachmentStorageService, ArchiveService, TaskAttachmentFactory, ImageHeaderReader. FileManager — тонкая реализация внешнего интерфейса, не набор всей файловой логики.
- Telegram: Routing проверяет авторизацию и выбирает обработчик; Handlers содержит отдельные меню проектов/сессий/задач/каталогов/файлов/загрузок; Transport отвечает за отправку и throttling, Navigation — callback registry и редактирование сообщения, Notifications — статусы/permissions, Workers — polling и фоновые публикации.
- Worker: Program — точка композиции; Configuration, Hosting, Commands и Services разделены. В Program нет JSON-отчётов, разбора секретов и бизнес-логики.

Применённые EF миграции и persisted state сохранены. Рефакторинг не требует новой миграции.
