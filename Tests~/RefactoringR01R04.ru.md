# WhimTex: выполненный рефакторинг R01–R04

Дата: 4 октября 2026 года. Изменения локальные; commit/push и повышение версии не выполнялись.
Основание — [аудит](../Context~/REFACTORING_AUDIT_2026-10-04.ru.md). R05–R15 не реализовывались.

## Что изменено и зачем

### R01. Старое состояние каналов → одна актуальная маска

Удалены `ViewState.channel` и конвертация старого выбора. Новое состояние начинается с
`channelMask = 15` (RGBA); конструктор оставляет только четыре бита. Маска 0 не заменяется default.
Это состояние окна, а не документа: миграция старых layouts не нужна.

Файл: [LayerPreviewPanel.cs](../src/Editor/LayerPreviewPanel.cs).
Внешний вид, кнопки каналов, высота и жизненный цикл панели не менялись.
Skill `ui-uitk` использован для сохранения существующего UI-контракта без изменения USS/UXML.

### R02. Два Gaussian kernel → один расчёт и единый upload

Расчёт Gaussian Blur и Gaussian-ветки Sharpen перенесён в
[GaussianKernel.cs](../src/Layers/GaussianKernel.cs). Сохранены прежние формулы, double-промежуточные
вычисления, диапазон support и нормализация. Вместо двух рабочих массивов используется один постоянный;
новых массивов или Material на вызов не добавлено.

Общий `Upload` требует ровно 128 Vector4, отдельно передаёт активный pairCount и отвергает короткий
буфер до присваивания material. Это защищает ёмкость shader array от первого короткого upload.

Blur Brush тоже использует общий upload, но сохраняет собственный алгоритм: center weight 0.2,
пять прежних весов 0.12/0.10/0.08/0.06/0.04 и прежние смещения. Gaussian вместо кисти не подставлялся.

Подключения: [Gaussian Blur](../src/Layers/GaussianBlurRenderer.cs),
[Sharpen](../src/Layers/SharpenRenderer.cs), [Blur Brush](../src/Layers/DrawingLayerBehaviour.BlurBrush.cs).

### R03. Два растровых encoder → общий кодировщик

Добавлен [WhimTexRasterEncoder.cs](../src/WhimTexRasterEncoder.cs):
`Texture2D + RasterImageFormat + JPEG quality + EXR flags → bytes`.

- PNG/JPEG/TGA используют прежний HDR → LDR и цветовой контракт.
- JPEG сохраняет белый фон под alpha; default quality — 95.
- EXR остаётся HDR и использует заданные flags; default — CompressZIP.
- Входная Texture2D заимствована. Промежуточная LDR-текстура уничтожается encoder в finally.
- PSD/JSON/Unity texture Asset не входят в растровый enum.
- Окно сохраняет свои диалоги/пути; API — ограничение Temp/WhimTex и правила overwrite.

[Оконный экспорт](../src/TextureCompositorWindow.Export.cs) оставляет небольшой адаптер своего общего
списка форматов к растровому enum. [API](../src/Automation/WhimTexApi.Export.cs) вызывает тот же encoder.
Это не alias старого API, а граница между пользовательским workflow и кодированием изображения.

### R04. Глобальные Last*-списки → диагностика конкретного чтения

[Deserialize](../src/WhimTexDocumentSerializer.cs) теперь возвращает `ModelReadResult`:
`Model`, `SkippedFields`, `MissingTypes`, `UnresolvedReferences`.

Списки и dedup-наборы принадлежат экземпляру Reader; результат содержит независимые read-only снимки.
Глобальные `LastSkippedFields`, `LastMissingTypes`, `LastUnresolvedReferences` удалены.
Успешное, ошибочное или вложенное чтение не перезаписывает диагностику другого вызова.
Общий кеш разрешённых CLR-типов остался метаинформацией, не диагностикой.

[Файловый Load и CreateEditableCopy](../src/WhimTexDocumentFile.cs) читают результат своей операции.
Тексты предупреждений и блокировка потенциально lossy Save/copy сохранены.
При ошибке reader по-прежнему освобождает созданные Unity objects.
Все найденные тестовые потребители Deserialize/Last* обновлены; тесты не удалялись.

Это не обещание фоновой загрузки Unity assets: обращения к Unity objects/AssetDatabase по-прежнему
требуют поддерживаемого Editor-контекста. Проверены независимость последовательных результатов и
вложенное чтение, не многопоточная работа AssetDatabase.

## Что не менялось

Бинарный writer (включая промежуточные определения до Reader) сравнен с локальным состоянием
перед этой правкой — исходник совпадает. FormatVersion, tags, сериализуемые имена модели и
compact defaults v1 не менялись. Frozen fixtures 0.12.5 не редактировались; их хеши подтверждены.
Новые Unity internal reflection, platform-specific ветки, зависимости и настройки проекта не добавлены.

## Проверки

Проверки выполнены в подключённом Unity Editor проекта `D:/DCFA/Projects/Test6.6`,
Unity 6000.7.0a6, Linear/D3D12. Skill `unity-cli` использован для штатной компиляции и запусков.
Перекомпиляция: completed, failed=false, errors=[]; финально Editor не компилирует и не в Play mode.
Auto Refresh временно приостанавливался на пакет правок и восстановлен.

Итог: **31/31 профильных Unity-сценариев и 11/11 Node-файлов прошли, без SKIP.**
Полный исторический набор из 231 Unity entry point и всех Node-файлов здесь не перепрогонялся.

Новые проверки: [RefactoringR01R04Smoke.cs](RefactoringR01R04Smoke.cs).

| Entry point | Результат |
| --- | --- |
| Run | 2382 проверки: default, все 16 масок и sanitization, state roundtrip, точная kernel-формула, короткий/полный upload, encoder bytes/options/ownership, независимые immutable diagnostics, nested read и cleanup после ошибки |
| SharpenMaterialOrder | 3309: cold Brush → Sharpen, HDR-пиксели идентичны независимому первому Sharpen-рендеру, прежние веса кисти, capacity и caller render state |
| ExportApiParity | 51: PNG/JPG/JPEG/TGA/EXR, full/scaled размеры, bytes совпадают с общим encoder, защита пути и overwrite |
| EditableCopy | 8: независимые model/pixels, точные HDR-байты, sampling/brush values, защита от неполного источника |

Ключевые существующие регрессии:

- GaussianBlurSmoke — 18 369; GaussianSharedKernelSmoke — 4098, CPU impulse delta 0.000019713.
- DisplayChannelsSmoke — 96 проверок всех 16 масок; CanvasTerminologyMigrationSmoke — 27.
- LayerPreviewReuseSmoke — 2 752 550: cache/ownership/state, группы, processor, clipping fallback,
  отсутствие влияния export/thumbnail на Layer Preview, collapsed/detached.
- ExportWindowSmoke — 69; OutputEncodingSmoke — 828; ColorPipelineSmoke — 59.
- Compatibility0125Smoke — 376: семь frozen TIFF/JSON open/render/save/reopen, точные Drawing bytes,
  preset values; Compatibility0125ReaderSmoke — 20 adversarial guards.
- ShaderFX0125PresetSmoke — 10 408: пять исходников из тега, параметры/IDs/group headers,
  render parity, TIFF и native presets. NativeManualFxFileSmoke — 12.
- DocumentPayloadCoverageSmoke — 49, все 15 типов behaviour / 825 изменённых полей.
- DocumentReliabilitySmoke — 44, включая запрет lossy saves; DocumentMissingTypeSmoke — 15.
- DocumentVectorWideningSmoke — 178; DocumentMigrationProbe — 11.
- DocumentSaveCacheSmoke — 54; DocumentRoundTripSmoke — 33; DeferredDrawingSmoke — passed.
- ShaderFXDocumentDirtySmoke — 108; WhimTexGradientContractSmoke — 526 258;
  WhimTexGradientRetiredFieldSmoke — 6174; ColorPickerSmoke — 34.
- RemainingLegacyCleanupSmoke — 79; GaussianBlurApiSmoke — 13; CanvasRenderNamingSmoke — 17 583.

31 сценарий считается по финальному результату каждого entry point; Start/Result LayerPreviewReuse —
один завершённый сценарий. История первых попыток не скрыта:
два новых entry point сначала столкнулись с ошибкой создания своих test fixtures
(повторная оболочка behaviour после установки owner-dependent свойства); API parity затем —
с неверным индексом JObject-аргумента в тестовой reflection. Исправлены сами тесты,
повторные запуски прошли; производственный код ради этих ошибок не менялся.

Node-файлы: RefactoringR01R04, FinalLegacyAudit, RenameMarkerCleanup, GaussianStrength,
CanvasRenderNaming, Compatibility0125, BlurAndNoise1D, CanvasTerminology,
RemainingLegacyCleanup, UserSettingsCleanup, UiRefactor. Старый статический kernel-тест обновлён:
проверяет общую Capacity и подключения renderer/brush вместо трёх отдельных массивов.
UiRefactor подтверждает неизменные 75 USS cascade fingerprints.

Документация: source check — 84 страницы; agent-fields и brush schema --check — passed.
Production Jekyll/GitHub Pages build в этом локальном проходе не выполнялся.
`git diff --check` — exit 0 (Git также выводит существующие предупреждения LF/CRLF).

[Машинные результаты и история запусков](RefactoringR01R04.results.json).

