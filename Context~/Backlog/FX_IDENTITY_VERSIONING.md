# F14. Идентификатор, версия и выбор источника FX

- Назначение: находить исходник FX независимо от пути и использовать его только при точном совпадении версии, иначе сохранять результат через код документа.
- Статус: выполнено. Штатная компиляция Unity и профильные проверки пройдены.
- Источники истины: [контракт](../../Documentation~/ShaderFX.md#preset-identity-and-version), [парсер](../../src/ShaderFXMetadata.cs), [разрешение источника](../../src/ShaderFX.Catalog.cs), [восстановление FX](../../src/ShaderFX.cs), [каталог](../../src/ShaderFXCatalog.cs), [includes](../../src/ShaderFXSourceBuilder.cs), [TIFF](../../src/WhimTexDocumentSerializer.cs), [JSON](../../src/WhimTexDocumentJson.cs), [экспорт пресетов](../../src/ShaderFXPresetWriter.cs).

## Согласованный контракт

```hlsl
// @whimtex-effect Color/Example
// @preset("12bc047d-f5b2-43e4-91fd-84e0c2276cde", 1)
```

Одна необязательная директива в начальном комментарии до HLSL, желательно сразу
после `@whimtex-effect Category/Name`. Если нужен `@control`, порядок экспорта:
marker → preset → control. GUID и положительная целая версия обязательны вместе.
Ненулевой GUID принимается в D/N и нормализуется в lowercase N; версия — 1…2147483647.
Пустая/частичная пара, неправильный формат, повторная или поздняя директива — ошибки.

- Без `@preset` добавляется независимая встроенная копия, включая пресеты проекта и пакетов. Unity asset GUID не используется как запасная идентичность.
- С парой связываются project/package и внешние user-library HLSL. Исходник используется только при точном совпадении ID и версии. Другой номер не означает совместимость.
- При отсутствии, ошибке, конфликте или другой версии исходника используется код документа. Ссылка сохраняется, появление подходящего рабочего исходника допускает повторное разрешение. Путь не может подменить ID.
- TIFF/JSON сохраняют `presetId`, `presetVersion` и fallback code с раскрытыми custom dependencies. Source path/hash временны. Engine includes и внешние текстуры остаются зависимостями. Удаление icon metadata из fallback отложено.
- Save Preset для нового файла создаёт новый GUID/version 1. Перезапись сохраняет ID/version назначения; если директивы нет — создаёт новую пару. Некорректная директива остаётся ошибкой. Экспорт не перепривязывает текущий FX.
- При дубликатах ID сохраняется прежний известный владелец. Без него — самый старый `CreationTimeUtc`, при равенстве нормализованный путь. Остальные доступные для записи копии получают новый ID/version 1, даже при одинаковом содержимом. Read-only конфликт диагностируется, произвольный источник молча не выбирается.
- ID в API каталога — объявленный ID, иначе путь неидентифицируемой записи. Это не идентичность экземпляра FX.

## Границы

[F18](NODE_GRAPH_DESIGN.md#модель-и-стабильные-адреса) отделяет FX instance ID
от Source ID/Version. `shaderKey` в этой задаче остаётся без изменений; значения
параметров и Active принадлежат экземпляру. Нет миграций документов, внешних
Material/Shader/ShaderFX `.asset`, диапазонов версий или режима Follow Preset.

Предложение от 2026-10-10 с независимыми ID/Version, Unity GUID fallback и несколькими
Source Policy заменено этим контрактом. Обычный FX без директивы остаётся допустимым.

## Выполнено

- Реализованы идентичность/версия, выбор источника и fallback с сохранением ссылки, экспорт и обработка дубликатов. Все 32 встроенных FX получили `@preset` с версией 1.
- TIFF/JSON сохраняют пару и portable code; схема документа перегенерирована через Unity. Fallback допускает код без catalog header; исходник каталога валидируется до привязки.
- Внешний HLSL-редактор: диагностика пары, GUID D/N, версии, единственности и начального заголовка; подсветка, completion/snippet с новым UUID; `@control` после optional `@preset`.
- Технический контракт, EN/RU/ZH workflow, AI-authoring и правила агента приведены к новой модели источников.

Проверки внешнего редактора используют существующие JS-кейсы
[metadata](../../Tests~/Cases/FX/ShaderFXVSCodeMetadata.test.mjs),
[snippets](../../Tests~/Cases/FX/ShaderFXVSCodeSnippets.test.mjs),
[grammar](../../Tests~/Cases/FX/ShaderFXVSCodeGrammar.test.mjs) и
[external code](../../Tests~/Cases/FX/ShaderFXExternalCode.test.mjs).
JS-проверки пройдены; они не заменяют проверки Unity.

## Проверено в Unity

- `shader-fx-catalog-v2` — 498 проверок, `shader-fx-tiff-catalog-v2` — 13, PASS. `shader-fx-diagnostics` и `shared-preset-libraries-v2` — PASS после исправления fallback без catalog header.
- `shader-fx-document-dirty-v2` — 109 проверок, PASS. Финальная штатная компиляция Unity — без ошибок; повторный TIFF round-trip — PASS.
- Ручной eval: дубликаты ID между версиями, сохранение известного владельца, read-only конфликт и восстановление после его устранения — PASS.
- Ручной JSON eval: точная версия, fallback при другой версии, восстановление источника; Active и `shaderKey` сохранены — PASS.

Кейсы и условия запуска — в [каталоге тестов](../../Tests~/scripts/test-catalog.json) и [test runner](../../Tests~/RUNNING_TESTS.md).

Прежние отдельные сбои `shader-fx0125-preset-v2` (устаревший float2 entry point)
и `distortion-controls-v2` (неполная таблица пресетов) оставлены вне этого scope.

## Связи

- [F13](FX_CUSTOM_EDITORS.md): Source ID может связывать пользовательский редактор с FX; версия и параметры определяют пригодность редактора.
- [F09](WHIMTEX_IMPORTER.md), [F06](ANIMATION_DESIGN.md), [F10](TEXTURE_SETS.md): импорт и Bake используют согласованную версию источника, без обновления посреди расчёта.

Карточка не разрешает изменение пользовательских документов/ассетов, commit/push или релиз.
