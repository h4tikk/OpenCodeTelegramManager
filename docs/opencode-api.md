# OpenCode v2 API

Реально установлена и проверена версия 2.2.5. Источники: https://opencode.ai/v2/docs/api и https://opencode.ai/v2/openapi.json. DTO реализуют используемую часть публичного контракта; неизвестные поля могут игнорироваться. V1 не поддерживается автоматически.

| Операция | Маршрут |
|---|---|
| Информация | GET /api/info |
| Проекты | GET /api/project (массив, без data wrapper) |
| Модели, провайдеры, агенты | GET /api/model, /api/provider, /api/agent (data wrapper) |
| Сессии, пагинация | GET /api/session?limit=100&cursor=... |
| Сессии каталога | GET /api/session?directory=... |
| Сессия | GET /api/session/{sessionID} |
| Создание | POST /api/session: id, title, location.directory, permissions |
| Переименование/архив/permissions | PATCH /api/session/{sessionID} |
| Выбор модели/агента | POST /api/session/{sessionID}/model, /agent |
| Отправка | POST /api/session/{sessionID}/prompt: id, text, metadata, files |
| История | GET /api/session/{sessionID}/message с cursor |
| Входящие | GET /api/session/{sessionID}/inbox |
| Отмена принятого, но не запущенного ввода | DELETE /api/session/{sessionID}/inbox/{inboxID} |
| Остановка | POST /api/session/{sessionID}/interrupt |
| Разрешения | GET /api/session/{sessionID}/permission |
| Ответ | POST /api/session/{sessionID}/permission/{requestID}/reply: decision=once/reject |
| Diff | GET /api/session/{sessionID}/diff?from={messageID} |
| SSE | GET /api/event |

Location для model/agent — deepObject query `location[directory]`. Model.Ref = id + providerID, не v1 modelID. Session parentID нативно связывает с директорией родителя; здесь не передаётся при создании независимой сессии.

Авторизация Basic username opencode + существующий пароль сервера. При startup проверяется major 2. HTTP ошибки разделяются: 401 auth, 403 forbidden, 404 missing, 409 conflict, 429 limit, 5xx server. GET 429/5xx имеет ограниченный retry, POST не повторяется. Фоновые циклы повторяют безопасную синхронизацию после сетевых ошибок.

SSE v2.2.5 использует `data: {type,data,...}`. Транспортный контракт volatile: пропущенные события восстанавливаются сверкой REST, не вымышленным cursor глобального SSE. Экспериментальный долговременный session log сознательно не является зависимостью MVP.

Image admission проверен на временной сессии настоящего сервера: `files:[{uri:"data:image/png;base64,...",name:"fixture.png"}]`, `resume:false`, затем DELETE inbox, без AI-генерации. Первый тест с повреждённой PNG корректно получил HTTP 400, заменён валидным fixture. Это подтверждает принятие файла API, но не результат vision-генерации: такая проверка требует платного запроса и не проводилась.
