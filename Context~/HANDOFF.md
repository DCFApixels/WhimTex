# WhimTex — карта контекста

Сначала применимые [AGENTS.md](../AGENTS.md). Здесь — архитектура и навигация; [DECISIONS.md](DECISIONS.md) — договорённости по функциям.
Это не список незакоммиченных изменений, не история релизов и не поручение выполнять отложенные идеи.
Текущий код/контракт проверять перед изменениями; новый запрос пользователя имеет приоритет.

## Цель

Быстро создавать и дорабатывать текстуры, спрайты и VFX-маски в Unity. Приоритет — недеструктивные слои,
процедурные инструменты и удобная работа, не копирование большого универсального редактора.
Пользователь ожидает аргументированной оценки, а не автоматического согласия или вкусового рефакторинга.

## Архитектура: что нельзя потерять

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
- Два независимых входа ИИ: живой API для подключённого редактора; clipboard JSON/HLSL для браузерного ИИ.
  Не путать их форматы, права, импорт и синхронизацию.

## Карта исходников

Ссылки ведут в пакет; читать только нужную подсистему.

| Задача | Точка входа |
| --- | --- |
| Модель и типы слоёв | [Layer.cs](../src/Layers/Layer.cs), [Layers/](../src/Layers/) |
| Формат документа | [DOCUMENT_FORMAT.md](DOCUMENT_FORMAT.md), [WhimTexDocumentContainer.cs](../src/WhimTexDocumentContainer.cs) |
| Независимая сборка TIFF, без смены API агентов | [TIFF_AUTHORING.md](TIFF_AUTHORING.md), [WhimTexDocumentBuild.cs](../src/WhimTexDocumentBuild.cs) |
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
| Кисти и пресеты | [BrushPresetLibrary.cs](../src/BrushPresetLibrary.cs), [BrushPresetImporter.cs](../src/Editor/BrushPresetImporter.cs), [BrushHlslWindow.cs](../src/Editor/BrushHlslWindow.cs) |
| Выделение, UV, направляющие | [CanvasSelection.cs](../src/CanvasSelection.cs), [UvIslandMap.cs](../src/UvIslandMap.cs), window partial-файлы `.AreaSelection`, `.Uv`, `.Guides`, `.GuideSnapping` |
| Пипетка / пользовательские настройки | [Eyedropper](../src/TextureCompositorWindow.Eyedropper.cs), [WhimTexUserSettings.cs](../src/WhimTexUserSettings.cs) |
| Clipboard слоёв / кистей | [WhimTexApi.Clipboard.cs](../src/Automation/WhimTexApi.Clipboard.cs), [WhimTexApi.BrushClipboard.cs](../src/Automation/WhimTexApi.BrushClipboard.cs) |
| Контракты ИИ | [AI_AUTHORING.md](../AI_AUTHORING.md), [AI/README](../Documentation~/AI/README.md), [AI/BRUSHES](../Documentation~/AI/BRUSHES.md), [примеры](../Documentation~/Examples/Clipboard/README.md) |
| Живое редактирование | [skill](../Skills~/whimtex-live/SKILL.md), [LiveAgentAPI](../Documentation~/LiveAgentAPI.md), [AgentAPI](../Documentation~/AgentAPI.md) |
| Проверки | [Tests~/](../Tests~/), [building.md](../Documentation~/building.md), [генератор схемы слоёв](../Documentation~/scripts/build-clipboard-schema.mjs) |

## Как продолжать

1. Определить подсистему по запросу, прочитать её договорённости в DECISIONS и релевантный код.
2. Не восстанавливать текущую версию, ветку или статус проверок из памяти: смотреть Git и результаты запуска.
3. При изменении контракта синхронизировать парсер, генератор схемы, примеры и документацию.
4. Исторические ошибки из DECISIONS — список рисков для регрессии, не утверждение, что баги остаются.
