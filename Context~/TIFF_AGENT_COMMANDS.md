# Агентские команды для TIFF-пайплайна

- Назначение: краткая карта команд и различий Batch, Headless Live и Assistant.
- Статус: действующая памятка, не отдельная версия протокола.
- Источники истины: [AgentAPI](../Documentation~/AgentAPI.md), [LiveAgentAPI](../Documentation~/LiveAgentAPI.md), [WhimTexCommands](../src/Automation/Pipeline/WhimTexCommands.cs).

Дополнительно поддержан `whimtex.document` (`.json`). Команда `whimtex_document_json`
сериализует, проверяет, экспортирует, открывает и записывает документ, вставляет фрагмент или заменяет
содержимое слоя. Batch также читает/сохраняет JSON. Headless Live пока остаётся TIFF-пайплайном.
Контракт: [JSON_FORMAT](../Documentation~/JSON_FORMAT.md).

TIFF — основной формат документа. Документы `.asset` и прежние версии TIFF не поддерживаются; для них нужен соответствующий старый checkout.
Это карта команд, а не отдельная версия протокола. Полный контракт и лимиты находятся в
[AgentAPI](../Documentation~/AgentAPI.md) и [LiveAgentAPI](../Documentation~/LiveAgentAPI.md).

## Три режима редактирования

| Режим | Команды | Состояние и сохранение |
| --- | --- | --- |
| Batch | `whimtex_batch_execute` | Независимая временная модель из TIFF или JSON. После запроса уничтожается, даже с `save:false`. `save:true` сохраняет файл в его формате, но не изменения открытого окна. JSON не хранит пиксели Drawing. Пользовательского Undo файла нет. |
| Headless Live | `whimtex_headless_live` | Модель между запросами без окна. `begin`, `list`, `status`, `preview`, `render`, `complete`, `cancel`. Сохраняет только `complete`; активная сессия теряется при domain reload. |
| Assistant | `whimtex_assistant_sessions`, `whimtex_assistant_begin`, `whimtex_assistant_lock`, `whimtex_assistant_live`, `whimtex_assistant_execute` | Открытый документ, Undo, резервирование/блокировки для длительных задач или немедленный batch без pending jobs. Автосохранения нет; сохранение через окно. |

Общие операции: `add`, `set`, `transform`, `target`, `move`, `resize`, `stroke`, `compact`, `fx`,
`delete`, `duplicate`, `merge`, `convertToDrawing`, `blurStroke`, `smudgeStroke`, `healStroke`.
Они доступны в Batch, Headless `operations` и Assistant Execute. Формат `fx.edits`
общего batch отличается от `changes.fx` оконной резервации; примеры нельзя смешивать.
В резервации и lock-job новые FX принимают только `code` с `@param` и его defaults,
без `parameters`/`presetId`/`set`. Изменение значений и добавление связанного пресета —
общие операции после завершения pending jobs.

## Batch

Создание:

```json
{
  "apiVersion": 1,
  "assetPath": "Assets/Art/Wall.tiff",
  "create": true,
  "width": 1024,
  "height": 1024,
  "save": true,
  "operations": []
}
```

Для существующего TIFF или JSON сначала `whimtex_document_inspect`, затем его `document.revision`
в `expectedRevision`. При создании поле `expectedRevision` отсутствует, а не равно `null`.
`dryRun:true` проверяет операции на копии без записи; это не проверка GPU/нового HLSL/диска.
Пути документов `.asset` отвергаются с `invalid_path`, в том числе для чтения и dry-run.

`save:false` не сохраняет рабочую сессию и не меняет окно: для этого нужны Headless или Assistant.
Пустой batch с `save:true` пересохраняет дисковый документ; это не способ сохранить изменения
Assistant или восстановить потерянный кандидат после ошибки. После `saveMayBePartial:true`
проверить документ на диске и staged-файлы: ошибка могла произойти до либо после commit.
Повторять только подтверждённо отсутствующие правки с новой ревизией.

## Headless Live

Отдельные JSON-запросы:

```json
{"apiVersion":1,"op":"begin","sessionId":"wall-live","assetPath":"Assets/Art/Wall.tiff","expectedRevision":"<inspect revision>"}
{"apiVersion":1,"op":"preview","sessionId":"wall-live","requestId":"preview-1","operations":[{"op":"add","type":"color","as":"overlay","settings":{"name":"Overlay","color":[1,0.2,0.1,1]}}]}
{"apiVersion":1,"op":"render","sessionId":"wall-live","outputPath":"Temp/WhimTex/wall-preview.png","overwrite":true}
{"apiVersion":1,"op":"complete","sessionId":"wall-live","operations":[]}
```

Непустые `operations` пересобирают кандидат от исходного `begin`, а не дополняют прошлый preview.
Отсутствующие или пустые операции оставляют текущего кандидата. `render` с операциями тоже меняет
кандидат. Для созданных в списке слоёв использовать `@aliases`, не ID из предыдущего preview.

`begin` не идемпотентен: после таймаута искать сессию через `list`/`status`. У preview кешируется
только последний непустой запрос с `requestId`; тот же ID требует тех же аргументов.
`complete` проверяет исходную дисковую ревизию; конфликт оставляет сессию открытой.
`cancel` освобождает модели без сохранения. Одновременно менять файл из окна и Headless нельзя.
Активные модели не переживают reload; последние 32 успешных ответа `complete`/`cancel` хранятся
для повторного получения результата, но не восстановления активной сессии.

## Каталог, диагностика и восстановление

Все перечисленные команды реализованы:

| Команда | Назначение |
| --- | --- |
| `whimtex_describe` | Поддерживаемые операции, режимы, типы, значения по умолчанию и лимиты. |
| `whimtex_document_inspect` | Дисковый документ: ID, настройки, FX, локальные трансформации слоёв и групп, ревизия. |
| `whimtex_document_render` | PNG композиции в `Temp/WhimTex`; не сохранение документа. |
| `whimtex_fx_catalog` | Пресеты из Assets, Packages и пользовательской библиотеки; `presetId` раскрывает параметры. |
| `whimtex_fx_compile` | Компиляция пресета или raw HLSL без правки документа, с предупреждениями и ошибками. |
| `whimtex_render_probe` | Композит, слой, вход/выход FX и каналы для дискового, Assistant или Headless документа. |
| `whimtex_storage_inspect` | Каталог TIFF-блоков без материализации Drawing. |
| `whimtex_document_validate` | Проверка модели, ссылок, лимитов и Shader FX; опционально рендер. |
| `whimtex_document_status` | Дисковая SHA-256 ревизия, импорт, открытый документ, блокировки и staged-файлы. Эта ревизия не заменяет `document.revision`. |
| `whimtex_document_compare` | Сравнение двух документов: модель, блоки, опционально пиксели; без merge. |
| `whimtex_document_recover` | Проверка staged TIFF и восстановление в новый destination без перезаписи исходника. |
| `whimtex_document_export` | Плоский PNG/JPEG/TGA/EXR в `Temp/WhimTex`. |
| `whimtex_image_import` | Копирование локального PNG/JPEG в новый asset, не создание документа. |


Настройки импорта принадлежат штатному `TextureImporter` и `.meta`, не модели документа.
Ни экспорт, ни диагностический PNG, ни GPU Live Update не доказывают сохранение редактируемого TIFF.

## Проверки

`agent-documentation-v2` сверяет эту карту с командами и разбирает JSON-примеры.
Сохранение/Undo/рендер проверяются отдельно: `agent-api-v2`, `tiff-agent-api-v2`,
`tiff-live-v2`, `live-agent-unity-v2`. Использовать [общий runner](../Tests~/RUNNING_TESTS.md);
source check не доказывает runtime-поведение.
