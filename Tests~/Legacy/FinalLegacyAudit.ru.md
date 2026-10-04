# WhimTex: финальный аудит легаси и тестов

Дата: 4 октября 2026 года. Локальный проект: `D:\DCFA\Projects\Test6.6`.
Пакет: `Packages/com.dcfapixels.whimtex`. Unity 6000.7.0a6, Direct3D12.

## Итог

Обнаруженные в этом проходе безопасно удаляемые остатки удалены. Основные регрессии
проходят; найдена и исправлена настоящая ошибка общего материала Gaussian.
Гарантия совместимости остаётся только для поддерживаемых файлов, записанных 0.12.5.
Это не утверждение, что любое упоминание `legacy`, `Preview` или fallback является мёртвым кодом.

| Проверка | Результат |
| --- | --- |
| Инвентаризация верхнеуровневых C#-скриптов Tests~ | 268 файлов: 231 regression, 19 multi-step, 12 diagnostic, 6 manual-helper |
| Основные Unity-регрессии | 231/231 выбранных Main/Run выполнены повторно после исправлений |
| Многошаговые сценарии | 16/19 файлов: выбранные сценарии выполнены, список ниже |
| Диагностика | 2/12 файлов выполнены; остальные не запускались |
| Вспомогательные скрипты | 3/6 setup выполнены перед соответствующими UI-проверками |
| Компиляция C#-классов тестов | 181 класс: 180 compile-only через Pipeline и новый Gaussian-тест с компиляцией/исполнением |
| Компиляция пакета в Editor | Штатная Unity-компиляция/domain reload: completed, failed=false, errors=[] |
| Node-проверки | 76/76 файлов, без ошибок и пропусков |
| Документация | Source-проверка 84 страниц и обе generated schema-проверки проходят |
| Production Jekyll / GitHub Pages | Не проверено: отсутствуют Ruby/Bundler; Docker/WSL также недоступны |
| Commit / push / версия / player build | Не выполнялись |

Полная инвентаризация, выбранные entry points, аргументы, результаты и история неудачных
попыток находятся в [FinalLegacyAudit.results.json](FinalLegacyAudit.results.json).
Статус каждого файла там относится к выполненному сценарию, а не ко всем его публичным методам.
Начальные ошибки запуска или проверки старого контракта не выдаются за текущие красные тесты.

## 1. Удалённые неиспользуемые методы

Проверены определения, вызовы, reflection/string consumers в исходниках, тестах и документации.

| Файл | Удалено | Причина |
| --- | --- | --- |
| [SDFLayerBehaviour.cs](../src/Layers/SDFLayerBehaviour.cs) | `ConvertDistance(float)` | Не использовался |
| [MissingLayerRecovery.cs](../src/MissingLayerRecovery.cs) | `GradientTime(JToken, out float)` | Не использовался |
| [TextureCompositor.cs](../src/TextureCompositor.cs) | `CloneEmbeddedShaderFX()`, `CloneDrawingLayerTextures()` | Отдельные неиспользуемые helpers; действующие пути клонирования сохранены |
| [TextureCompositorWindow.UI.cs](../src/TextureCompositorWindow.UI.cs) | `FillRect(Painter2D, ...)` | Не вызывался; активное рисование UI сохранено |
| [WhimTexDocumentSerializer.cs](../src/WhimTexDocumentSerializer.cs) | `ReflectedFieldCount(Type)` | Не вызывался |

Шесть оставшихся кандидатов с единственным текстовым упоминанием проверены и оставлены:
это callbacks, зарегистрированные через `InitializeOnLoadMethod` или `MenuItem`, включая
menu validation. Отсутствие прямого вызова не делает такой метод мёртвым.
Статический список кандидатов — подсказка для проверки, а не доказательство полноты аудита.

## 2. Удалённые тесты

- `LayerPersistenceSetup.cs`, `LayerPersistenceVerify.cs`, `LayerPersistenceCleanup.cs`:
  проверяли исключённый compositor `.asset` backend и его фиксированную временную fixture.
  Текущие TIFF/JSON, Missing Reference и настоящий domain reload проверяются другими тестами.
- `SpriteEditorCompileSmoke.cs`: проверял удалённый `WhimTexSpriteDataProvider` через
  старый optional AssemblyBuilder путь. Предмета проверки больше нет.

Удалены только четыре тестовых скрипта. Они восстановимы из Git.
Пользовательские документы, пресеты, сцены и другие пользовательские ассеты не удалялись.
Эти удаления не означают отказ от native FX preset `.asset`: такой файловый формат 0.12.5 сохраняется.

## 3. Актуализированные тесты

| Тест | Что исправлено | Проверка |
| --- | --- | --- |
| [DocumentJsonSmoke.cs](DocumentJsonSmoke.cs) | Убрана зависимость от пользовательских Assets/Learn/Pass/*.tiff вне frozen-базы. Используются procedural.tiff из 0.12.5 и package golden BASE_Gradient_128.tiff. Пустой диапазон не считается успешным прогоном | Две fixture × три write modes: шесть JSON/TIFF/render roundtrip; исходные байты не изменены |
| [LiveAgentSmoke.cs](LiveAgentSmoke.cs) | Объявление FX через `@param`, без удалённого массива ручных определений | 113 проверок |
| [MakeSeamlessContractSmoke.cs](MakeSeamlessContractSmoke.cs) | Ссылка на действующий agent-fields.schema.json вместо удалённой layers.schema.json | 4407 проверок |
| [NoiseApiSmoke.cs](NoiseApiSmoke.cs) | Убрано ожидание миграции старого runtime/JsonUtility контракта; проверяются текущие явные оси | 67 проверок; файловая нормализация отдельно покрыта 0.12.5-базой |
| [UIRefreshSmoke.cs](UIRefreshSmoke.cs) | Fixture создаёт controls из настоящего `@param` parser. Исправлены поиск FloatField, ranges, порядок и controls при замене модели | Main и Bindings проходят; stable UI, conditional rows, replacement, reorder/add/remove, coalescing/detach/reattach |

В UIRefresh счётчик allocation недоступен в этом Editor: измеренное отсутствие allocation
не заявляется. Проверки стабильности объектов и событий выполнены.

[LayerComposition.md](LayerComposition.md) приведён к текущему TIFF/JSON контракту.
Прежние [TestSuiteAudit.ru.md](TestSuiteAudit.ru.md) и [RedTestsFixes.ru.md](RedTestsFixes.ru.md)
помечены как исторические, их результаты не переписаны.
В [building.md](../Documentation~/building.md) исправлено устаревшее название generated schema.

## 4. Настоящий баг: Blur Brush ограничивал общий Gaussian kernel

Это не несовместимость файлов и не устаревший PNG-эталон. Ошибка зависела от порядка
первого использования общего материала `WhimTexMaterials.GaussianBlur`.

Blur Brush первым передавал `_Kernel` из пяти Vector4. GaussianBlurRenderer и
SharpenRenderer используют массив на 128 Vector4. После первой короткой загрузки
длинный kernel обрезался, поэтому некоторые примеры заметно расходились с эталонами.
Сброс материала или иной порядок первого рендера временно скрывал ошибку.

Unity документирует, что длина material array фиксируется первым присваиванием,
а последующее более длинное значение обрезается.
Источник: [Material.SetVectorArray](https://docs.unity3d.com/ru/current/ScriptReference/Material.SetVectorArray.html).

Исправление в [DrawingLayerBehaviour.BlurBrush.cs](../src/Layers/DrawingLayerBehaviour.BlurBrush.cs):
переиспользуемый буфер на 128 Vector4; первые пять пар, их веса и `_PairCount=5`
остались прежними. Одновременно убрана allocation массива на каждом сегменте кисти.
Формат файлов и shader uniform не изменены.

Добавлен [GaussianSharedKernelSmoke.cs](GaussianSharedKernelSmoke.cs):

- Берёт собственный холодный материал; первым реально запускает Blur Brush.
- Проверяет полную ёмкость массива, затем Gaussian radius=24 по одиночному импульсу.
- Сравнивает GPU-результат с независимым дискретным CPU-эталоном и проверяет finite pixels.
- 4098 проверок; максимальное расхождение 0.0000197129970267627 при пороге 0.00005.
- В finally восстанавливает прежний package-owned материал и render state, освобождает свои объекты.

Порог нового теста учитывает paired bilinear GPU sampling; существующие допуски регрессий
и PNG-эталоны не ослаблены. Примеры не перегенерировались.
Последовательность `AgentEditing → AgentResize → AgentSamples[0,38]` проходит без сброса материала.
Все 38 примеров повторно прошли также после полного набора 231 регрессии.

## 5. Совместимость файлов 0.12.5

Frozen fixture, SHA-256 manifest и `WhimTexJsonDefaultsV1.cs` не менялись.

| Проверка | Результат |
| --- | --- |
| Compatibility0125Smoke | 376 проверок |
| Compatibility0125ReaderSmoke | 20 проверок |
| ShaderFX0125PresetSmoke | 10408 проверок, пять точных HLSL из 0.12.5, render parity, TIFF/native FX |
| RemainingLegacyCleanupSmoke | 79 проверок, включая все 13 сохранённых типов FX, IDs/значения и idempotence |
| NativeManualFxFileSmoke | 12 проверок |
| DocumentVectorWideningSmoke | 178 проверок текущего строгого чтения и несовместимых данных |
| TiffCompactReferenceSmoke.RunGradient / RunDrawing | Оба golden TIFF: неизменные размеры/SHA-256, чтение и композиция |

Нативный ScriptableObject как временная модель документа остаётся действующим кодом.
Неиспользуемый compositor `.asset` save backend не возвращается.
Saved manual FX читаются только через узкий file adapter; создавать новые параметры
можно только через `@param`. Пользовательский authoring marker `@formerlyserializedas`
не является старым Unity rename marker и остаётся действующей возможностью.

## 6. Выполненные многошаговые сценарии

| Файл | Проверенные завершающие entry points |
| --- | --- |
| [BrushHeaderLayoutSmoke.cs](BrushHeaderLayoutSmoke.cs) | BrushHeaderLayoutSmoke.Verify |
| [CanvasViewFooterSmoke.cs](CanvasViewFooterSmoke.cs) | eval_file |
| [ColorPickerRingSmoke.cs](ColorPickerRingSmoke.cs) | ColorPickerRingSmoke.Verify |
| [ContentFillUiSmoke.cs](ContentFillUiSmoke.cs) | eval_file |
| [DocumentReloadSmoke.cs](DocumentReloadSmoke.cs) | DocumentReloadSmoke.Verify |
| [GradientHistorySmoke.cs](GradientHistorySmoke.cs) | GradientHistorySmoke.Verify, GradientHistorySmoke.PickerRecency |
| [GradientKeyPickerSmoke.cs](GradientKeyPickerSmoke.cs) | GradientKeyPickerSmoke.FocusLifecycle, GradientKeyPickerSmoke.Verify |
| [GuideReloadSmoke.cs](GuideReloadSmoke.cs) | GuideReloadSmoke.Verify |
| [LayerPreviewPanelSmoke.cs](LayerPreviewPanelSmoke.cs) | LayerPreviewPanelSmoke.Result |
| [LayerPreviewReuseSmoke.cs](LayerPreviewReuseSmoke.cs) | LayerPreviewReuseSmoke.Result |
| [LayerTransferSmoke.cs](LayerTransferSmoke.cs) | LayerTransferSmoke.Drop, LayerTransferSmoke.Guards |
| [QuiltingSeedLayoutSmoke.cs](QuiltingSeedLayoutSmoke.cs) | QuiltingSeedLayoutSmoke.Result |
| [SeamlessUIPolishSmoke.cs](SeamlessUIPolishSmoke.cs) | SeamlessUIPolishSmoke.Result |
| [TiffCompactReferenceSmoke.cs](TiffCompactReferenceSmoke.cs) | TiffCompactReferenceSmoke.RunGradient, TiffCompactReferenceSmoke.RunDrawing |
| [UvUiSmoke.cs](UvUiSmoke.cs) | eval_file |
| [WhimTexGradientReloadSmoke.cs](WhimTexGradientReloadSmoke.cs) | WhimTexGradientReloadSmoke.End |

Document/Guide/Gradient reload проверены после настоящей штатной перекомпиляции/domain reload,
не только после disable/enable. У LayerTransfer выполнены все шесть Drop-вариантов:
top, before, after, group, end, footer, плюс Guards. Setup/cleanup соблюдены.
Также отдельно выполнены UIRefresh.Bindings и ColorPicker.LayoutVerify.
Запуск Start/Setup сам по себе не считался прохождением проверки.

## 7. Что не запускалось

Ниже 16 файлов без исполнения; C#-классы из них проверены compile-only.
Помощники, captures и diagnostics не удалены только из-за того, что они не являются
автоматическими регрессиями: у них есть действующие задачи.

| Файл | Категория | Ограничение |
| --- | --- | --- |
| [CanvasViewFooterHintResize.cs](CanvasViewFooterHintResize.cs) | manual-helper | Ручной resize/framebuffer capture помощник; не запускался. |
| [ContentFillUiCapture.cs](ContentFillUiCapture.cs) | manual-helper | Ручной resize/framebuffer capture помощник; не запускался. |
| [DocumentBurstHashProbe.cs](DocumentBurstHashProbe.cs) | diagnostic | Диагностический probe/benchmark/experiment, не обязательная регрессия; не запускался. |
| [DocumentMigrationProbe.cs](DocumentMigrationProbe.cs) | diagnostic | Диагностический probe/benchmark/experiment, не обязательная регрессия; не запускался. |
| [DocumentPerformanceProbe.cs](DocumentPerformanceProbe.cs) | diagnostic | Диагностический probe/benchmark/experiment, не обязательная регрессия; не запускался. |
| [DocumentReleaseValidation.cs](DocumentReleaseValidation.cs) | multi-step | Отдельный release/install/deferred-failure сценарий; содержит player-build этапы, которые не запускались. |
| [DocumentSaveCostProbe.cs](DocumentSaveCostProbe.cs) | diagnostic | Диагностический probe/benchmark/experiment, не обязательная регрессия; не запускался. |
| [DocumentSaveTailProbe.cs](DocumentSaveTailProbe.cs) | diagnostic | Диагностический probe/benchmark/experiment, не обязательная регрессия; не запускался. |
| [HistogramArithmeticAudit.cs](HistogramArithmeticAudit.cs) | diagnostic | Диагностический probe/benchmark/experiment, не обязательная регрессия; не запускался. |
| [ModelSerializationBenchmark.cs](ModelSerializationBenchmark.cs) | diagnostic | Диагностический probe/benchmark/experiment, не обязательная регрессия; не запускался. |
| [NoiseSmallScaleExperiment.cs](NoiseSmallScaleExperiment.cs) | diagnostic | Диагностический probe/benchmark/experiment, не обязательная регрессия; не запускался. |
| [PatchQuiltingEquivalence.cs](PatchQuiltingEquivalence.cs) | diagnostic | Диагностический probe/benchmark/experiment, не обязательная регрессия; не запускался. |
| [SeamlessOptimizationSmoke.cs](SeamlessOptimizationSmoke.cs) | multi-step | Отдельные optimization/release, stress, benchmark или capture сценарии; проверены компиляцией, не выполнены. |
| [SeamlessReleaseSmoke.cs](SeamlessReleaseSmoke.cs) | multi-step | Отдельные optimization/release, stress, benchmark или capture сценарии; проверены компиляцией, не выполнены. |
| [SmallDocumentSaveProbe.cs](SmallDocumentSaveProbe.cs) | diagnostic | Диагностический probe/benchmark/experiment, не обязательная регрессия; не запускался. |
| [UvUiCapture.cs](UvUiCapture.cs) | manual-helper | Ручной resize/framebuffer capture помощник; не запускался. |

Из diagnostics выполнены HealingNoiseSeamDiagnostic.Main и TiffAgentDiagnosticsSmoke.Run.
Основные алгоритмы seamless отдельно покрыты многочисленными regression Main/Run,
но это не заменяет optional stress/performance/visual сценарии.

Не подтверждены: player build, физические жесты/нативные меню/пипетка,
все capture-варианты и их визуальная оценка, другие Unity-версии, графические backends и scaling.
У DrawingReloadSmoke проверен disable/enable lifecycle; перенос незавершённого GPU stroke
через reload остаётся ручным сценарием, а не заявленным результатом этого прогона.

## 8. Проверки документации и инструменты

`check-docs.mjs source`: 84 страницы, взаимные EN/RU/ZH связи, navigation и локальные Markdown links.
`build-agent-fields-schema.mjs --check` и `build-brush-schema.mjs --check` проходят.
Это проверки исходников, не production-сборка сайта.

Все 76 Node-файлов выполнены, включая реальную TextMate/Oniguruma grammar:
использованы зависимости уже установленного VS Code через process-local
`WHIMTEX_VSCODE_APP`. Глобальные пакеты и инструменты не устанавливались.

Применены навыки `unity-cli` — штатная проверка в точном подключённом Test6.6 Editor,
и `ui-uitk` — проверка UI Toolkit/Painter2D перед удалением неиспользуемого draw helper.
Обновлены только локальные исходники, тесты и отчётные/контрактные документы;
commit, push, version bump и player build не выполнялись.

## Остатки, которые намеренно сохраняются

- Узкое чтение поддерживаемых файлов 0.12.5 и защита неизвестных данных от потери.
- File-only перевод сохранённых manual FX в `@param`, включая исходные эффективные значения.
- Native FX/preset libraries и их действующие данные.
- Активные настройки разных контекстов кисти, callbacks, кеши и штатные render fallbacks.
- Контекстно правильные `Preview`-имена: не каждый Preview относится к Canvas View.

Новых обоснованных кандидатов на удаление легаси в этом проходе не осталось.
Дальнейшее упрощение этих действующих механизмов — отдельный рефакторинг,
а не безопасное удаление старой обратной совместимости.
