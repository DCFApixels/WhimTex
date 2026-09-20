# Агентские команды для TIFF-пайплайна

Статус: контракт перед адаптацией. Команды ниже пока не переключают `WhimTexApi` и не меняют
существующий API `.asset`. Цель — сохранить JSON v1 и выбирать backend по расширению `assetPath`.

## 1. Команды, необходимые для адаптации

Это минимальный набор. Новые имена для обычного редактирования не нужны: существующие команды
должны принимать и legacy `.asset`, и WhimTex TIFF.

| Команда | Изменение | Backend |
| --- | --- | --- |
| `whimtex_describe` | Добавить `storageFormats`, `backends`, `migration` и ограничения TIFF. Описать, что `assetPath` может быть `.asset` или `.tiff`. | capability discovery |
| `whimtex_inspect` | При `.asset` оставить текущий путь. При `.tiff` открыть через `WhimTexDocumentBuild.Open`, вернуть те же стабильные ID, настройки и `revision`. | read-only |
| `whimtex_execute` | При `.asset` оставить текущую реализацию. При `.tiff` создать/открыть transient-модель, применить те же операции, проверить `expectedRevision`, затем атомарно сохранить через `WhimTexDocumentBuild.Save`. | independent build |
| `whimtex_render` | При `.asset` оставить текущий путь. При `.tiff` открыть build-сессию, вызвать `Render`, записать обычный PNG в `Temp/WhimTex`. | independent build |
| `whimtex_import_image` | Не менять. Это импорт исходного PNG/JPEG, а не создание WhimTex-документа. | ordinary texture |
| `whimtex_migrate` | Новая явная команда для `.asset → .tiff`: `sourcePath`, `destinationPath`, `overwrite=false`. Исходный файл, `.meta`, GUID и output не изменяются. | migration |

### Контракт `whimtex_execute` для TIFF

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

- расширение `.tiff` выбирает `WhimTexDocumentBuild`, `.asset` выбирает старый backend;
- `create:true` создаёт transient-модель, а не `ScriptableObject` в Assets;
- `dryRun` не создаёт TIFF, не импортирует его и не меняет открытые документы;
- `save:false` оставляет изменения только в текущем запросе и не создаёт сессию редактора;
- `expectedRevision` обязателен для редактирования существующего TIFF;
- запись выполняется только после полной проверки операций и через существующую атомарную транзакцию;
- после commit результат содержит путь, GUID, revision и сводку операций;
- операции `add`, `set`, `transform`, `target`, `move`, `stroke`, `compact` не дублируются —
  их применение должно быть отделено от загрузки и сохранения backend-сессии.

### Контракт `whimtex_migrate`

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

## 2. Live API не смешивать с независимой сборкой

`whimtex_begin`, `whimtex_lock`, `whimtex_live` и `whimtex_sessions` остаются API подключённого
окна. Они работают с открытой сессией, резервированием слоёв и Live Update. TIFF-батч не должен
самовольно выбирать окно или переносить изменения в открытый документ.

Для открытого TIFF нужно вернуть конфликт, если его уже редактирует другое окно или live-сессия.
Для независимой сборки используются `WhimTexDocumentBuild` и проверка disk revision.

## 3. Команды, которых не хватало в ходе разработки

Эти команды не обязательны для первого переключения, но закрывают реальные диагностические пробелы.

| Команда | Зачем нужна | Приоритет |
| --- | --- | --- |
| `whimtex_inspect_storage` | Быстро прочитать каталог TIFF, версии блоков, размеры модели, Drawing и общий размер без материализации Unity-текстур. | высокий |
| `whimtex_validate` | Проверить структуру, лимиты, missing types, ссылки, HLSL и импорт. Опциональный `render:true` добавляет проверку композиции. Ничего не сохраняет. | высокий |
| `whimtex_status` | Для указанного пути вернуть disk revision, GUID, lock/live-состояние, dirty-состояние, import errors и наличие staged recovery. `whimtex_sessions` показывает только открытые окна. | высокий |
| `whimtex_compare` | Сравнить две ревизии/два файла по модели, Drawing-блокам и итоговому рендеру без попытки merge. Нужно для Git-конфликтов и долгосрочных эталонов. | средний |
| `whimtex_recover` | Явно проверить и восстановить staged TIFF после оборванной записи в новый destination. Не перезаписывает исходник. | средний |
| `whimtex_export` | Экспортировать уже загруженный результат в PNG/JPEG/TGA/EXR с явными параметрами. Сейчас `whimtex_render` закрывает только диагностический PNG-сценарий. | низкий |

### Почему не нужны отдельные команды

- `whimtex_save` не нужен: сохранение без дополнительных операций уже выражается `whimtex_execute`
  с `operations: []` и `save:true`.
- `whimtex_open` и `whimtex_focus` не нужны для независимого TIFF backend; выбор окна относится только
  к Live API.
- `whimtex_thumbnail` не нужен: TIFF-превью и иконки Project обрабатывает штатный Unity `TextureImporter`.
- `whimtex_compile` не нужен: HLSL FX применяются и проверяются общим renderer/save pipeline.
- Отдельный `whimtex_texture_settings` пока не нужен: настройки принадлежат штатному `TextureImporter`
  и `.meta`, а не слоистой модели.

## 4. Предлагаемый порядок реализации

1. Вынести применение JSON-операций из текущего asset lifecycle в общий backend-neutral слой.
2. Добавить чтение/сохранение TIFF в `whimtex_inspect`, `whimtex_execute` и `whimtex_render` по расширению.
3. Добавить `whimtex_migrate` и regression tests для `.asset → TIFF`.
4. Добавить `whimtex_inspect_storage` и `whimtex_validate` до переключения документации агентов.
5. Обновить `whimtex_describe`, Agent API и примеры; старые `.asset` команды оставить совместимыми.
6. После отдельного периода проверки объявить создание `.asset` устаревшим, не удаляя чтение и миграцию.
