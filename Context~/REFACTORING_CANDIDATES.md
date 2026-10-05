# Кандидаты на рефакторинг

- Назначение: сохранить только предложения, уменьшающие обязательный контекст работы агента.
- Статус: R05/R09 — предложения, не поручение; актуальность сверена по коду 2026-10-05.
- Источники истины: файлы по ссылкам ниже; ограничения из [AGENTS.md](../AGENTS.md).

## R05. Общие batch-правила → один исполнитель

Проблема: aliases, порядок операций, ответы и итоговые проверки повторяются между API-входами.
Решение: расширить существующий `RunCommonOperations`, сохранив отдельные транзакции.
Польза: изменение общей валидации или диагностики требует сверки одного алгоритма и обвязок.
Dispatch `ApplyOperation` уже общий: новая операция не требует трёх отдельных обработчиков.

### Где и что изменить

- [WhimTexApi.cs](../src/Automation/WhimTexApi.cs): probe/apply и разные бюджетные проверки.
- [WhimTexApi.AssistantBatch.cs](../src/Automation/WhimTexApi.AssistantBatch.cs): `RunCommonOperations`.
- [WhimTexApi.TiffLive.cs](../src/Automation/WhimTexApi.TiffLive.cs): `ReplayTiffLiveRequest`.
- [WhimTexApi.Layers.cs](../src/Automation/WhimTexApi.Layers.cs): общий `ApplyOperation`.

Один исполнитель может владеть aliases, порядком, индексом операции, сбором ответа и общими
target/budget-проверками с явным document path. Сохранить сведения о фазе ошибки и частичный результат.
Probe остаётся `execute:false`; копирование, Undo/rollback, Save и save failure — у конкретного workflow.
Assistant обновляет transform hierarchy после каждой операции: это отличие сначала проверить на
зависимых операциях, не убрать механически. TIFF replay сохраняет baseline и requestId/retry;
working model заменяется только после успешной подготовки. Повтор запроса не повторяет adds/strokes.

Не строить общий транзакционный framework, не объединять оконную и независимую TIFF-сессии.
Если извлечение требует множества callbacks/options, оно не упрощает работу агента.

### Проверки

`agent-editing-v2`, `agent-resize-v2`, `tiff-live-v2`, `live-agent-unity-v2`:
aliases, зависимые операции, ошибка в середине, dry-run, rollback, requestId и save failure.
Различия жизненных циклов должны остаться явными в тестах.

## R09. Временное render state → небольшой scope

Проблема: несколько путей вручную сохраняют и восстанавливают поля композиции/GPU state в `finally`.
Решение: маленький scope для повторяющегося контракта восстановления, без allocations на горячем пути.
Польза: новый изолированный путь можно сверить с одним списком временного состояния.
Текущие `finally` уже восстанавливают его; это не диагноз нового дефекта.

### Где и что изменить

- [RenderCanvasWithCache](../src/TextureCompositor.EffectCache.cs): cache, quality, `publishingLayerPreview`.
- [RenderLayerThumbnail](../src/TextureCompositor.Thumbnails.cs), [PickLayerAtPixel](../src/TextureCompositor.LayerPicking.cs): cache, quality, diagnostics, `RenderTexture.active`.
- [RasterizeFXPrefix](../src/TextureCompositor.ApplyFX.cs): свой save/restore cache и quality.
- [RenderCanvasCore](../src/TextureCompositor.cs): отдельное владение созданным cache и temporary output.

Подключать только подходящие entry points, не унифицировать их настройки. `lastLayerPreview*` —
опубликованный результат успешного рендера, не временные поля вроде `publishingLayerPreview`.
Scope восстанавливает state, но не уничтожает borrowed cache или возвращённую текстуру.
Владение temporary/persistent ресурсами остаётся у caller. Nested calls и исключения проверяются отдельно.

Не вводить универсальный `RenderRequest`, новый `CanvasRenderer` или переписывание pipeline:
их самостоятельная польза агенту не доказана.

### Проверки

`render-state-v2`, `layer-preview-reuse-v2`, `effect-cache-smoke-v2`, `canvas-render-naming-unity-v2`:
исключения/nested state, clipping/group/Target, picking/thumbnail/export. Миниатюры сохраняют
отключённые transform/clipping и возможность показывать hidden layer.

## Общие ограничения

Запуск — через [RUNNING_TESTS.md](../Tests~/RUNNING_TESTS.md), после чтения выбранных исходников
и проверки разрешений. Сохранить файлы 0.12.5, frozen fixtures, Compact defaults v1, Undo,
source resolution, группы, Target, clipping, appearance и texture ownership.
Не объединять разные revision/save fingerprint/render stamp или разные контексты настроек кисти.
Временный SO модели не является снятым document `.asset` форматом.

R01–R04 завершены; R14 завершён переносом тестов на общий API/каталог и удалением Legacy.
Это не основания повторять работы или считать старый PASS проверкой будущего изменения.
Предпочтительный порядок при новом запросе — R05, затем ограниченный R09.

## История

[Исходный аудит](https://github.com/DCFApixels/WhimTex/blob/fc4afbf765e3b7734c3fbf0baab77701367b3f02/Context~/REFACTORING_AUDIT_2026-10-04.ru.md)
содержит обоснования завершённых R01–R04/R14 и отклонённых R06–R08/R10–R13/R15.
Они сохранены в Git, а не в действующем плане.
