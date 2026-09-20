# Агентские команды для TIFF-пайплайна

Статус: TIFF-пайплайн реализован и проверен. JSON v1 сохраняется; новые документы агентов
всегда используют `Assets/.../*.whimtex.tiff`. Legacy `.asset` продолжает работать только для
чтения, диагностики и явной миграции; новые `.asset` создавать не следует.

## 1. Команды, необходимые для адаптации

Это минимальный набор. Для обычного редактирования агент использует TIFF-команды; legacy `.asset`
поддерживается только для чтения и миграции.

| Команда | Изменение | Backend |
| --- | --- | --- |
| `whimtex_describe` | Возвращает `storageFormats`, `backends`, `migration` и ограничения TIFF; новый `assetPath` должен быть `.tiff`. | capability discovery |
| `whimtex_document_inspect` | При `.asset` сохраняет legacy-путь. При `.tiff` открывает transient-модель и возвращает те же стабильные ID, настройки и `revision`. | read-only |
| `whimtex_batch_execute` | При `.asset` сохраняет текущую реализацию. При `.tiff` создаёт/открывает transient-модель, применяет те же операции, проверяет `expectedRevision` и сохраняет через `WhimTexDocumentBuild.Save`. | batch authoring |
| `whimtex_document_render` | При `.asset` сохраняет текущий путь. При `.tiff` открывает документ без окна, вызывает общий renderer и пишет PNG в `Temp/WhimTex`. | diagnostic |
| `whimtex_image_import` | Не менять. Это импорт исходного PNG/JPEG, а не создание WhimTex-документа. | ordinary texture |
| `whimtex_document_migrate` | Явная команда для `.asset → .tiff`: `sourcePath`, `destinationPath`, `overwrite=false`. Исходный файл, `.meta`, GUID и output не изменяются. | migration |
| `whimtex_headless_live` | Persistent transient-сессия без окна: `begin`, `status`, `preview`, `render`, `complete`, `cancel`. | headless live |

### Контракт `whimtex_batch_execute` для TIFF

Формат запроса остаётся v1:

```json
{
  "apiVersion": 1,
  "assetPath": "Assets/Art/Wall.whimtex.tiff",
  "create": true,
  "width": 1024,
  "height": 1024,
  "expectedRevision": null,
  "dryRun": false,
  "save": true,
  "operations": []
}
```

Правила:

- расширение `.tiff` выбирает `WhimTexDocumentBuild`; `.asset` допускается только для legacy-чтения и миграции;
- `create:true` создаёт transient-модель, а не `ScriptableObject` в Assets;
- `dryRun` не создаёт TIFF, не импортирует его и не меняет открытые документы;
- `save:false` оставляет изменения только в текущем запросе и не создаёт сессию редактора;
- `expectedRevision` обязателен для редактирования существующего TIFF;
- существующий `.asset` можно передать в `whimtex_batch_execute` только с `dryRun:true`; сохранение,
  создание и применение изменений к legacy-файлу возвращают `legacy_read_only`;
- запись выполняется только после полной проверки операций и через существующую атомарную транзакцию;
- после commit результат содержит путь, GUID, revision и сводку операций;
- операции `add`, `set`, `transform`, `target`, `move`, `stroke`, `compact` не дублируются —
  их применение должно быть отделено от загрузки и сохранения backend-сессии.

### Контракт `whimtex_document_migrate`

```json
{
  "apiVersion": 1,
  "sourcePath": "Assets/Legacy/Stone.asset",
  "destinationPath": "Assets/Art/Stone.whimtex.tiff",
  "overwrite": false
}
```

Команда должна использовать `CreateEditableCopy` и общий TIFF writer. Она не должна автоматически
переназначать ссылки на старый output, удалять `.asset` или менять его содержимое.

## 2. Два режима Live API

`whimtex_assistant_begin`, `whimtex_assistant_lock`, `whimtex_assistant_live` и `whimtex_assistant_sessions` остаются API подключённого
окна. Они работают с открытой сессией, резервированием слоёв и Live Update. TIFF-батч не должен
самовольно выбирать окно или переносить изменения в открытый документ.

Для независимой TIFF-сборки используется `whimtex_headless_live`. Она держит transient-модель между
запросами и не создаёт окно:

```json
{"apiVersion":1,"op":"begin","sessionId":"wall-live","assetPath":"Assets/Art/Wall.whimtex.tiff","expectedRevision":"<inspect revision>"}
{"apiVersion":1,"op":"preview","sessionId":"wall-live","requestId":"preview-1","operations":[{"op":"add","type":"color","as":"overlay","settings":{"name":"Overlay","color":[1,0.2,0.1,1]}}]}
{"apiVersion":1,"op":"render","sessionId":"wall-live","requestId":"render-1","outputPath":"Temp/WhimTex/wall-preview.png","overwrite":true}
{"apiVersion":1,"op":"complete","sessionId":"wall-live","operations":[]}
```

`preview` каждый раз строится от snapshot, захваченного в `begin`, поэтому повторный preview
идемпотентен и не накапливает операции. `complete` проверяет disk revision и выполняет одну
атомарную запись; внешнее изменение TIFF возвращает `revision_conflict`. `cancel` освобождает
transient-модели без записи. Оконный API и независимая сессия не должны одновременно менять один файл.

## 3. Команды, которых не хватало в ходе разработки

Эти команды не обязательны для первого переключения, но закрывают реальные диагностические пробелы.

| Команда | Зачем нужна | Приоритет |
| --- | --- | --- |
| `whimtex_storage_inspect` | Быстро прочитать каталог TIFF, версии блоков, размеры модели, Drawing и общий размер без материализации Unity-текстур. | высокий |
| `whimtex_document_validate` | Проверить структуру, лимиты, missing types, ссылки, HLSL и импорт. Опциональный `render:true` добавляет проверку композиции. Ничего не сохраняет. | высокий |
| `whimtex_document_status` | Для указанного пути вернуть disk revision, GUID, lock/live-состояние, dirty-состояние, import errors и наличие staged recovery. `whimtex_assistant_sessions` показывает только открытые окна. | высокий |
| `whimtex_document_compare` | Сравнить две ревизии/два файла по модели, Drawing-блокам и итоговому рендеру без попытки merge. Нужно для Git-конфликтов и долгосрочных эталонов. | средний |
| `whimtex_document_recover` | Явно проверить и восстановить staged TIFF после оборванной записи в новый destination. Не перезаписывает исходник. | средний |
| `whimtex_document_export` | Экспортировать уже загруженный результат в PNG/JPEG/TGA/EXR с явными параметрами. `whimtex_document_render` закрывает диагностический PNG-сценарий. | низкий |

### Реализованные диагностические команды

`whimtex_storage_inspect`, `whimtex_document_validate` и `whimtex_document_status` теперь доступны в Pipeline.
Первая читает только каталог контейнера и не создаёт Unity-текстуры. `validate` открывает
временную модель, проверяет лимиты, ссылки и Shader FX, а при `render:true` дополнительно
проверяет композицию. `status` не материализует модель и сообщает дисковую SHA-256 ревизию,
GUID, импорт, состояние открытого окна/live-lock и оставшиеся staged-файлы.

### Почему не нужны отдельные команды

- `whimtex_save` не нужен: сохранение без дополнительных операций уже выражается `whimtex_batch_execute`
  с `operations: []` и `save:true`.
- `whimtex_open` и `whimtex_focus` не нужны для независимого TIFF backend; выбор окна относится только
  к Live API.
- `whimtex_thumbnail` не нужен: TIFF-превью и иконки Project обрабатывает штатный Unity `TextureImporter`.
- `whimtex_compile` не нужен: HLSL FX применяются и проверяются общим renderer/save pipeline.
- Отдельный `whimtex_texture_settings` пока не нужен: настройки принадлежат штатному `TextureImporter`
  и `.meta`, а не слоистой модели.

## 4. Реализовано в первом адаптере

1. Применение JSON-операций осталось общим для обоих backend-ов.
2. `inspect`, `execute` и `render` выбирают TIFF backend по расширению.
3. Добавлены `whimtex_document_migrate`, диагностические команды, `whimtex_headless_live` и regression tests
   `TiffAgentApiSmoke`/`TiffLiveSmoke`.
4. Старые `.asset` документы оставлены читаемыми; создание и сохранение новых legacy-документов
   агентским API запрещены.

Примеры агентов используют TIFF по умолчанию. Текущие `whimtex_assistant_begin`, `whimtex_assistant_live` и `whimtex_assistant_lock` по-прежнему относятся
только к открытым окнам.
