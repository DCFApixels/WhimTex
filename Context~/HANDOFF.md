# WhimTex — карта контекста

Сначала применимые [AGENTS.md](../AGENTS.md). Здесь — архитектура и навигация; [DECISIONS.md](DECISIONS.md) — договорённости по функциям.
Это не список незакоммиченных изменений, не история релизов и не поручение выполнять отложенные идеи.
Текущий код/контракт проверять перед изменениями; новый запрос пользователя имеет приоритет.

## Цель

Быстро создавать и дорабатывать текстуры, спрайты и VFX-маски в Unity. Приоритет — недеструктивные слои,
процедурные инструменты и удобная работа, не копирование большого универсального редактора.
Пользователь ожидает аргументированной оценки, а не автоматического согласия или вкусового рефакторинга.

## Архитектура: что нельзя потерять

Текущие входы рендера: публичный `ComposeCanvas()` и internal `ComposeCanvas(maxSize)`
дают читаемый HDR Texture2D; `RenderCanvas(maxSize)` и `RenderCanvasWithCache` —
временный RenderTexture. `RenderCanvasAtSize(width, height)` сохраняет точные размеры
и прежний scaleMultiplier = 1. [Сопоставление имён и проверки](../Tests~/CanvasRenderNaming.ru.md).

- `Layer` — стабильные ID, имя, общие настройки, дети, FX; `[SerializeReference] LayerBehaviour` — заменяемое поведение.
  Подмена поведения сохраняет оболочку слоя. Missing Reference не должен лишать доступа к общим данным.
- `TextureCompositor` и partial-файлы — композиция, отдельные пути групп, Target, обтравки и экспорта.
  Исправление только главного preview-пути часто недостаточно.
- Групповые FX: собрать детей → FX → Swizzle/Color Range → внешняя opacity/blend.
  FX изолируют Pass Through с Normal; без FX сквозной режим возвращается, если не мешают Swizzle/обтравка.
  Properties группы: только read-only фактический `Compositing`.
- Group transforms: local data only; parent→children is the sole serialized relationship. TextureCompositor.TransformHierarchy caches canvas matrices/inverses/GPU rows; invalidates by local value, parent identity/version and canvas size. Group frame is its own unit rectangle, not child bounds. Targeted effects conjugate their local transform into canvas space to avoid applying the parent twice.
- Source pixels ≠ canvas pixels. Исходное разрешение Drawing сохраняется; вписывание — трансформом.
- Градиент — сериализуемое значение + GPU LUT. Кеш LUT зависит от данных; кеш произвольного FX
  нельзя считать постоянным: возможны время, внешние текстуры и зависимости слоёв.
- Общий Gaussian material: каждый путь SetVectorArray("_Kernel", ...) должен передавать
  полный буфер на 128 Vector4, даже если активных пар меньше. Первое короткое присваивание
  ограничивает ёмкость material array и ломает последующие Gaussian/Sharpen рендеры.
  Cold Brush → Gaussian проверяется GaussianSharedKernelSmoke.
- Два независимых входа ИИ: живой API для подключённого редактора; clipboard JSON/HLSL для браузерного ИИ.
  Не путать их форматы, права, импорт и синхронизацию.

## Карта исходников

Ссылки ведут в пакет; читать только нужную подсистему.

| Задача | Точка входа |
| --- | --- |
| Модель и типы слоёв | [Layer.cs](../src/Layers/Layer.cs), [Layers/](../src/Layers/) |
| Формат документа | [DOCUMENT_FORMAT.md](DOCUMENT_FORMAT.md), [WhimTexDocumentContainer.cs](../src/WhimTexDocumentContainer.cs) |
| Очистка легаси с файловой совместимостью 0.12.5 | [инвентаризация и выполненные этапы](LEGACY_CLEANUP_0125.ru.md), [замороженная файловая база](../Tests~/Fixtures/Compatibility0125/README.md) |
| Единый JSON документа/фрагмента, режимы записи, assets и Drawing | [JSON_FORMAT.md](../Documentation~/JSON_FORMAT.md), [WhimTexDocumentJson.cs](../src/WhimTexDocumentJson.cs), [JSON API](../src/Automation/WhimTexApi.DocumentJson.cs) |
| Независимая сборка TIFF/JSON в памяти | [TIFF_AUTHORING.md](TIFF_AUTHORING.md), [WhimTexDocumentBuild.cs](../src/WhimTexDocumentBuild.cs) |
| Проверка переезда на TIFF: build, сбои, большие Drawing | [TIFF_VALIDATION.md](TIFF_VALIDATION.md) |
| Узкие места 4K Drawing Save: замеры и план оптимизации | [TIFF_SAVE_PERFORMANCE.md](TIFF_SAVE_PERFORMANCE.md) |
| Рендер и зависимости | [TextureCompositor.cs](../src/TextureCompositor.cs), partial-файлы `.Clipping`, `.EffectCache`, `.Psd`, [EffectRenderCache.cs](../src/EffectRenderCache.cs) |
| Будущая анимация, время, частицы и кэш (предварительный дизайн, отложено) | [ANIMATION_DESIGN.md](ANIMATION_DESIGN.md) |
| Окно и инструменты | [TextureCompositorWindow.cs](../src/TextureCompositorWindow.cs), partial-файлы по функциям |
| Общие настройки слоёв | [WhimTexUI.cs](../src/WhimTexUI.cs), [LayerColorSettingsView.cs](../src/LayerColorSettingsView.cs) |
| FX и каталог | [ShaderFX.cs](../src/ShaderFX.cs), [ShaderFXMetadata.cs](../src/ShaderFXMetadata.cs), [ShaderFXCatalog.cs](../src/ShaderFXCatalog.cs), [ShaderFXPresetWriter.cs](../src/ShaderFXPresetWriter.cs) |
| UI параметров | [ShaderFXParameterView.cs](../src/Editor/ShaderFXParameterView.cs), [WhimTexSoftRangeField.cs](../src/Editor/WhimTexSoftRangeField.cs) |
| Готовые эффекты | [FXPresets/](../src/FXPresets/) |
| Градиенты | [WhimTexGradient.cs](../src/WhimTexGradient.cs), [WhimTexGradientTexture.cs](../src/WhimTexGradientTexture.cs), [WhimTexGradientWindow.cs](../src/Editor/WhimTexGradientWindow.cs), [canvas handles](../src/TextureCompositorWindow.Gradient.cs) |
| Выбор цвета и история документа | [WhimTexColorPicker.cs](../src/Editor/WhimTexColorPicker.cs), [WhimTexColorField.cs](../src/Editor/WhimTexColorField.cs), [TextureCompositor.ColorHistory.cs](../src/TextureCompositor.ColorHistory.cs); [контракт HDR](../Documentation~/HDR.md#color-picker-and-history) |
| Кисти и пресеты | [BrushPresetLibrary.cs](../src/BrushPresetLibrary.cs), [BrushPresetImporter.cs](../src/Editor/BrushPresetImporter.cs), [BrushHlslWindow.cs](../src/Editor/BrushHlslWindow.cs) |
| Выделение, UV, направляющие | [CanvasSelection.cs](../src/CanvasSelection.cs), [UvIslandMap.cs](../src/UvIslandMap.cs), window partial-файлы `.AreaSelection`, `.Uv`, `.Guides`, `.GuideSnapping` |
| Пипетка / пользовательские настройки | [Eyedropper](../src/TextureCompositorWindow.Eyedropper.cs), [WhimTexUserSettings.cs](../src/WhimTexUserSettings.cs) |
| Clipboard слоёв / кистей | [WhimTexApi.Clipboard.cs](../src/Automation/WhimTexApi.Clipboard.cs), [WhimTexApi.BrushClipboard.cs](../src/Automation/WhimTexApi.BrushClipboard.cs) |
| Контракты ИИ | [AI_AUTHORING.md](../AI_AUTHORING.md), [AI/README](../Documentation~/AI/README.md), [AI/BRUSHES](../Documentation~/AI/BRUSHES.md), [примеры](../Documentation~/Examples/Clipboard/README.md) |
| Живое редактирование | [skill](../Skills~/whimtex-live/SKILL.md), [LiveAgentAPI](../Documentation~/LiveAgentAPI.md), [AgentAPI](../Documentation~/AgentAPI.md) |
| Проверки | [opt-in профили и запуск](../Tests~/RUNNING_TESTS.md), [Tests~/](../Tests~/), [финальный аудит после очистки](../Tests~/FinalLegacyAudit.ru.md), [building.md](../Documentation~/building.md), [генератор схемы JSON](../Documentation~/scripts/DocumentJsonSchema.cs); [схема полей API/кистей](../Documentation~/scripts/build-agent-fields-schema.mjs) — не формат документа |

Unity-маркеры исторических имён и общий attribute-based reader удалены. Встроенные FX
используют канонический `_Opacity` 0.12.5 без прежних aliases; пользовательская директива
`@formerlyserializedas` остаётся функцией FX/кистей. Эталоны исходников из тега —
`Tests~/Fixtures/ShaderFX0125`, проверки — `RenameMarkerCleanup.test.mjs` и `ShaderFX0125PresetSmoke.Run`.

User settings не входят в файловую гарантию: ключи, значения и layouts можно менять без миграции.
Настройки инструментов используют `Canvas.*`, вида панели — `CanvasView.*`. Поиск прежней
папки библиотеки и миграция `blurOpacity` сняты; существующую библиотеку можно выбрать
вручную в User Settings. Изменение/сброс пути не перемещает и не удаляет файлы пресетов.

Gradient clipboard/API больше не конвертируют старый `WhimTex.Gradient/1`, color-объекты,
числовые enum и `transition`. Writer градиента не менялся относительно 0.12.5;
пресеты этой версии остаются рабочими. Document reader больше не подавляет диагностику
`WhimTexGradient.transition`: неизвестные данные блокируют TIFF Save и JSON writer.
Современные JSON-объекты, массивы и Markdown fences сохранены. Проверки —
`GradientClipboardCleanup.test.mjs`, `GradientClipboardCleanupSmoke.Run` и актуализированные
`GradientPresetsSmoke`, `WhimTexGradientContractSmoke`, `WhimTexGradientRetiredFieldSmoke`.

Оставшаяся очистка: document window использует одну привязку DocumentService без старых layout
fallback. FX authoring — только `@param`; ручной drawer/массив live definitions и affine-cache
upgrade удалены. Сохранённые manual FX 0.12.5 преобразуются только при файловом чтении;
IDs/значения/ссылки и native applied snapshot сохраняются без автоматической записи ассета.
Прежний `declaredInCode` — только read-only provenance, не режим модели. Удалённые пользователем
code declarations не восстанавливаются. Sentinels Noise/Pattern/Shape нормализуются на чтении;
runtime хранит явные оси/углы. TIFF reader требует SHA-256 manifest и texture sampling block,
исторических scalar/vector coercion нет. Compact defaults v1 заморожены.
Drawing brush settings и PaintToolSettings — действующие настройки разных контекстов рисования,
а не забытая совместимость. Проверки/обоснования — в отчёте очистки.

Рефакторинг R01–R04: Layer Preview хранит только channelMask; GaussianKernel объединяет расчёт
Blur/Sharpen и полный upload 128 элементов (веса Blur Brush прежние); WhimTexRasterEncoder общий
для оконного/API растрового экспорта. Binary Deserialize возвращает ModelReadResult с независимыми
read-only диагностическими снимками вместо Last*-списков. Writer/файловая база не менялись.
[Результаты и границы проверки](../Tests~/RefactoringR01R04.ru.md).

## Как продолжать

1. Определить подсистему по запросу, прочитать её договорённости в DECISIONS и релевантный код.
2. Не восстанавливать текущую версию, ветку или статус проверок из памяти: смотреть Git и результаты запуска.
3. При изменении контракта синхронизировать парсер, генератор схемы, примеры и документацию.
4. Исторические ошибки из DECISIONS — список рисков для регрессии, не утверждение, что баги остаются.
