# WhimTex: инвентаризация легаси с точкой перехода 0.12.5

Дата: 3 октября 2026 года. База проверки: `v0.12.5`, коммит `a72cc9544d39f93555ca6e9f39c137340e3028f0`.

Исходная инвентаризация ниже — исторический план по коду 0.12.5. Выполнены восемь этапов:
файловая база, удаление document `.asset` backend, старого clipboard, rename markers,
старых user settings bridges, gradient bridges, оставшаяся очистка окон/FX/модели/reader
и финальный аудит неиспользуемых методов, тестов и документации.
Текущий итог приведён первым; последующие этапы и исходный план — история работы.
Последнее уточнение пользователя исключает композиторы `.asset` из обратной совместимости.
Все user settings также исключены: их можно менять, сбрасывать и удалять без миграции.
Это не разрешение удалять или переписывать пользовательские файлы пресетов.
Версия пакета не изменена, commit/push не выполнялись.

## Выполнено: финальный аудит — 4 октября 2026

- Удалены шесть неиспользуемых helpers и четыре тестовых скрипта снятых функций.
  Действующие Unity attribute callbacks и file-only адаптер 0.12.5 оставлены.
- Актуализированы DocumentJson, LiveAgent, MakeSeamlessContract, NoiseApi и UIRefresh:
  текущие fixtures, schema, `@param` и controls вместо ожиданий удалённых API.
- Исправлена ошибка общего Gaussian material: Blur Brush больше не фиксирует `_Kernel`
  первым коротким массивом на пять элементов. Все пути загружают буфер на 128 Vector4;
  число активных пар и веса кисти не изменены. Добавлена cold-material/CPU-reference регрессия.
- После исправлений проходят 231/231 основных Unity entry points, выбранные сценарии
  16 многошаговых файлов, 76/76 Node-файлов и файловые проверки 0.12.5.
  Все 38 примеров повторно сверены с неизменёнными PNG; реальный domain reload проверен.
- Исходники 84 страниц и generated schemas проходят. Production Jekyll не запускался:
  Ruby/Bundler недоступны. Optional stress/capture/diagnostic и ручные ограничения перечислены отдельно.

Подробности, инвентаризация всех 268 C#-скриптов и статусы невыполненных сценариев:
[финальный отчёт](../Tests~/Legacy/FinalLegacyAudit.ru.md),
[машиночитаемые результаты](../Tests~/Legacy/FinalLegacyAudit.results.json).
Commit/push, изменение версии и player build не выполнялись.

## Выполнено: оставшаяся очистка — 4 октября 2026

### Удалено и упрощено

- Окно больше не хранит отдельные path/GUID/owner документа и не восстанавливает старые layouts.
  Привязкой владеет DocumentService. Текущие Save/Save As, навигация и domain reload сохранены.
- Неиспользуемый корневой JSON `kind` удалён из reader/schema/API-примеров. Writer 0.12.5
  его уже не записывал. `Shape.kind` остаётся действующим полем.
- Полностью снят режим ручного создания FX-параметров: старый drawer, переключатель
  `declaredInCode` в модели, `UsesCodeParameters`, manual controls/fallbacks,
  приоритет ручных параметров и массив определений `parameters` в live FX add/replace.
  Единственный способ объявления — `@param`. Агентский `set` продолжает менять значения
  уже объявленных параметров, а не создавать определения.
- Удалены `UpgradeTransformHelpers` и `upgradedTransformShader`. Builder 0.12.5 уже
  содержит актуальные projective helpers; прежний affine-кеш не входит в гарантию.
- Noise/Pattern хранят явные значения Y; Shape — четыре явных значения углов.
  Runtime-fallback «0 означает X» и «отрицательный угол означает общий roundness» удалён.
  Shape `roundness` больше не сериализуемое поле; convenience setter задаёт все четыре угла.
- Из бинарного reader удалены scalar/vector widening и преобразования IntVector в FloatVector.
  Канонические теги читаются в свой точный тип; несовместимые поля диагностируются.
- Убраны приём контейнера без SHA-256 manifest и восстановление texture sampling по defaults
  при отсутствии блока. Writer 0.12.5 уже сохраняет manifest и 24-байтовый sampling.
  Eager и deferred чтение требуют эти данные; проверки целостности не ослаблены.

### Что осталось только на чтении файлов 0.12.5

Узкий адаптер `WhimTexFileCompatibility0125` не поддерживает прежние API/settings:

- Контролируемо пропускает retired output/sprite/origin поля; остальные неизвестные данные
  по-прежнему защищены от молчаливой потери при сохранении.
- Разворачивает нулевые Y и отрицательные углы после чтения, включая Compact defaults.
  Новая запись содержит явные значения.
- Преобразует сохранённые manual FX uniforms в `@param`, сохраняя IDs, значения,
  текстуры/слои и произвольные градиенты/кривые. Не сводит сложные значения к HLSL defaults.
  Scalar за пределами manual hard range получает прежнее эффективное clamped-значение:
  это сохраняет изображение, хотя недостижимое исходное число нормализуется.
- Прежний `declaredInCode` читается только как признак происхождения: намеренно удалённый
  code-declared параметр не превращается в manual. Поля/режима в runtime и новых writers нет.
  Для native FX preset используются сохранённые controls. Черновик и applied snapshot
  нормализуются отдельно; не применённое редактирование остаётся не применённым.
  Новый JSON writer сохраняет параметры валидного черновика без мутации applied state;
  сохранение/повторное чтение также не возвращает удалённые объявления.
- Malformed FX draft сохраняется для диагностики/ремонта через обычный Apply.
  Reader не переписывает внешние HLSL или native preset файлы и не делает их dirty.

Это необходимая файловая совместимость, а не оставленный manual authoring.
Старый compositor `.asset` не поддерживается; самостоятельный ShaderFX preset `.asset` поддерживается.

### Итог исходной инвентаризации

| Пункты | Текущее решение |
| --- | --- |
| 1–18 | Старый document backend, output/sprites, clipboard, helpers, origin metadata и root kind удалены. |
| 19 | Sentinels перенесены из runtime в нормализацию при файловом чтении. |
| 20 | Настройки кисти оставлены после проверки потребителей: это два действующих контекста, см. ниже. |
| 21–28 | Оконные fallback, settings bridges, Unity rename markers, общий alias reader и исторические coercion удалены. |
| 29–31 | Manual FX authoring и специальные приоритеты удалены; сохранён только файловый конвертер. |
| 32 | Пользовательский `@formerlyserializedas` оставлен как текущая функция редактирования FX/кистей. Старые aliases встроенных FX уже сняты. |
| 33 | Автоматический upgrade старых affine helpers удалён. |

Четыре старых API-алиаса также удалены ранее. Согласованные пункты 1–5 текущей очистки выполнены;
доказанно удаляемого легаси из этой инвентаризации не оставлено в рабочем пути.

### Почему не удалены настройки кисти и другие похожие места

`DrawingLayerBehaviour` хранит настройки кисти для рисования по конкретному слою через API;
`GetStrokeParameters` и `WhimTexApi.Paint` реально используют их.
`PaintToolSettings` обслуживает интерактивный инструмент окна. Удаление одной структуры
потребовало бы изменения действующей модели рисования, а не снятия неиспользуемой совместимости.
Сохранённые настройки слоя и его pixels остаются частью файловой гарантии.
Объединение этих двух контекстов — возможный будущий рефакторинг, не скрытый незаконченный этап.

Также сохранены:

- Замороженные `WhimTexJsonDefaultsV1`: они определяют смысл уже записанного Compact JSON.
- Текущие layer ownership/conversions, value transfer при редактировании HLSL, группы и controls.
- Поддержка заявленных Unity 6000.0/6000.3, текущих типов файлов и расширений.
- Missing layers/assets, GUID/path разрешение ссылок, transactional save/recovery, integrity,
  ревизии, HDR Drawing, Undo, native TIFF importer/Sprite, Live Update.
- Методы/uniform со словом Preview, где оно обозначает актуальное масштабированное или отдельное
  представление; имя само по себе не является legacy-контрактом.
- Файлы пользовательской библиотеки, действующие GUID и исторические отчёты/CHANGELOG.

### Проверки седьмого этапа

Компиляция — штатно через подключённый Unity Editor Test6.6, без ошибок.
Все 40 запущенных `run_script` smoke-файлов прошли; дополнительно прошли Shape,
FileNavigation и Prepare → реальный domain reload → Verify. Это релевантная регрессионная
матрица, не заявление о запуске каждого Unity-теста проекта.

| Проверка | Результат |
| --- | --- |
| Compatibility0125Smoke.Run | 376: семь неизменённых TIFF/JSON из тега, render/save/reopen, точные Drawing bytes, IDs/FX и пресеты. |
| Compatibility0125ReaderSmoke.Run | 20: bounded discard, back-references и защита неизвестных данных. |
| ShaderFX0125PresetSmoke.Run | 10 408: пять точных исходников HLSL 0.12.5, render parity, TIFF/native FX presets. |
| RemainingLegacyCleanupSmoke.Run | 79: все 13 сохранённых типов FX, IDs/значения, сложные градиенты/кривые, sampling, idempotence, Compact axes/corners, root kind и удалённые code declarations. |
| NativeManualFxFileSmoke.Run | 12: реальный native asset load, draft/applied snapshots, прежний effective render, файл не перезаписан/не dirty, удалённый @param не восстановлен. |
| DocumentVectorWideningSmoke.Run | 178: точные typed roundtrips и отказ от исторических coercion. |
| FillPatternSmoke.Main | 2 958 671: GPU layouts, TIFF, transforms, API, Undo/Redo. |
| NoiseControls / WarpScale / Randomize / ScaleDistribution | 129 / 327 751 / 7 279 / 3 295 655. |
| ShapeFeather / Shape | 852 516 / 184 404. |
| ShaderFXCurve / Projective / Vectors | 225 / 8 521 / 38. |
| ShaderFXConditionalParameters / Control / DocumentDirty | 65 / 35 / 108. |
| ShaderFXGroup / FormerlySerializedAs / Enum / Bool / Gradient | PASS: все 27 встроенных FX, текущая grammar, UI, значения, GPU и export. |
| ApplyFX / GroupFX / GradientSoftFX / TextureLayers / SelfTexture | 387 / 10 / 12 336 / 6 / 8. |
| DocumentJson Contract / Safety / Operations / BrokenFX / Validation / OptionalSettings | 23 / 40 / 60 / 27 / 169 / 121. |
| DocumentStorageBoundary / Preparation / Reliability / BurstIntegrity | 14 / 37 / 44 / 32. |
| DeferredDrawing / ShaderFXTiffCatalog | PASS: deferred materialization/resave и linked FX. |
| DocumentReload / FileNavigation | 8 / 16: привязка окна, unsaved state, Save, Live Update и открытие/переиспользование окна. |

Неактуальные ожидания старого контракта актуализированы: vectors теперь проверяют отказ
от coercion, Pattern проверяет нормализацию через файловый reader, Curve API задаёт значение
существующего параметра, BrokenFX ремонтируется с сохранением @param, manifest обязателен.
Сценарий старого affine-кеша заменён проверкой актуального applied projective shader.
Полезное покрытие сохранено; удаления тестов на этом этапе не потребовалось.

Все 75 Node `.test.mjs` файлов — PASS. Проверка исходников docs — 84 страницы EN/RU/ZH,
API/brush generators `--check` и `git diff --check` — PASS. Document schema перегенерирована
из актуальных 31 типов модели. Новая Node-проверка подключена к documentation CI.
Исходные fixtures, Compact defaults v1, версия и CHANGELOG не менялись.
Проверки использовали собственные временные данные/ассеты; пользовательские файлы не удалялись.

Jekyll/GitHub Pages production build, deployment и player build на этом этапе не запускались.
Применены skills `ui-uitk` (UI-путь упрощён без изменения оформления) и `unity-cli`
(штатная компиляция и проверки только в подключённом Test6.6).
Работа локальная: commit/push не выполнялись.

## История шестого этапа: очистка gradient bridges — 4 октября 2026

Из `WhimTexGradientClipboard` удалены:

- Префикс `WhimTex.Gradient/1` и его обрезка перед parsing.
- Преобразование старых color-объектов `{r,g,b,a}` в RGBA-массивы.
- Преобразование числовых `mode`, `wrapMode`, `colorSpace` в имена enum.
- Вспомогательный `ConvertEnum` и ставший ненужным import.

В общем API reader снят приём `transition`. Теперь это неизвестное поле,
а не принимаемая настройка без эффекта. Правило действует также для gradient
ввода кистей. В document serializer удалено особое подавление диагностики
`WhimTexGradient.transition`; новые исторические aliases не добавлялись.
Числовые enum в бинарном формате документа и scalar/vector coercion этим этапом
не менялись — их нельзя смешивать с удобным JSON clipboard/API-вводом.

Сохранены современный `whimtex.gradient`, RGBA-массивы, строковые enum,
JSON-объекты без обёртки, отдельные массивы ключей, JSON/plain Markdown fences,
BOM, HDR-значения, независимые color/alpha дорожки и midpoints.
Алгоритм Rounded, GPU/LUT, режимы интерполяции, кеши, UI и формат файлов не менялись.

### Почему совместимость 0.12.5 сохранена

В исходном теге `v0.12.5` модель `WhimTexGradient` уже не содержит `transition`.
Gradient Copy/Save уже записывают `whimtex.gradient`, массивы и имена enum.
Текущий метод Write побайтно совпадает с методом тега после нормализации LF и trim;
его SHA-256 — `eb2506c494c4dc779c80b178331461db9b96fedd8fd2d4896b5c3ddbb24ed64b`.
Замороженные документы и пресет градиента не перегенерировались.

Более старые файлы следует пересохранить через 0.12.5 до обновления, а clipboard
заново скопировать в этой версии. TIFF с неизвестным `transition` остаётся доступным
для чтения известных данных с диагностикой. Save и JSON writer блокируют потерю
неизвестных данных; неизвестное поле в document JSON отвергается.

### Проверки шестого этапа

Штатная компиляция подключённого Unity Editor Test6.6 — без ошибок.
Все **12 запущенных C# smoke-файлов** прошли:

| Проверка | Результат |
| --- | --- |
| GradientClipboardCleanupSmoke.Run | 40 392; все modes/wrap/spaces, HDR/alpha/midpoints/sampling, варианты ввода, отказ clipboard/API/brush от старых данных, библиотека пресетов 0.12.5, TIFF Save/JSON writer guards |
| GradientPresetsSmoke.Main | PASS; независимые копии, актуальные форматы, библиотека/превью пресетов, удаление только собственного пресета в `.trash` |
| WhimTexGradientContractSmoke.Main | 526 258; кривые, сериализация, строгий clipboard, кеш, Smoothness Undo/Redo |
| WhimTexGradientRetiredFieldSmoke.Main | 6 174; шесть прежних binary snapshots теперь проверяют диагностику неизвестного поля и сохранность известных значений |
| GradientDefaultsSmoke.Main | 71; defaults, explicit overrides, brush inputs и roundtrip |
| WhimTexGradientRoundedSmoke.Main | 68 512; независимый аналитический эталон, redundant stops, alpha и boundaries |
| WhimTexGradientPipelineSmoke.Main | 102 400; composite, группы, Specific Target, clipping и export |
| GradientSoftFXSmoke.Main | 12 336; GPU FX sampling, rebind и lossy-export guard |
| GradientGpuRunSmoke.Rounded | 2 858 482; GPU/CPU, формы, endpoint joins, кеш и lifecycle |
| GradientGpuSmoke (eval_file) | 1 309 245; включая точные Fixed/Mirror boundaries |
| Compatibility0125Smoke.Run | 366; семь исходных TIFF/JSON, render/save/reopen, точные Drawing bytes и пресеты |
| Compatibility0125ReaderSmoke.Run | 20; bounds и защита неизвестных данных |

Максимальная абсолютная разница GPU/CPU — 0.008546, внутри прежнего допуска
Perceptual; допуски не расширялись. Три старых теста актуализированы, не удалены:
проверки приёма исторического JSON заменены проверками отказа/диагностики;
остальное покрытие сохранено. Шесть старых binary snapshots также сохранены.
Новый Node-контракт добавлен в documentation CI.

Все **74 JS тестовых файла** — код 0. Source-проверка документации (84 страницы
EN/RU/ZH), генераторы API/brush schema (`--check`) и `git diff --check` — PASS.
Схемы уже использовали только современную gradient grammar, перегенерация не требовалась.
Настройки восстановлены, собственные временные файлы очищены; пользовательские файлы
не удалялись. GitHub Pages/Jekyll и player build не запускались.
Версия, CHANGELOG, Compact defaults v1 и исходные fixtures не менялись; commit/push нет.

Следующий отдельный этап — ручные ShaderFX-параметры с сохранением значений из файлов 0.12.5.

## Выполнено: очистка user settings — 4 октября 2026

Переименованы **11 ключей EditorPrefs** без чтения или миграции старых значений:

| Настройки | Новое семейство / ключ |
| --- | --- |
| CheckerLight, CheckerDark, InvalidPixels, CheckerSize, ShowManta, PostFxBackground, PostFxBackgroundMode | `DCFApixels.WhimTex.CanvasView.*` вместо `DCFApixels.WhimTex.Preview.*` |
| PaintToolSettings | `DCFApixels.WhimTex.Canvas.PaintToolSettings` |
| PreviewTool | `DCFApixels.WhimTex.Canvas.Tool` |
| PreviewTransformReturnTool | `DCFApixels.WhimTex.Canvas.TransformReturnTool` |
| PaintingPreviewScale | `DCFApixels.WhimTex.Canvas.PaintingScale` |

Старые значения остаются в EditorPrefs, но runtime их больше не использует.
Соответствующие настройки при первом запуске берут значения по умолчанию.
Удалена строковая миграция `blurOpacity` → `blurFlow` при загрузке настроек кисти.
Поле `blurFlow`, его значение по умолчанию и обработка повреждённого JSON сохранены.
Остальные актуальные ключи не сбрасывались массово.

Удалены `LegacyDataFolder` и автоматический поиск исторической папки библиотеки.
Папка по умолчанию теперь всегда `LocalApplicationData/DCFApixels/WhimTex/Presets`.
Уже выбранный пользовательский путь по-прежнему читается напрямую: ключ
`DCFApixels.WhimTex.PresetsFolder` не менялся. Если раньше библиотека находилась
через fallback, её можно снова выбрать вручную. Проверки допустимости пути,
уведомление о смене папки и Reset сохранены.

Getter и Reset не создают папки, не перемещают, не удаляют и не переписывают файлы.
Совместимость форматов TIFF/JSON и пресетов 0.12.5 этим этапом не менялась.
Политика настроек и ручной выбор библиотеки отражены в AGENTS, DECISIONS,
HANDOFF, AgentAPI и инструкциях painting на EN/RU/ZH.

### Проверки пятого этапа

Штатная компиляция подключённого Unity Editor Test6.6 завершилась без ошибок.
Все **10 запущенных C# smoke-файлов** прошли:

| Проверка | Результат |
| --- | --- |
| UserSettingsCleanupSmoke.Run | 42; старые ключи игнорируются, новые сохраняются/восстанавливаются, reset/path validation, байты пресета 0.12.5 не меняются |
| PaintToolSettingsSmoke (eval_file) | 45 |
| SharedPresetLibrariesSmoke (eval_file) | 90; общие библиотеки, выбор папки и каталоги |
| PostFxBackgroundSettingsSmoke (eval_file) | 18 |
| GuideSettingsSmoke (eval_file) | 12 |
| ContextToolsSmoke.Main | 701; инструменты, временное переключение, Escape, capture, Undo и prefs |
| Compatibility0125Smoke.Run | 366; семь исходных TIFF/JSON, Drawing bytes, render/save/reopen, кисть и градиент |
| Compatibility0125ReaderSmoke.Run | 20; bounded discard и защита неизвестных данных |
| ShaderFX0125PresetSmoke.Run | 10 408; пять FX, linked catalog refresh, параметры/group header, render parity, TIFF и native FX presets |
| CanvasTerminologyMigrationSmoke.Run | 27 |

В HealingBrushSmoke, HealingPerimeterSmoke и HealingNoiseSeamDiagnostic обновлён
только ключ prefs. Все три проверены через `run_script --dry_run`: ошибок
компиляции нет; сами healing-проверки на этом этапе не запускались.

Все **73 JS тестовых файла** завершились с кодом 0. Новый
`UserSettingsCleanup.test.mjs` добавлен в documentation CI. Source-проверка
документации (84 страницы EN/RU/ZH), генераторы API/brush schema (`--check`)
и `git diff --check` — PASS. Замороженные fixtures не менялись.
Проверки восстановили затронутые prefs и очистили собственные временные папки.
Jekyll/GitHub Pages и player build не запускались. Версия, CHANGELOG и
Compact defaults v1 не менялись; commit/push не выполнялись.

Следующие отдельные этапы: gradient bridges и ручные FX-параметры.

## Выполнено: rename markers и встроенные FX — 4 октября 2026

Удалены все **22 `MovedFrom` и 9 `FormerlySerializedAs`** из runtime: модель,
Layer/behaviours, ShaderFX, alias `groupToggleParameter`, состояние окна и направляющие.
Удалены pending rename-комментарии, ненужные imports и общий поиск исторических
type/field names в document reader: миграционный cache, сканирование атрибутов,
извлечение прежних строк. Разрешение точных имён внешних типов сохранено.

Канонические имена не менялись: writer 0.12.5 уже записывает их. Новая карта
aliases для этого этапа не понадобилась. Узкий `WhimTexFileCompatibility0125`
ранее снятых полей остаётся. Неизвестные поля/типы диагностируются и блокируют
перезапись с потерей данных; старые layouts и API-алиасы не восстанавливаются.

Из пяти встроенных пресетов **Color Filter, Negative, Mask, Gradient Map, HSV**
сняты `@formerlyserializedas(_Amount/_Density)`. В исходном теге все пять уже
объявляют `_Opacity`. Шейдерная математика, `groupHeaderParameter`, metadata,
GUID/meta исходников и самостоятельные FX `.asset` сохранены.

**Оставлено намеренно:** пользовательская HLSL-директива `@formerlyserializedas`.
Это действующая функция переименования параметров FX/кистей и metadata исходников,
которое может присутствовать в файлах 0.12.5. Парсер, перенос совместимых значений/ID,
валидация и экспорт остаются; это не Unity-атрибут и не поддержка старых версий пакета.

Тесты актуализированы, не удалены ради зелёного результата:
`CanvasTerminologyMigrationSmoke` проверяет актуальную сериализацию окна/направляющих,
`DocumentMigrationProbe` — канонические имена и диагностику,
`DocumentReliabilitySmoke` — точный тип и прежние проверки сохранности.
Branding, Canvas Terminology и Negative FX больше не требуют исторических markers.

Новая база [ShaderFX0125](../Tests~/Fixtures/ShaderFX0125/README.md) — точные HLSL blobs
из тега `v0.12.5`, не результат текущего writer. Начальная TIFF/JSON база и 30 хешей
не изменены. Атрибуты Git фиксируют байты/переводы строк эталонов при checkout.
Сравнение обеих версий HLSL проводится в текущем Editor, а не выдаётся за старый GPU-рендер.

### Проверки четвёртого этапа

Штатная компиляция подключённого Unity Editor Test6.6 завершилась без ошибок.
Все **10 запущенных C# smoke-файлов** прошли:

| Проверка | Результат |
| --- | --- |
| ShaderFX0125PresetSmoke.Run | 10 408; пять FX, linked catalog refresh, значения/ID/group header, render parity, TIFF и native FX presets |
| Compatibility0125Smoke.Run | 366; семь исходных TIFF/JSON, Drawing bytes, render/save/reopen, кисть и градиент |
| Compatibility0125ReaderSmoke.Run | 20; bounded discard и защита неизвестных данных |
| CanvasTerminologyMigrationSmoke.Run | 27; каноническое состояние окна и направляющие |
| DocumentMigrationProbe (eval_file) | 11; имена и диагностика снятого поля |
| DocumentReliabilitySmoke.Run | 44; metadata, неизвестные поля/типы блокируют Save, Drawing/FX, Live и bindings |
| DocumentVectorWideningSmoke.Run | 254; автоматический/ручной reader и несовместимые данные |
| DocumentPayloadCoverageSmoke (eval_file) | 49; 15 behaviours, 826 полей |
| ShaderFXFormerlySerializedAsSmoke.Main | PASS; пользовательский HLSL rename, ID, validation, FX/brush roundtrip |
| ShaderFXControlSmoke.Main | 35; controls, Undo/Redo, preset export и TIFF |

Все **72 JS тестовых файла** завершились с кодом 0. Новый
`RenameMarkerCleanup.test.mjs` проверяет отсутствие Unity-маркеров и пять blob ID;
проверка добавлена в documentation CI. Source-проверка документации (84 страницы
EN/RU/ZH), генераторы API/brush schema (`--check`) и `git diff --check` — PASS.

Проверки используют только собственные временные fixtures и очищают их.
Замороженного отдельного ShaderFX `.asset` 0.12.5 по-прежнему нет:
native preset roundtrip здесь проверен текущим writer. Jekyll/GitHub Pages и player
build не запускались. Версия, CHANGELOG, Compact defaults v1 и исходная файловая
база не менялись; commit/push не выполнялись.

Следующие пункты — отдельная работа: старые prefs, gradient bridges и ручные FX-параметры.

## Выполнено: очистка clipboard — 4 октября 2026

Удалено:

- Reader `whimtex.layers` с отдельным parsing, `type/properties/fx`, локальными targets,
  `gradientOptions`, File asset resolver и whitelist/лимитами старого HLSL.
- Multi-image очередь старого JSON, подтверждение списка хостов, владение clipboard tree
  на время загрузок и отдельный override фильтра назначения.
- `BuildPortableSource`, `ExportPortableIncludes`, `ValidatePortableSource` и их вспомогательные
  таблицы/методы. Современные `BuildSource` и `ExportIncludes` сохранены.
- Drawing URL provenance: `PortableImageUrl`, `RememberImageUrl`, три сериализованных поля
  URL/revisions и их сбросы/счётчики в painting, HDR, healing и blur.
- Десять fixtures старого clipboard, старый linked-image JSON из документации,
  `layers.schema.json` и генератор старого envelope. Они восстановимы из Git.
  Пользовательские файлы не удалялись.

Сохранено:

- `whimtex.document`, предупреждения о сломанных FX, проверка targets, remapping ID,
  изменение размера Canvas и атомарный Undo/Redo вставки.
- Обычная вставка HTTP(S) картинки и URL tip кисти используют общий загрузчик одного изображения,
  с прежними ограничениями, отменой и освобождением ресурсов. Диалоги доверия кисти не обходятся.
- Drawing pixels, исходное разрешение, HDR, deferred TIFF и копирование слоёв между окнами.
- Узкий discard reader полей 0.12.5. Рабочая модель уже не хранит URL/revisions,
  но старые TIFF/JSON с этими полями по-прежнему читаются.
- Native API/brush определения вынесены в [agent-fields.schema.json](../Documentation~/AI/agent-fields.schema.json)
  и [генератор](../Documentation~/scripts/build-agent-fields-schema.mjs). Это не новый формат документа.
  Brush schema после переноса зависимостей не изменилась.

В регрессионном тесте обнаружен и исправлен дефект обычной URL-вставки: текстура должна
регистрироваться в Undo перед повторной регистрацией документа. Теперь Redo восстанавливает слой.
Добавлен [ImageUrlPasteSmoke](../Tests~/Legacy/ImageUrlPasteSmoke.cs): локальный HTTP-сервер, без
внешней сети и файлов, 535 проверок пикселей/alpha, размеров, fitting, Undo/Redo, отмены,
смены документа, владения decoded texture и лимита 64 MB.

Полезные clipboard-тесты актуализированы, не удалены: procedural/native paste, hierarchy,
FX/texture targets, палитры, SDF, projective transforms, группы, HLSL includes и параметры.
Проверки отменённых ограничений убраны. `ClipboardExamplesSmoke` теперь проверяет девять современных
рецептов через read/save/reopen, без обращения к старому reader; разница рендера равна 0.
Тесты системной копии сохраняют существующий native snapshot перед прогоном и восстанавливают его.

### Проверки третьего этапа

Unity `Test6.6`, `6000.7.0a6`: штатная компиляция завершилась без ошибок.
Повторно выполнены 30 C# smoke-файлов:

| Группа | Результат |
| --- | --- |
| Compatibility0125Smoke / ReaderSmoke | 366 / 20; семь frozen TIFF/JSON, точные Drawing bytes, рендеры и пресеты |
| DocumentStorageBoundarySmoke / PayloadCoverageSmoke | 14 / 49; 15 behaviour, 826 значений |
| DocumentJsonContract / Validation / Safety | 23 / 169 / 40 |
| ProceduralClipboard / PortableClipboard / CompactPortableClipboard | 76 / 20 / 53 |
| ClipboardExamples / BrokenFx | 9 рецептов, delta 0 / 266 |
| ImageUrlPaste / ImageClipboard | 535 / 51 |
| LayerClipboard / LayerClipboardWindow | 35 / 14; перенос между окнами |
| PortableIncludes / ShaderFXControl / UnifiedLighting | 14 / 35 / 667653 |
| AllBelowInput / OptionalLayerGradient | 181442 / 295003 |
| SdfControls / ProjectiveTransform | 33446 / 67796 |
| GroupFX | 10 |
| FillPattern / NoiseControls / SeamlessChannels | 2958670 / 129 / 2362650 |
| DrawingResolution / DeferredDrawing / DrawingReload | PASS / PASS / 40 |

Все 71 JS test-файла завершились без ошибок; необязательная TextMate-интеграция по-прежнему
пропускается без `WHIMTEX_VSCODE_APP`. Frozen hashes 0.12.5 проверены, fixtures не пересоздавались.
Document schema пересоздана штатным генератором: 31 model type.
Проверены схемы API/кистей, 38 samples, девять clipboard recipes и 84 страницы документации.
Полный Jekyll build / GitHub Pages deployment и player build не запускались.
DrawingReload проверяет lifecycle, не реальный domain reload и не незавершённые GPU strokes.

EN/RU/ZH инструкции обновлены. По старому URL reference оставлена короткая памятка:
до обновления вставить `whimtex.layers` в **0.12.5**, сохранить TIFF для Drawing pixels
или экспортировать `whimtex.document` JSON.

Следующий отдельный этап: rename markers/старые prefs, затем gradient bridges.
Они этим этапом не изменены. Весь рефакторинг пока не завершён; версия, commit и push не менялись.

## История второго этапа: удаление document .asset backend — 4 октября 2026

`TextureCompositor : ScriptableObject` остаётся рабочей моделью в памяти, не форматом файла.
Старые документы нужно открыть и сохранить в TIFF **через WhimTex 0.12.5 до обновления**.
После обновления нет чтения, записи, dry-run или миграции документов `.asset`.
Старые пользовательские файлы не удалялись и не изменялись.

Удалено:

- Writer `SaveLegacyAssetForCompatibility`, поиск композитора среди Unity sub-assets,
  меню/класс миграции и `WhimTexApi.Migrate` / `whimtex_document_migrate`.
- Старые compositor Inspector, Output Settings window/rows, Output Preview и его shader.
- Embedded output processing, compression/settings snapshot, Linked Output, custom sprites/slicing
  и отдельная Sprite Editor assembly/provider.
- Старый `LiveOutputSession` и ветвления окон/агентского API для document `.asset`.
- Прикрепление Drawing и встроенных ShaderFX к compositor asset.
- Неиспользуемые USS-селекторы старого окна Output; общие Layer Preview и Brush preview стили сохранены.

Сохранено:

- TIFF/JSON, reader снятых полей файлов 0.12.5 и неизменённые Compact defaults версии 1.
- Исходные Drawing, встроенные/внешние FX, композиция, Undo и операции со слоями.
- Нативный `TextureImporter`, его Sprite Editor, современный Live Update и File-layer связи.
- Самостоятельные ShaderFX пресеты `.asset`, Texture2D export `.asset` и обычные ссылки на Unity assets.
  Отказ от document `.asset` не означает запрета всех Unity assets.
- Замороженные исходники и SHA-256 базы. Семь TIFF/JSON входят в совместимость;
  `compositor.asset` оставлен только архивным отрицательным образцом и не импортируется в тестах.
  Одноразовый генератор старой базы удалён.

Тесты:

- Удалены 11 тестов исключительно снятого backend: `LegacySavePolicySmoke`, `LiveOutputSmoke`,
  `LinkedOutputSmoke`, `OutputCompressionSmoke`, `OutputProcessingSmoke`, `OutputTypeSmoke`,
  `SpriteSlicingSmoke`, `OutputSettingsSmoke`, `OutputSettingsLayoutSmoke`,
  `OutputSettingsUXSmoke`, `OutputSettingsWindowSmoke`.
- `DocumentPreparationSmoke` переведён с миграции SO asset на современный Drawing TIFF round-trip;
  сохранены проверки FX-пресетов, include, сжатого кеша и точности пикселей.
- `TiffAgentApiSmoke` и новый `DocumentStorageBoundarySmoke` проверяют отказ от старого формата,
  включая переименованный TIFF с расширением `.asset`, без записи файлов.
- `OutputDependenciesSmoke` сохраняет GPU/notification/cycle/cache проверки через публичную
  GPU-публикацию тестовой текстуры; реальный backend отдельно проверяет `DocumentLiveUpdateSmoke`.
- `CanvasFilterSmoke` сохраняет современные фильтры; проверки снятой live-сессии убраны.
- Node-контракты обновлены: нет legacy discovery/migration, 21 зарегистрированная команда.
- USS baseline выведен из проверенного исходного commit `e8070f0` с удалением только снятых правил,
  не перезаписан произвольным снимком нового UI.

### Проверки текущего этапа

Unity `Test6.6`, `6000.7.0a6`: компиляция штатным Editor без ошибок.

| Проверка | Результат |
| --- | --- |
| Compatibility0125Smoke | 366 проверок: семь TIFF/JSON open/render/save/reopen, точные Drawing bytes, кисть/градиент |
| Compatibility0125ReaderSmoke | 20 guards: bounded discard и защита неизвестных данных |
| DocumentStorageBoundarySmoke | 14 проверок отказа от document asset, включая disguised TIFF и отсутствие writer/migration |
| DocumentPreparationSmoke | 36 проверок современного хранения, внешних FX и include |
| DocumentReliabilitySmoke | 44 проверки сохранения, GUID/meta и Live Update |
| DocumentRoundTripSmoke | 33 проверки, разница композиции 0 |
| DocumentPayloadCoverageSmoke | 49 проверок, 15 behaviour, 826 изменённых значений |
| DocumentJsonContractSmoke / Validation / Safety | 23 / 169 / 40 |
| DocumentLiveUpdateSmoke | 19 проверок публикации, сохранения и восстановления native importer |
| OutputDependenciesSmoke / CanvasFilterSmoke | 18 / 22 |
| OutputEncodingSmoke | 828 |
| ApplyFXSmoke / AgentEditingSmoke | 387 / 226 |
| MergeLayersSmoke / FileNavigationSmoke | 20 / 16 |
| TiffAgentApiSmoke / TiffLiveSmoke / CanvasToolbarSmoke | PASS |
| CanvasTerminologyMigrationSmoke | 27 |
| DocumentSaveTailProbe | dry-run compilation PASS; профилирование не запускалось |

Node: все 71 `*.test.mjs` завершились без ошибок. В одном файле необязательная TextMate
интеграция пропущена: `WHIMTEX_VSCODE_APP` не задан. USS — 75 property fingerprints,
25344 reference checks. Документация — 84 страницы, локальные ссылки и EN/RU/ZH navigation PASS.
Полный локальный Jekyll build / GitHub Pages deployment не выполнялся; Ruby/Bundler отсутствуют в PATH.
Player build не запускался.

Оставшиеся группы — старый clipboard `whimtex.layers`, rename markers/старые prefs,
градиентные bridges и portable-source helpers — этим этапом не удалены.
Следующий шаг: отдельная очистка clipboard с проверкой JSON/пресетов 0.12.5.


## История первого этапа: файловая база и первые удаления

- Снята [база файлов 0.12.5](../Tests~/Fixtures/Compatibility0125/README.md) на исходном runtime тега:
  восемь документов, кисть, градиент, `.meta`, SHA-256 и неравномерные эталонные рендеры.
  Исходные SDR/HDR Drawing проверяются по точным байтам; итоговые изображения — с GPU-допуском.
- `WhimTexFileCompatibility0125` задаёт узкий перечень снятых полей по владельцу.
  TIFF/JSON writer больше не записывают старые output/slices и Drawing URL/revisions.
  Binary reader потребляет их без поиска снятых типов/GUID, сохраняет номера объектов
  и ограничивает размеры/глубину. Активные ссылки на снятые объекты отклоняются.
  Неизвестные данные вне перечня по-прежнему защищены от потери при сохранении.
- Удалены четыре API-алиаса: `UsePreviewChannels`, `PreviewTitle`, `ImmediatePreviewUpdates`,
  `RequestPreview`. Канонические Canvas/Layer Preview API сохранены. Тесты и описание HDR обновлены.
- Удалены меню создания композитора `.asset` и отключённый `TextureCompositorProjectPreview`
  вместе с `.meta` и вызовом очистки кеша. Из диагностического теста убрана неактуальная
  попытка включать/выключать удалённый callback. Эти удаления восстанавливаются через Git.
  `NoiseContract` также актуализирован: проверяет канонический immediate-preview hook,
  а не удалённый виртуальный мост.
- JSON-схема пересоздана; `spriteSlices` осталось только deprecated входным полем для
  Full-файлов 0.12.5. Compact defaults v1 не изменены. Проверка эталонов добавлена в docs CI.

### Что ещё не удалено

Старые Output Settings/UI, Live Output, linked-output и sprite-subasset backend пока
остаются. `SaveLegacyAssetForCompatibility` временно нужен их существующим тестам,
но пользовательское создание `.asset` через меню уже убрано. Чтение `.asset` 0.12.5 сохранено.
Для удаления writer сначала перевести тесты файлового импорта на замороженные образцы,
а проверки удаляемого старого поведения убрать вместе с самим backend, не раньше.
Общие MovedFrom/FormerlySerializedAs, старый clipboard и прочие пункты инвентаризации
этим этапом не удалены. Покрытие внешних ссылок, отдельных FX/HLSL, textured brushes
и slicing требуется расширить перед удалением соответствующих маршрутов.

### Проверки первого этапа

Unity Editor `Test6.6`, `6000.7.0a6`: штатная перекомпиляция завершилась без ошибок.

| Проверка | Результат |
| --- | --- |
| Compatibility0125Smoke | 415 проверок; 8 файлов открываются, рендерятся и пересохраняются в TIFF без изменения эталонных исходников |
| Compatibility0125ReaderSmoke | 20 проверок; неизвестные типы/GUID снятых полей не разрешаются, неверные ссылки/длины/глубина/теги отклоняются |
| DocumentReliabilitySmoke | 44 проверки; Live Update, GUID/.meta, Drawing и запрет потери неизвестных данных |
| DocumentRoundTripSmoke | 33 проверки; round-trip модели/FX/Drawing, разница итогового рендера 0 |
| DocumentPayloadCoverageSmoke | 49 проверок, 15 типов behaviour, 826 изменённых значений |
| DocumentJsonContractSmoke | 23 проверки |
| DocumentJsonValidationSmoke | 169 проверок |
| DocumentJsonSafetySmoke | 40 проверок |
| CanvasTerminologyMigrationSmoke | 27 проверок |
| LegacySavePolicySmoke | PASS; исходный `.asset` не изменяется, миграционное сохранение пишет TIFF |
| DocumentSaveTailProbe | Успешная компиляция dry-run; профилирование не запускалось |

Node: все 71 тестовых файла завершились без ошибок; необязательная интеграция
TextMate пропущена, поскольку `WHIMTEX_VSCODE_APP` не задан.
В схеме проверены 38 sample-документов, 9 clipboard recipes и 4 замороженных JSON 0.12.5.
Проверка исходников документации: 84 страницы, PASS. Полная сборка Jekyll локально
не запускалась: Ruby/Bundler не найдены в PATH; успешный GitHub Pages build этим
прогоном не подтверждён. В workflow добавлены проверки эталонов для следующего CI-запуска.

## Исходная инвентаризация: выводы до удаления backend

Ниже описано состояние кода тега 0.12.5; выполненные пункты сверять с результатами выше.

Самые крупные кандидаты на удаление — старые документы-композиторы `.asset` со своей системой Output Settings, отдельным Live Update и спрайтовыми subasset, а также старый входной формат буфера обмена `whimtex.layers`. Они поддерживают параллельные пути выполнения, которые не нужны после явного перехода через `0.12.5` на TIFF/JSON.

Однако «удалить всё старое» нельзя свести к удалению всех `legacy`, `MovedFrom` и `FormerlySerializedAs`. Версия `0.12.5` сама записывает некоторые устаревшие поля в TIFF и допускает сохранённые ручные параметры ShaderFX. Чтобы сохранить её файловые данные и результат изображения, часть очистки требует узкого адаптера чтения `0.12.5`.

Граница по уточнению пользователя:

- Сохранять обратную совместимость **только TIFF/JSON и пресетов `0.12.5`, без композиторов `.asset`**: чтение содержимого, значений, ссылок и результата изображения; возможность редактировать и пересохранить данные новой версией.
- Публичный/защищённый C# API, API расширений, агентский API, команды и их аргументы можно менять без совместимых алиасов и старых маршрутов.
- Не обещать совместимость EditorPrefs, прежних layouts/состояния окон и старого поведения API. Это не разрешение удалять пользовательские файлы или терять ссылки на них.
- Убрать прежний рабочий маршрут композиторов `.asset`, старый `whimtex.layers` и общую поддержку древних имён типов. Если данные такого вида записаны самой `0.12.5` в поддерживаемый файл, оставить только необходимый файловый импорт/преобразование.
- Старые проекты переводить в поддерживаемую форму средствами `0.12.5` до обновления пакета.
- Не считать установку `0.12.5` миграцией: она не переписывает автоматически все документы, пресеты и ссылки.

### Что относится к файловому контракту

**Форматы данных.** TIFF, сериализатор, JSON и библиотека кистей имеют собственные версии формата, сейчас преимущественно `1`. Это не номер пакета. Нельзя определить происхождение файла условием «версия равна 0.12.5». Нужны зафиксированные образцы и явный перечень допустимых полей/типов.

**Файлы и исходники.** Проверять TIFF, JSON во всех режимах, файлы кистей/градиентов, пользовательские ShaderFX presets и HLSL, а также ссылки и настройки Unity `.meta`, от которых зависит документ. Возможность менять API не разрешает перестать понимать данные, которые были записаны этими API в файл.

**Старые композиторы `.asset`.** Предложенный маршрут перехода — конвертация в TIFF/JSON средствами `0.12.5`. Но в `0.12.5` ещё существует `CreateAssetMenu` старого композитора: расширение `.asset` само по себе не доказывает, что файл создан до этой версии. Для файлов, действительно созданных/сохранённых `0.12.5`, нужен ограниченный импорт с переводом в новую модель. Старые writer/UI/output runtime всё равно можно убрать. Считать полное удаление reader безопасным нельзя без проверки файлового baseline.

## 1. Удалить после подготовки перехода через 0.12.5

«Удалить» в этом разделе означает удалить поддержку старого пути, а не общие компоненты, которые он использует. Поля, попавшие в сохранённый TIFF `0.12.5`, требуют обработки из раздела 2.

| № | Место | Что убрать или упростить | Граница удаления |
| --- | --- | --- | --- |
| 1 | `LegacyAssetWriter` (удалён) | `SaveLegacyAssetForCompatibility`: создание старого `.asset`, embedded texture/sprite, сохранение подассетов и старого Output. | В текущем потоке это вспомогательный писатель для совместимости и тестов. Вместе с ним убрать создание старого композитора через `CreateAssetMenu` в [TextureCompositor](../src/TextureCompositor.cs). Сам класс композитора сохранить: это модель текущего документа. |
| 2 | `WhimTexLegacyMigration` (удалён), `API Migration` (удалён), [Pipeline commands](../src/Automation/Pipeline/WhimTexCommands.cs) | Меню миграции `.asset → TIFF`, `WhimTexApi.Migrate`, команда `whimtex_document_migrate`. | Удалить прежний публичный маршрут без API-алиаса. Нормализованные TIFF/JSON читать напрямую. Выполнено: композиторы `.asset` исключены из совместимости по уточнению пользователя; reader и runtime удалены. |
| 3 | [API Assets](../src/Automation/WhimTexApi.Assets.cs), [API](../src/Automation/WhimTexApi.cs), [Inspect](../src/Automation/WhimTexApi.Inspect.cs) | Разрешение `.asset` в `DocumentPath`, fallback `Load` через `FindDocument`, ответы `legacy_read_only`, ветви старого документа в inspect/discovery и dry-run. | Оставить `.tiff`/`.json`, общие проверки revision и dry-run. Не удалять импорт обычных изображений или проверки безопасности только потому, что они находятся рядом. |
| 4 | [Window DocumentFile](../src/TextureCompositorWindow.DocumentFile.cs), [Window UI](../src/TextureCompositorWindow.UI.cs) | Ветвления Save/Save As для legacy asset, сообщения о миграции, переход в старый Output Settings. | Сохранить Save As для нового документа и JSON/TIFF. Условия вокруг строк 108–127 в DocumentFile можно свести к одному современному маршруту. |
| 5 | `TextureCompositorEditor` (удалён), `OutputSettingsWindow` (удалён), `OutputSettingsRow` (удалён) | Старый инспектор композитора, окно Output Settings, его валидатор/строки и добавление UI в инспектор старой текстуры. | Вырезать legacy UI вместе с вызовами. Настройки текущего TIFF принадлежат `TextureImporter`; они не должны исчезнуть. |
| 6 | `OutputPreview` (удалён), `OutputPreview.shader` (удалён) | Preview mip/channel для старого окна Output Settings. | У класса обнаружен только потребитель OutputSettingsWindow. Перед удалением шейдера проверить остальные ссылки. [LayerPreviewPanel](../src/Editor/LayerPreviewPanel.cs) — самостоятельная актуальная функция, её оставить. Общие USS-классы не удалять по имени `output-preview`: их использует Layer Preview. |
| 7 | `OutputProcessing` (удалён), `LinkedTexture` (удалён), `OutputSettingsState` (удалён) | Отдельная обработка legacy output, запись Linked Output, snapshot/revert старых Output Settings. | Linked Output здесь — старый вывод композитора, а не File-layer ссылки и не современное редактирование исходного изображения. Сохранить обычный экспорт и современное кодирование TIFF. |
| 8 | [TextureCompositor.Assets](../src/TextureCompositor.Assets.cs) | Старые enum/OutputSettings, создание embedded output, `FindDocument` для `.asset`, старый publisher и восстановление его выходных ссылок. | Это смешанный файл: не удалять целиком. `OutputTexture`/`OutputSprite`, уведомления и часть привязок нужны текущему TIFF. Поля старого output сначала обработать в адаптере чтения. |
| 9 | `TextureCompositor.Sprites` (удалён), `SpriteDataProvider` (удалён), `SpriteEditorBridge` (удалён) | Старые `SpriteSlice`, embedded Sprite, собственный provider для композитора `.asset`, мост и при отсутствии других потребителей его assembly definition. | Сохранить нативные настройки Sprite/Slicing в `TextureImporter` и `.meta` TIFF. Наличие слова `SpriteEditor` само по себе не делает код легаси. |
| 10 | `TextureCompositorProjectPreview` — удалён на первом этапе | Старые project icons и cache для композиторов `.asset`. | Удалены отключённый класс, его `.meta`, вызов очистки кеша и диагностическое переключение callback. Нативные TIFF icons не затронуты. |
| 11 | `LiveOutputSession` (удалён), [Window LiveOutput](../src/TextureCompositorWindow.LiveOutput.cs), [TextureCompositor.Assets](../src/TextureCompositor.Assets.cs) | Отдельную live-сессию embedded output и ветви `liveOutputEnabled` для старого композитора. | У `LiveOutputSession` найдено создание только в старой модели output. Современный TIFF использует отдельный [WhimTexDocumentSession](../src/Editor/WhimTexDocumentSession.cs); его GPU-публикацию, восстановление readable и save-паузы сохранить. |
| 12 | [API Clipboard](../src/Automation/WhimTexApi.Clipboard.cs) | Входной reader `whimtex.layers`, его format-1 parsing, специальные `gradientOptions`, `targets`, старое представление FX, очередь image URL. | `0.12.5` пишет `whimtex.document`. Сохранить современную ветвь чтения, трансформы назначения, проверки targets и управление временем жизни данных. Старый JSON перед обновлением нужно вставить и пересохранить через `0.12.5`. |
| 13 | [Window ImageUrl](../src/TextureCompositorWindow.ImageUrl.cs) | Async-очередь и multi-image batch, обслуживающие старый `whimtex.layers`. | Не удалять обычную вставку URL изображения, актуальные операции image import или `PreparePortableDestination` только из-за исторического имени: подготовка назначения нужна и текущему clipboard. |
| 14 | [ShaderFXPresetWriter](../src/ShaderFXPresetWriter.cs), [ShaderFXSourceBuilder](../src/ShaderFXSourceBuilder.cs) | `BuildPortableSource`, `PortableIncludes`, `ValidatePortableSource`, `ExportPortableIncludes` после удаления их старых потребителей. | У `BuildPortableSource` не обнаружено рабочих вызовов вне тестов. Обычные `BuildSource` и `ExportIncludes` используются текущими HLSL/JSON и должны остаться. |
| 15 | [GradientClipboard](../src/Editor/WhimTexGradientClipboard.cs), [API Layers](../src/Automation/WhimTexApi.Layers.cs) | Префикс `WhimTex.Gradient/1`, старые color-объекты `{r,g,b,a}`, конвертацию enum из чисел, приём retired `transition`. | Современный writer использует `whimtex.gradient`, RGBA-массивы и строковые enum. Обычные массивы градиента, допустимые unwrapped-объекты и Markdown fences — текущий удобный ввод, их не считать легаси. |
| 16 | [DocumentJson](../src/WhimTexDocumentJson.cs), [DocumentJsonSchema](../Documentation~/scripts/DocumentJsonSchema.cs) | Неиспользуемый root `kind`, который reader ещё принимает, а writer уже не выводит. | Не путать с `ShapeLayerBehaviour.kind`: это действующий тип фигуры. Сначала менять генератор схемы, затем производные схемы и проверки. |

### Ограничение старой миграции `.asset → TIFF`

В `0.12.5` мигратор создаёт новый TIFF и оставляет исходный `.asset`. Это не гарантирует автоматический перенос всех внешних ссылок и настроек импорта. Новый файл получает другую asset identity; материалы, ссылки и Sprite-настройки нужно проверить отдельно. Нельзя обещать, что одной кнопкой переводится любой старый проект.

Также нельзя расширять обещание перехода на древние структуры, которые уже не поддерживает сама `0.12.5`. Точку перехода следует проверять на конкретных исходных версиях и образцах.

## 2. Упростить с адаптером данных 0.12.5

Это не поддержка всех прошлых версий. Это небольшой, явно ограниченный слой, который принимает сохранённую форму `0.12.5` и переводит её в новую модель.

### 2.1. Старые поля всё ещё попадают в современный TIFF

[WhimTexDocumentSerializer](../src/WhimTexDocumentSerializer.cs) пропускает кеши и временные поля, но в список `SkippedFields` не входят `outputSettings`, `savedOutputSettings`, `spriteSlices`, `originalImageUrl`, `originalImageRevision`, `pixelsRevision`. Сериализация проходит по сериализуемым полям и сохраняет имена типов.

У [JSON writer](../src/WhimTexDocumentJson.cs) другой список исключений: `outputSettings`, `savedOutputSettings` и URL/revision Drawing там исключены, но `spriteSlices` не исключён. Следовательно, TIFF и JSON нельзя считать эквивалентными по присутствию этих полей; конкретный JSON mode и значения тоже имеют значение.

Удаление полей и типов без адаптера опасно: [WhimTexDocumentFile](../src/WhimTexDocumentFile.cs) выставляет предупреждение при неизвестных полях/типах, запрещает сохранение неполностью прочитанного документа и отклоняет неполную editable copy.

Что требуется:

1. Зафиксировать реальные TIFF/JSON, записанные `v0.12.5`, а не только искусственные payload.
2. Явно описать retired-поля `0.12.5`, которые допустимо пропустить без потери актуального содержимого.
3. При чтении безопасно пропускать их значения/поддеревья без необходимости создавать удалённые legacy-типы.
4. Не снимать глобально защиту от неизвестных данных. Новое неизвестное поле или пропавший пользовательский тип всё ещё должны вызывать предупреждение.
5. Сохранённый новой версией документ уже не должен содержать retired-поля.

| № | Место | Возможное упрощение | Что требуется для 0.12.5 |
| --- | --- | --- | --- |
| 17 | `OutputSettingsState` (удалён), `Sprites` (удалён), [Assets](../src/TextureCompositor.Assets.cs) | Удалить legacy output/sprite-модель из runtime, а не таскать её в каждом TIFF. | Явное пропускание соответствующих старых полей и типов при чтении. Актуальные Sprite-параметры брать из TIFF importer, не из discarded legacy-модели. |
| 18 | [DrawingLayerBehaviour](../src/Layers/DrawingLayerBehaviour.cs), [Drawing HDR](../src/Layers/DrawingLayerBehaviour.Hdr.cs) | Удалить `PortableImageUrl`, `RememberImageUrl`, origin URL/revision и счётчик, если после удаления их проверок он больше нигде не нужен. | Сейчас `PortableImageUrl` не имеет потребителей; revision связан с этой проверкой. Сохранить Drawing pixels, HDR, Undo и остальные актуальные настройки. Старые поля TIFF пропускать явно. |
| 19 | [Noise](../src/Layers/NoiseLayerBehaviour.cs), [FillPatternSettings](../src/Layers/FillPatternSettings.cs), [Shape](../src/Layers/ShapeLayerBehaviour.cs) | Убрать sentinel-значения и fallback из runtime: хранить уже вычисленные размеры/roundness. | `scaleY = 0`, `warpScaleY = 0`, `sizeY = 0` и отрицательный `cornerRoundness` — допустимые текущие значения `0.12.5`, а не только мусор старых версий. Нужна нормализация с сохранением изображения. |
| 20 | [DrawingLayerBehaviour](../src/Layers/DrawingLayerBehaviour.cs), [API Paint](../src/Automation/WhimTexApi.Paint.cs) | Упорядочить дублирование brush-параметров слоя и глобальных Paint settings, изменить defaults агентского рисования. | Старое поведение `GetStrokeParameters` сохранять не требуется. Сериализованные значения слоя нужно явно перенести в новую структуру либо признать retired tool-state; pixels и изображение файла должны сохраниться. |
| 21 | [DocumentFile window](../src/TextureCompositorWindow.DocumentFile.cs) | Удалить upgrade-ветвь «layout без owner GUID», сократить дублирование привязки path/GUID с DocumentService. | Старые layouts можно не восстанавливать. Нужна корректная привязка нового окна к документу и работа нового Save/Save As, а не совместимость прежнего состояния окна. |
| 22 | [Window Tools](../src/TextureCompositorWindow.Tools.cs) | Удалить input-перенос `blurOpacity → blurFlow`. | Это совместимость настроек, а не файлов документа; мост можно убрать без предварительной записи prefs в `0.12.5`. |
| 23 | [UserSettings](../src/WhimTexUserSettings.cs) | Убрать fallback пользовательской библиотеки на папку `SpriteEditor`. | Старый default path сохранять не обязательно. Но `0.12.5` может хранить там действительные файлы пресетов: не удалять их и обеспечить явное открытие/импорт библиотеки из выбранной папки. Автоматический перенос — отдельная операция. |

### 2.2. Зафиксированные defaults — часть формата

[WhimTexJsonDefaultsV1](../src/WhimTexJsonDefaultsV1.cs) хранит snapshot значений, используемых компактным JSON. В нём есть старые поля и sentinel-значения.

Нельзя просто пересоздать этот snapshot по новым конструкторам и оставить тот же смысл версии `1`: отсутствующее поле старого compact JSON начнёт означать другое значение. Возможные решения — сохранённый decoder/defaults для формы `0.12.5` и новая схема записи либо явно версионированная нормализация. Удалённые runtime-типы не должны требоваться для раскрытия старого snapshot.

## 3. Rename/migration-механизмы

| № | Место | Что можно убрать | Условие |
| --- | --- | --- | --- |
| 24 | [Layer](../src/Layers/Layer.cs), классы в `src/Layers`, [TextureCompositor](../src/TextureCompositor.cs), [ShaderFX](../src/ShaderFX.cs) | `MovedFrom` прежнего namespace/assembly проекта. | После нормализации документов и отдельных сохраняемых объектов в `0.12.5`. Её современный writer хранит текущие имена типов; простая установка версии не переписывает старые Unity assets. |
| 25 | [Window Channels](../src/TextureCompositorWindow.Channels.cs), [Guides](../src/TextureCompositorWindow.Guides.cs), [GuideCommands](../src/TextureCompositorWindow.GuideCommands.cs), [Inspector](../src/TextureCompositorWindow.Inspector.cs), [Tiling](../src/TextureCompositorWindow.Tiling.cs) | Старые field aliases `preview*` и `MovedFrom` для `PreviewGuide`/`PreviewGuideSettingsWindow`. | Для состояния окна/layout можно удалить без переходного моста. Если данные направляющих присутствуют в самостоятельном поддерживаемом файле, их reader оценивать отдельно как файловый контракт. |
| 26 | [ShaderFX](../src/ShaderFX.cs) | `FormerlySerializedAs("groupToggleParameter")` для `groupHeaderParameter`. | `0.12.5` сохраняет актуальное имя. Само поле, его значение и поведение сохранить. Для внешних `.asset` FX нужна явная пересериализация. |
| 27 | [DocumentSerializer](../src/WhimTexDocumentSerializer.cs) | Общий поиск прежних field/type names: reflection по `FormerlySerializedAs` и `MovedFrom`, сканирование миграционных атрибутов. | Заменить прямым разрешением имён и ограниченной картой изменений относительно файлов `0.12.5`. Механизм расширений можно менять; сохранённые данные нельзя терять молча, а отсутствующий внешний тип по-прежнему должен давать диагностику. |
| 28 | [DocumentSerializer](../src/WhimTexDocumentSerializer.cs) | Исключение retired gradient `transition`, старые scalar/vector widening и coercion. | После проверки canonical payload `0.12.5`. Оставить действующие теги и необходимые преобразования файловых данных. Интерфейс ручной сериализации можно менять, но его данные в поддерживаемых файлах должны иметь путь чтения или явную диагностику неизвестного типа. |

Правило [AGENTS.md](../AGENTS.md) сейчас требует сохранять существующие `MovedFrom`. При реализации новой политики его нужно согласованно обновить, зафиксировав исключение и точку перехода. В рамках аудита правило не менялось.

## 4. FX: не всё старое на вид является легаси

| № | Место | Оценка | Возможное действие |
| --- | --- | --- | --- |
| 29 | [ShaderFX Catalog](../src/ShaderFX.Catalog.cs), [API LiveFx](../src/Automation/WhimTexApi.LiveFx.cs), [ShaderFXParameter](../src/ShaderFX.cs) | Ручные параметры без `@param` ещё являются допустимыми сохранёнными данными `0.12.5`. | Старый API-вход manual parameters можно удалить. Для файлов сначала конвертировать сохранённые описания: типы, имена uniform, значения, ranges, группы, текстуры и IDs. После этого runtime можно унифицировать вокруг code-declared parameters. |
| 30 | [ShaderFXEditor](../src/Editor/ShaderFXEditor.cs) | Старый `ShaderFXParameterDrawer` не нужен основному UI с `ShaderFXParameterView`; возможные внешние потребители больше не ограничивают очистку. | Удалить после проверки внутренних потребителей. Reader сохранённых параметров FX должен работать независимо от наличия этого drawer. |
| 31 | [ShaderFXMetadata](../src/ShaderFXMetadata.cs) | `PreserveValues` объединяет duplicate/manual данные и перенос значений при редактировании HLSL. | После конвертации manual-параметров можно убрать их специальные приоритеты и лишние fallback. Перенос значений при текущем authoring — отдельная полезная функция. |
| 32 | [ShaderFXMetadata](../src/ShaderFXMetadata.cs), [FX presets](../src/FXPresets) | `// @formerlyserializedas` — не только совместимость пакета, но и grammar пользовательского HLSL в `0.12.5`. Директива есть в актуальных ColorFilter, GradientMap, HSV, Mask и Negative. | Сохранить как authoring-функцию либо отдельно объявить её удаление с нормализацией HLSL. Сохранение TIFF само по себе не переписывает внешний `.hlsl`. Исторические aliases встроенных пресетов можно снять после переноса baseline-параметров. |
| 33 | [SourceBuilder](../src/ShaderFXSourceBuilder.cs), [ShaderFX](../src/ShaderFX.cs) | `UpgradeTransformHelpers`/`upgradedTransformShader` поддерживают прежние generated affine helpers. | Кандидат на удаление после проверки повторной компиляции FX `0.12.5` современным builder. TIFF не хранит compiled/applied кеши; отдельные ShaderFX `.asset` и внешние sources проверить отдельно. Сами актуальные projective helpers сохранить. |

Старый **композитор** `.asset` и актуальный **ShaderFX preset** `.asset` — разные сущности. Удаление первого не разрешает удалить второй, его каталог и создание пресетов.

## 5. API можно менять без обратной совместимости

Решение принято: публичный C# API и агентский API не входят в гарантию совместимости `0.12.5`. Следующие алиасы можно удалить, обновив внутренних потребителей и тесты:

| Место | Устаревший контракт | Основной контракт |
| --- | --- | --- |
| [WhimTexColorField](../src/Editor/WhimTexColorField.cs) | `UsePreviewChannels` | `UseCanvasChannels` |
| [Utils](../src/Utils.cs) | `PreviewTitle` | `LayerPreviewTitle` |
| [Utils](../src/Utils.cs) | `ImmediatePreviewUpdates` | `ImmediateLayerPreviewUpdates` |
| [Utils](../src/Utils.cs) | `RequestPreview` | `RequestLayerPreview` |

Также можно удалить `WhimTexApi.Migrate`/`whimtex_document_migrate`, старые аргументы и формы ответов, compatibility overloads и мосты для внешних инспекторов. Старые вызовы больше не обязаны компилироваться или выполняться. Документацию и собственные интеграции нужно привести к новому контракту, без обязательного переходного API.

[Layer](../src/Layers/Layer.cs) содержит implicit/explicit conversions, forwarded properties и автоматическое создание владельца `LayerBehaviour`. Их публичность больше не мешает рефакторингу. Но это действующая архитектура, а не автоматически бесполезное легаси: оценивать внутренние вызовы, ownership и lifecycle; при изменении сериализуемой модели сохранить чтение файлов `0.12.5` через явное преобразование.

Нельзя путать имя метода с именем в файле. Метод можно переименовать без алиаса; если переименовано сериализованное поле или тип, прежнее имя из файла `0.12.5` должно обрабатываться адаптером. То же относится к единому JSON: старый агентский endpoint можно удалить, но сохранённый `whimtex.document` остаётся файловым контрактом.

## 6. Остальные границы очистки

- Старые **ключи EditorPrefs и layouts не требуют совместимости**: `PaintingPreviewScale`, `DCFApixels.WhimTex.PreviewTool`, `ReturnToolPreview…`, `DCFApixels.WhimTex.Preview.*` можно переименовать/убрать без migration reader. Они вынесены из файловой гарантии. Существующие файлы пользовательской библиотеки при этом не удалять.
- Unity-version условные ветви для поддерживаемых редакторов: [UnityObjectID](../src/UnityObjectID.cs), serializer и другие `UNITY_6000_*`. Минимальная Unity в пакете — `6000.0`; удаление таких ветвей требует отдельного изменения поддержки Unity, а не только пакета WhimTex.
- Неизвестные/отсутствующие слои, внешние типы, missing assets, сохранение исходника неудачного FX, path/GUID fallback, транзакционное сохранение и recovery. Это защита актуальных данных, а не обслуживание старых версий.
- File-layer ссылки, пользовательские ShaderFX `.asset`, обычный экспорт Texture2D, native TIFF Sprite settings, Live Update TIFF, HDR Drawing, Undo и восстановление после domain reload.
- `TextureCompositorWindow`, `ComposePreview`, `RenderPreview`, `RenderCachedPreview`, `_PreviewScale`, `PostFxPreview*` лишь по их именам. В этих случаях имя не доказывает устаревший путь.
- `.meta` GUID сохраняемых компонентов и ассетов. Удаление `.meta` допустимо вместе с действительно удаляемым файлом после проверки ссылок, но не как способ «обновить» текущую identity.
- Историю `CHANGELOG`, отчёты старых прогонов и опубликованные release notes. Они описывают прошлое, а не исполняют совместимость. Архивирование — отдельная уборка, не обязательная часть runtime-рефакторинга.

## 7. Тесты, документация и схемы, связанные с удалением

Тесты нельзя удалять только потому, что в имени есть `Legacy`, `Migration`, `Portable` или `Output`: часть проверяет актуальные свойства результата. Сначала выделить удаляемый контракт, затем заменить полезное покрытие современными fixtures.

| Группа | Кандидаты | Как поступить |
| --- | --- | --- |
| Старый save/migration | [LegacySavePolicy.test.mjs](../Tests~/Legacy/LegacySavePolicy.test.mjs), `LegacySavePolicySmoke` (удалён), [DocumentMigrationProbe](../Tests~/Legacy/DocumentMigrationProbe.cs) | Снять проверки старого writer/мигратора и общего поиска древних aliases после удаления контракта. Вместо них проверить canonical данные `0.12.5`, controlled discard и сохранение результата новой версией. |
| Старый clipboard | [ProceduralClipboard.test.mjs](../Tests~/Legacy/ProceduralClipboard.test.mjs), [ProceduralClipboardSmoke](../Tests~/Legacy/ProceduralClipboardSmoke.cs), [PortableClipboardSmoke](../Tests~/Legacy/PortableClipboardSmoke.cs), [CompactPortableClipboardSmoke](../Tests~/Legacy/CompactPortableClipboardSmoke.cs), [PortableIncludesSmoke](../Tests~/Legacy/PortableIncludesSmoke.cs) | Удалить проверки retired input/export. Сохранить проверки FX/includes, трансформов, targets и изображений через `whimtex.document`. |
| Сравнение поколений recipes | [ClipboardExamplesSmoke](../Tests~/Legacy/ClipboardExamplesSmoke.cs), `LegacyClipboard fixtures` (удалены) | Зафиксировать эталонные современные `0.12.5` documents/renders. Затем убрать необходимость хранить/парсить старый JSON при каждом прогоне. |
| Старый Output | `OutputCompressionSmoke` (удалён), `OutputProcessingSmoke` (удалён), `OutputTypeSmoke` (удалён), `OutputSettingsSmoke` (удалён), `OutputSettingsLayoutSmoke` (удалён), `LinkedOutputSmoke` (удалён) | Отделить legacy `.asset` UI/storage от актуального export/import. Retired сценарии удалить, полезные проверки перенести на TIFF/TextureImporter. [OutputEncodingSmoke](../Tests~/Legacy/OutputEncodingSmoke.cs) сохранить. |
| Rename/types | [CanvasTerminologyMigrationSmoke](../Tests~/Legacy/CanvasTerminologyMigrationSmoke.cs), [CanvasTerminology.test.mjs](../Tests~/Legacy/CanvasTerminology.test.mjs), [DocumentVectorWideningSmoke](../Tests~/Legacy/DocumentVectorWideningSmoke.cs), [Branding.test.mjs](../Tests~/Legacy/Branding.test.mjs) | Удалить требования старых API-алиасов, markers и legacy layouts. Добавить реальные файловые payload `0.12.5`; проверки текущих имён и сериализованных данных сохранить. |
| Gradient/FX | [GradientRetiredFieldSmoke](../Tests~/Legacy/WhimTexGradientRetiredFieldSmoke.cs), [ShaderFXFormerlySerializedAsSmoke](../Tests~/Legacy/ShaderFXFormerlySerializedAsSmoke.cs) | Retired `transition` после перехода больше не поддерживать. HLSL rename-тесты оставить, если остаётся current authoring grammar; не удалять их автоматически. |
| Контракт команд/docs | [AgentCommandInventory](../Tests~/Legacy/AgentCommandInventory.test.mjs), [AgentDocumentation](../Tests~/Legacy/AgentDocumentation.test.mjs), [MakeSeamlessContract](../Tests~/Legacy/MakeSeamlessContract.test.mjs) | Обновить inventory команд и текст политики. Проверки Make Seamless перенести со старой clipboard schema на современный формат без потери проверки диапазонов/полей. |

Документационные кандидаты:

- [LEGACY_LAYERS](../Documentation~/AI/LEGACY_LAYERS.md), `layers.schema.json` (удалена), `build-clipboard-schema` (удалён): убрать active authoring contract старого формата. При необходимости оставить по старому URL короткое указание на переход через `0.12.5`.
- [AI_AUTHORING](../AI_AUTHORING.md), [AI contract](../Documentation~/AI/README.md), [JSON_FORMAT](../Documentation~/JSON_FORMAT.md), API guides, README и локализованные руководства: единая граница поддержки и явные upgrade instructions.
- [Documentation workflow](../.github/workflows/documentation.yml): синхронно обновить запуск validators, которые завязаны на старые clipboard schema и примеры. Удаление схемы без изменения workflow даст красный CI.
- [HANDOFF](HANDOFF.md), [DECISIONS](DECISIONS.md), [AGENTS](../AGENTS.md): обновить текущую политику, не переписывая исторические сведения как будто совместимости никогда не было.

## 8. Рекомендуемый порядок реализации

1. **Зафиксировать файловый baseline `0.12.5`.** Подготовить настоящие сохранённые документы, пресеты, HLSL и ожидаемые результаты рендера. Явно описать границу композиторов `.asset`; API-запросы и layouts в обязательный compatibility baseline не входят. Тег уже есть; обновлять его не нужно.
2. **Удалить API-мосты.** Алиасы и прежние агентские маршруты не сохранять. Обновить внутренние вызовы, команды, документацию и тесты под новый контракт.
3. **Сделать узкое чтение `0.12.5`.** Discard retired output-полей, сохранение fixed defaults и необходимая нормализация. Проверить, что неизвестные пользовательские данные по-прежнему защищены.
4. **Удалить старый `.asset` рабочий маршрут.** Writer, прежний API мигратора, UI, embedded output/sprites runtime, старый Live Update и связанные discovery/save ветви; общие компоненты разделить. Выполнено без reader `.asset`; адаптер снятых полей сохранён только для TIFF/JSON 0.12.5.
5. **Удалить старый clipboard и portable helpers.** Перевести полезные fixtures/проверки на современный документ.
6. **Убрать старые markers и coercion.** Для файлов — после доказанного canonical resave через `0.12.5` и отдельно проверенных Unity presets; для EditorPrefs/layouts переходный reader не требуется.
7. **Отдельно унифицировать неоднозначные данные.** Manual FX, sentinel-размеры, brush settings, пользовательские каталоги. Это более рискованный рефакторинг, а не простое удаление dead code.
8. **Обновить правила, схемы, docs и тесты вместе.** Проверить штатную компиляцию Unity, релевантные regression tests и документационный CI. Аудит не подтверждает эти результаты заранее.

### Минимальная матрица регрессионных образцов

| Область | Образцы `0.12.5` | Что проверить после очистки |
| --- | --- | --- |
| TIFF/JSON | TIFF с несколькими типами слоёв; full/compact JSON и layer fragment | Открытие, отсутствие ложного incomplete warning, render, save/reopen; сохранность IDs, targets, transforms. |
| Retired fields | TIFF с outputSettings/savedOutputSettings/spriteSlices и Drawing origin metadata | Контролируемый discard без потери актуального содержимого; новые неизвестные поля не замалчиваются. |
| Defaults | Compact JSON с пропущенными полями; Noise/Pattern/Shape sentinel | Тот же визуальный результат, что в `0.12.5`; стабильные значения после нового сохранения. |
| Drawing | SDR/HDR pixels, сохранённые параметры кисти слоя | Те же pixels и результат изображения; явный перенос сохраняемых настроек, корректные Undo и дальнейшее рисование. Совместимость старого API stroke не требуется. |
| FX | Linked/embedded FX, manual parameters, внешние `.asset` presets, HLSL aliases/includes | Значения, текстурные ссылки и изображение сохранены; штатная повторная компиляция; invalid FX не теряет исходник. |
| Unity importer | TIFF Default и Sprite, slices/pivot/border, материалы/File ссылки | Настройки `.meta`, asset references и актуальный Live Update не повреждены. |
| Пользовательская библиотека | Файлы пресетов `0.12.5`, в том числе в старой папке | Файлы читаются при явном открытии/импорте. Старые prefs, автоматический выбор пути и layouts не обязаны восстанавливаться. |
| Ошибки/расширения | Missing assets, неизвестный layer/type, сохранённые custom данные | Защита от молчаливой потери файловых данных остаётся. Старая C# интеграция может требовать обновления; отсутствие типа должно быть диагностировано. |

## Степень уверенности

Выводы о потребителях, ветвлениях и записываемых полях основаны на статическом чтении исходников `v0.12.5` и поиске вызовов. Это достаточная база для плана очистки, но не доказательство полной миграции всех пользовательских файлов. Перед удалением соответствующие группы должны пройти baseline/regression проверки в подключённом Unity Editor.
