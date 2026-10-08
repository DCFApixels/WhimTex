# WhimTex — карта контекста

- Назначение: выбрать нужные источники, не загружая историю всей разработки.
- Статус: навигация и действующие архитектурные ограничения; не очередь задач.
- Источники истины: применимые [AGENTS.md](../AGENTS.md), текущий код и контракты по ссылкам ниже.

## Как пользоваться

1. Применить AGENTS.md и определить подсистему по запросу пользователя.
2. Прочитать нужный раздел [DECISIONS.md](DECISIONS.md) и релевантный код. Не читать всю папку автоматически.
3. Состояние ветки, версии и проверок получать из Git и нового запуска, не из контекста.
4. При переработке контрактов согласовывать парсер, генератор схемы, примеры и документацию пакетно;
   непроверенное и отложенное фиксировать до итогового прогона, не выдавать старый PASS за новый.
5. Отложенные идеи и исторические ошибки не являются поручением или утверждением о текущем баге.

Цель продукта — быстро создавать текстуры, спрайты и VFX-маски в Unity через
недеструктивные слои и процедурные инструменты. Предпочтительны аргументированные
локальные изменения, а не вкусовой рефакторинг или расширение до универсального редактора.

## Архитектурные ограничения

- `Layer` хранит стабильный ID, имя, общие настройки, детей и FX; `LayerBehaviour` заменяем.
  Смена поведения и Missing Reference не должны уничтожать оболочку слоя.
- `WhimTexDocument` — рабочая модель в памяти. Для переработок после 0.13.0 обратная совместимость
  не обязательна, включая TIFF/JSON, пресеты, API и user settings самой 0.13.0. Политика — в
  [AGENTS.md](../AGENTS.md#identity-and-compatibility); текущие readers — в [формате документа](DOCUMENT_FORMAT.md).
- Рендер имеет отдельные пути composite, group, Target, clipping, thumbnail и export.
  Изменение только основного Canvas-пути не проверяет остальные.
- FX группы: дети → FX → Channel Mapping/Color Range → внешняя opacity/blend. FX временно изолируют
  Pass Through через Normal; сохранённый выбор не переписывается. Без FX сквозной режим
  возвращается, если не мешают Channel Mapping/обтравка. `Compositing` группы read-only.
- Трансформы локальны; parent → children — единственная сериализуемая связь. Групповой frame —
  собственный единичный прямоугольник, не bounds детей. Targeted FX переводят локальный
  трансформ в canvas space, не применяя родителя дважды.
- Source pixels ≠ canvas pixels: исходное разрешение Drawing сохраняется, вписывание задаёт трансформ.
- LUT градиента зависит от данных. Произвольный FX может зависеть от времени, внешних текстур и слоёв;
  его кеш нельзя считать постоянным.
- Gaussian/Sharpen/Blur Brush используют общий material: каждый upload `_Kernel` передаёт
  все 128 Vector4. Короткий первый upload ограничивает ёмкость Unity material array.
- Live API подключённого Editor и browser JSON/HLSL — разные входы: не смешивать форматы,
  права, импорт, блокировки и сохранение.

## Карта источников

| Подсистема | Читать сначала |
| --- | --- |
| Договорённости и термины | [DECISIONS.md](DECISIONS.md), включая глоссарий |
| Модель слоёв | [Layer.cs](../src/Layers/Layer.cs), [Layers/](../src/Layers/) |
| TIFF, сохранение, recovery, Live Update | [DOCUMENT_FORMAT.md](DOCUMENT_FORMAT.md) |
| JSON документа и clipboard | [JSON_FORMAT.md](../Documentation~/JSON_FORMAT.md), [WhimTexDocumentJson.cs](../src/WhimTexDocumentJson.cs) |
| Независимая модель без окна | [TIFF_AUTHORING.md](TIFF_AUTHORING.md), [WhimTexDocumentBuild.cs](../src/WhimTexDocumentBuild.cs) |
| Режимы агентских команд | [TIFF_AGENT_COMMANDS.md](TIFF_AGENT_COMMANDS.md), [AgentAPI](../Documentation~/AgentAPI.md), [LiveAgentAPI](../Documentation~/LiveAgentAPI.md) |
| Производительность сохранения | [TIFF_SAVE_PERFORMANCE.md](TIFF_SAVE_PERFORMANCE.md) |
| Композиция и кеш | [WhimTexDocument.cs](../src/WhimTexDocument.cs), partial-файлы `.Clipping`, `.EffectCache`, `.Psd`, [EffectRenderCache.cs](../src/EffectRenderCache.cs) |
| Окно, инструменты и настройки | [WhimTexWindow.cs](../src/WhimTexWindow.cs) и тематические partial-файлы; [WhimTexUserSettings.cs](../src/WhimTexUserSettings.cs) |
| Настройки слоя | [WhimTexUI.cs](../src/WhimTexUI.cs), [LayerRenderingSettingsView.cs](../src/LayerRenderingSettingsView.cs), [LayerChannelMapping.cs](../src/LayerChannelMapping.cs) |
| FX, параметры и каталог | [ShaderFX.cs](../src/ShaderFX.cs), [ShaderFXMetadata.cs](../src/ShaderFXMetadata.cs), [ShaderFXCatalog.cs](../src/ShaderFXCatalog.cs), [ShaderFXPresetWriter.cs](../src/ShaderFXPresetWriter.cs), [ShaderFXParameterView.cs](../src/Editor/ShaderFXParameterView.cs), [WhimTexSoftRangeField.cs](../src/Editor/WhimTexSoftRangeField.cs), [FXPresets/](../src/FXPresets/) |
| Градиенты, цвет и HDR | [WhimTexGradient.cs](../src/WhimTexGradient.cs), [WhimTexGradientTexture.cs](../src/WhimTexGradientTexture.cs), [WhimTexGradientWindow.cs](../src/Editor/WhimTexGradientWindow.cs), [canvas handles](../src/WhimTexWindow.Gradient.cs), [HDR-контракт](../Documentation~/HDR.md#color-picker-and-history), [ColorHistory](../src/WhimTexDocument.ColorHistory.cs), [ColorPicker](../src/Editor/WhimTexColorPicker.cs), [ColorField](../src/Editor/WhimTexColorField.cs) |
| Кисти | [BrushPresetLibrary.cs](../src/BrushPresetLibrary.cs), [BrushPresetImporter.cs](../src/Editor/BrushPresetImporter.cs), [BrushHlslWindow.cs](../src/Editor/BrushHlslWindow.cs) |
| Выделение, UV, направляющие, пипетка | [CanvasSelection.cs](../src/CanvasSelection.cs), [UvIslandMap.cs](../src/UvIslandMap.cs), window partial-файлы `.AreaSelection`, `.Uv`, `.Guides`, `.GuideSnapping`, [.Eyedropper](../src/WhimTexWindow.Eyedropper.cs) |
| Healing / Content-Aware Fill | [HEALING_QUALITY.md](HEALING_QUALITY.md), [ContentAwareFill.cs](../src/ContentAwareFill.cs) |
| Clipboard слоёв и кистей | [WhimTexApi.Clipboard.cs](../src/Automation/WhimTexApi.Clipboard.cs), [WhimTexApi.BrushClipboard.cs](../src/Automation/WhimTexApi.BrushClipboard.cs) |
| Browser AI | [AI_AUTHORING.md](../AI_AUTHORING.md), [AI/README](../Documentation~/AI/README.md), [AI/BRUSHES](../Documentation~/AI/BRUSHES.md), [примеры](../Documentation~/Examples/Clipboard/README.md) |
| Тесты и файловые образцы | [Карта тестов](../Tests~/README.md), [RUNNING_TESTS.md](../Tests~/RUNNING_TESTS.md), [Fixtures/README.md](../Tests~/Fixtures/README.md) |
| Завершённые и спорные переименования | [RENAME_CLEANUP_AUDIT.ru.md](RENAME_CLEANUP_AUDIT.ru.md) |
| Документация и схемы | [building.md](../Documentation~/building.md), [DocumentJsonSchema.cs](../Documentation~/scripts/DocumentJsonSchema.cs); [live API field schema](../Documentation~/scripts/build-agent-fields-schema.mjs) — другой контракт, не формат документа |

## Предложения, не реализовывать автоматически

| Материал | Статус |
| --- | --- |
| [REFACTORING_CANDIDATES.md](REFACTORING_CANDIDATES.md) | Только R05/R09, требующие нового запроса и повторной проверки кода |
| [ANIMATION_DESIGN.md](ANIMATION_DESIGN.md) | Предварительный дизайн времени, анимации и частиц, не спецификация |
| [NOISE_FIELDS_DESIGN.md](NOISE_FIELDS_DESIGN.md) | Отложенный дизайн векторных шумов и distortion-карт; только внешний UI-прототип, без реализации генераторов |
| [LAYER_UPDATE_DESIGN.md](LAYER_UPDATE_DESIGN.md) | Отложенный дизайн Automatic и On Refresh для процедурных слоёв, хранения результата и строки в Layer Settings; не поручение на реализацию |
| [DOCUMENTATION_SCREENSHOTS.md](DOCUMENTATION_SCREENSHOTS.md) | План иллюстраций для пользовательской съёмки, не разрешение на правку ассетов |

## Стандарт файлов контекста

- Один H1, затем поля `Назначение`, `Статус`, `Источники истины`.
- Действующее поведение, ограничения, проверки и предложения — отдельные разделы.
  Разделы добавлять только по необходимости; не заполнять пустой шаблон.
- У правила один основной владелец. Остальные файлы дают ссылку и краткий смысл, не копию контракта.
- Для проверок указывать актуальные catalog IDs и ссылку на runner, не старые команды из Legacy.
- Даты нужны у измерений и обсуждений, не вместо статуса. Старый PASS не подтверждает новые изменения.
- Не хранить статус commit/push, журналы запусков и закрытые планы. Подробности остаются в Git;
  generated reports — в `Temp/WhimTex`. Контекст не расширяет разрешения пользователя.

## История

Полные материалы до упрощения закреплены коммитом `fc4afbf765e3b7734c3fbf0baab77701367b3f02`.
Это архив, не инструкция запуска и не текущий список задач:

- [Редакторская проверка документации](https://github.com/DCFApixels/WhimTex/blob/fc4afbf765e3b7734c3fbf0baab77701367b3f02/Context~/DOCUMENTATION_EDITORIAL_REVIEW.md).
- [Инвентаризация и этапы очистки легаси](https://github.com/DCFApixels/WhimTex/blob/fc4afbf765e3b7734c3fbf0baab77701367b3f02/Context~/LEGACY_CLEANUP_0125.ru.md).
- [Историческая TIFF-валидация и её замеры](https://github.com/DCFApixels/WhimTex/blob/fc4afbf765e3b7734c3fbf0baab77701367b3f02/Context~/TIFF_VALIDATION.md).
- [Аудит рефакторинга, завершённые и отклонённые пункты](https://github.com/DCFApixels/WhimTex/blob/fc4afbf765e3b7734c3fbf0baab77701367b3f02/Context~/REFACTORING_AUDIT_2026-10-04.ru.md).
- [Старое описание эталонных TIFF](https://github.com/DCFApixels/WhimTex/blob/fc4afbf765e3b7734c3fbf0baab77701367b3f02/Context~/TIFF_BASE_HEART.md).

История остальных сокращённых заметок доступна из их раздела `История`.
