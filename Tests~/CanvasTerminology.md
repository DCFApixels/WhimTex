# WhimTex: продолжение переименования и оставшиеся имена

Обновление 4 октября 2026: методы рендера композиции теперь используют Canvas.
Точные соответствия и текущие проверки — [в отдельном отчёте](CanvasRenderNaming.ru.md).
Прежние `ComposePreview` / `RenderPreview` и связанные имена ниже описывают историю,
а не оставшиеся ограничения совместимости.

Дата: 3 октября 2026 года. Пакет: `D:/DCFA/Projects/Test6.6/Packages/com.dcfapixels.whimtex`.

## Результат

Обновление при очистке легаси: файловая совместимость `0.12.5` сохраняется,
но API-совместимость больше не требуется. `UsePreviewChannels`, `PreviewTitle`,
`ImmediatePreviewUpdates` и `RequestPreview` удалены; использовать соответственно
`UseCanvasChannels`, `LayerPreviewTitle`, `ImmediateLayerPreviewUpdates` и `RequestLayerPreview`.
Строки таблицы ниже про эти алиасы описывают состояние до очистки. Маркеры
состояния окон удалены 4 октября 2026; прежние ключи EditorPrefs удалены 4 октября 2026 без миграции.
Инструменты используют `Canvas.*`, внешний вид панели — `CanvasView.*`.
`CanvasTerminologyMigrationSmoke.Run` теперь проверяет только актуальное состояние окна,
направляющие и roundtrip, а не миграцию старого layout. Исторические результаты ниже не являются текущим контрактом.

Продолжено переименование по назначению объектов, а не по наличию слова Preview.
Фон панели, общие помощники отображения каналов и актуальные тесты получили
уточнённые имена. В руководствах EN/RU/ZH обновлены названия панели, её верхней
и нижней строк, места перетаскивания и перехода фокуса.

Это рефакторинг имён. Алгоритмы рендера, размеры изображений, расположение
элементов, формат документов и правила взаимодействия не изменялись.
Существующие изменения предыдущего этапа сохранены.

## Термины

| Термин | Значение |
| --- | --- |
| Canvas / Холст | Редактируемая область изображения. |
| Canvas View / Панель холста | Панель, в которой отображается и перемещается холст; по текущему глоссарию включает настройки инструментов и нижнюю строку управления. |
| Layer Preview / Превью слоя | Результат отдельного слоя до смешивания с остальными, в Layer Settings или Properties. |

Слово Preview остаётся уместным для пробного результата, образца цвета,
мазка кисти, просмотра экспорта или игровой постобработки.

## Что переименовано на этом этапе

| Было | Стало | Где и почему |
| --- | --- | --- |
| `WhimTexPreviewBackdrop.png` | `WhimTexCanvasViewBackdrop.png` | `src/`: фон именно Canvas View. Обновлены генератор брендовых изображений и тест Branding. Само изображение не перегенерировалось. |
| `PreviewChannelDragManipulator` | `ChannelDragManipulator` | `src/Editor/`: общий помощник переключения каналов протягиванием мыши. Работает в Canvas View и Layer Preview, поэтому нейтральное имя точнее. |
| `WhimTexMaterials.PreviewChannels`, `previewChannelsMaterial` | `DisplayChannels`, `displayChannelsMaterial` | `src/Utils.cs`: общий материал отображения каналов, используемый также образцами кистей. |
| `PreviewChannels.shader` | `DisplayChannels.shader` | `src/Shaders/`: обновлены путь загрузки, имя `Hidden/TextureCompositor/DisplayChannels` и вызовы в коде/тестах. Подпись входной текстуры стала Input. |
| «основное превью», «шапка/футер превью» и аналогичные обозначения панели | Canvas View / панель холста и её верхняя/нижняя строка | Пользовательские руководства EN/RU/ZH. Образцы кистей, цветов и предварительные результаты не переименованы в Canvas. |
| Старые названия актуальных тестов панели | Canvas View / Canvas / Layer Preview / Display Channels | Сопоставление ниже. Строки фикстуры нижней строки и временный ключ результата теста Layer Preview также уточнены. |

Три Unity-ассета перемещены через `AssetDatabase.MoveAsset` вместе с метаданными.
GUID сохранены и проверены в подключённом Editor:

| Ассет | GUID |
| --- | --- |
| `src/WhimTexCanvasViewBackdrop.png` | `a693463e898e46f089f96a27a27c7451` |
| `src/Editor/ChannelDragManipulator.cs` | `7dc74ce0e40e423391e045e73c172f56` |
| `src/Shaders/DisplayChannels.shader` | `4dc8a962e0d34186818056fabc97e88a` |

Ссылки на ассеты по GUID сохраняются. Старые прямые пути к этим файлам и вызов
`Shader.Find("Hidden/TextureCompositor/PreviewChannels")` нужно заменить:
алиас старого имени скрытого служебного шейдера не добавлялся.
Публичный API WhimTex не использует это имя как точку входа.

### Актуальные имена тестов

Все файлы находятся в `Tests~/`.

| Старый файл | Текущий файл |
| --- | --- |
| `PreviewBackdrop.test.mjs` | `CanvasViewBackdrop.test.mjs` |
| `PreviewGuides.test.mjs` | `CanvasViewGuides.test.mjs` |
| `PreviewRotation.test.mjs` | `CanvasViewRotation.test.mjs` |
| `PreviewZoomSmoke.cs` | `CanvasViewZoomSmoke.cs` |
| `PreviewHeaderRefreshSmoke.cs` | `CanvasViewHeaderRefreshSmoke.cs` |
| `PreviewFooterSetup.cs` | `CanvasViewFooterSetup.cs` |
| `PreviewFooterHintResize.cs` | `CanvasViewFooterHintResize.cs` |
| `PreviewFooterSmoke.cs` | `CanvasViewFooterSmoke.cs` |
| `PreviewMarginsSmoke.cs` | `CanvasMarginsSmoke.cs` |
| `PreviewChannelsSmoke.cs` | `DisplayChannelsSmoke.cs` |
| `MiniPreviewReuseSmoke.cs` | `LayerPreviewReuseSmoke.cs` |

Для последнего теста класс и точки входа теперь
`LayerPreviewReuseSmoke.Start` / `LayerPreviewReuseSmoke.Result`.
Названия методов, на которые тесты ссылаются для проверки старой сериализации
и совместимых API, намеренно сохранены.

## Имена, относящиеся к старой терминологии, но оставленные

Это историческая инвентаризация до очистки. Unity-маркеры и C#/protected API-алиасы
из таблицы уже удалены. Прежние ключи EditorPrefs также сняты; остаются адреса документации. Настройки не входят
в файловую совместимость 0.12.5.

| Место | Что оставлено | Причина и условие возможного удаления |
| --- | --- | --- |
| `TextureCompositorWindow.{Channels,Guides,GuideCommands,Tiling,Inspector}.cs`; `Utils.cs` | Старые строки в `FormerlySerializedAs`: `previewChannels`, `previewDebug`, `previewGuidesHidden/Locked/Snap`, `previewGuides`, `tiledPreview`, `inspectorPreviewState` | Активные поля уже переименованы. Старые строки нужны для чтения сохранённого состояния; удалять только при явном отказе от его совместимости. |
| `TextureCompositorWindow.Guides.cs`, `.GuideCommands.cs` | `PreviewGuide`, `PreviewGuideSettingsWindow` внутри `MovedFrom` | Идентифицируют прежние вложенные типы при миграции. Сами типы уже CanvasGuide и CanvasGuideSettingsWindow. |
| `TextureCompositorWindow.cs`, `.Tools.cs` | Ключи `DCFApixels.WhimTex.PaintingPreviewScale`, `DCFApixels.WhimTex.PreviewTool`, `DCFApixels.WhimTex.PreviewTransformReturnTool` | Это адреса сохранённых EditorPrefs, не действующие имена полей или инструментов. Новые ключи потребуют отдельного переноса значений. |
| `WhimTexUserSettings.cs` | `DCFApixels.WhimTex.Preview.{CheckerLight,CheckerDark,InvalidPixels,CheckerSize,ShowManta,PostFxBackground,PostFxBackgroundMode}` | Сохраняют пользовательские настройки без дублирования ключей и выбора приоритетов. |
| `Editor/WhimTexColorField.cs` | `UsePreviewChannels` | Устаревший публичный алиас к `UseCanvasChannels`. Его удаление сломает внешние обращения. |
| `Utils.cs`, `LayerEditorWindowBase` | `PreviewTitle`, `ImmediatePreviewUpdates`, `RequestPreview` | Устаревшие защищённые точки расширения. Новые имена уже доступны; виртуальные мосты сохраняют старые override внешних окон Properties. |
| `Documentation~/{en,ru,zh}/preview.md` и ссылки на них | Имена файлов, URL `/preview/`, якоря вроде `preview-and-navigation`, `layer-mini-preview`, `минипревью-слоя` | Видимые названия обновлены. Старые адреса оставлены ради входящих ссылок и по правилам пакета. Для смены URL нужны перенаправления и сохранение старых якорей. |
| `CHANGELOG.md`, исторические отчёты в `Tests~/` | Прежние имена тестов и результаты прошлых запусков | Это исторические записи. Для запуска текущих файлов использовать таблицу выше, а не старые команды из отчётов. |

## Имена Preview, которые не требуется заменять по глоссарию

| Место | Имена / группа | Почему это не Canvas View |
| --- | --- | --- |
| `TextureCompositor.cs`, `.EffectCache.cs`; `Automation/WhimTexApi.*.cs`; `WhimTexDocumentBuild.cs`; `Editor/WhimTexDocumentSession.cs` | `ComposePreview`, `RenderPreview`, `RenderCachedPreview`, `GetPreviewDimensions`; поля/операции API `preview` | Получение изображения с ограничением разрешения, в том числе без окна. Подробнее ниже. |
| `ShaderFX.cs`, `ShaderFXSourceBuilder.cs`, FX и рендереры | `_PreviewScale`, `previewScale`, связанные обозначения масштаба расчёта | Масштаб выборки уменьшенного результата; не UI-масштаб и не название панели. HLSL-имя — существующий контракт пользовательских FX. |
| `PostFxPreview.cs`, `PostFx/URP/UrpPostFxPreview.cs`, `URPPreviewSurface.shader`, интеграция Post FX окна | `PostFxPreviewSettings`, `PostFxPreviewRequest`, `PostFxPreviewBackend`, `UrpPostFxPreview`, Post FX Preview | Предварительный просмотр игровой постобработки и контракт подключаемых реализаций. |
| `Layers/Layer.cs`, реализации слоёв, `TextureCompositor.Thumbnails.cs`, `EffectTargetSettingsView.cs` | `GetPreviewTexture`, локальные `preview` | Исходные изображения/миниатюры и образцы входа. Это не обязательно результат Layer Preview. |
| `Editor/WhimTexOutputSettingsWindow.cs`, `WhimTexOutputPreview.cs`, `Shaders/OutputPreview.shader`, USS | Output Preview, `.whimtex-output-preview-*`, resizer | Отдельный просмотр выходной текстуры и mipmap. Некоторые общие стили переиспользует Layer Preview. |
| `TextureCompositorWindow.BrushPreview.cs`, `Layers/DrawingLayerBehaviour.BrushPreview.cs`, `Editor/BrushPresetAssetEditor.cs` | `BrushStrokePreview`, `RenderBrushPreview`, `inspectorPreview`, Preview Scale (%) | Пробный мазок и образец пресета кисти. |
| `Editor/WhimTexColorPicker*`, `WhimTexGradient*`, `ShaderFXEditor.cs` | Preview EV, образцы цвета/градиента, увеличение пипетки, локальные поля preview | Просмотр конкретного значения или экранного пикселя. Источник каналов Canvas уже назван отдельно. |
| `TextureCompositorWindow.ContentAwareFill.cs` | Preview, `MakePreview`, `ReleasePreview` внутри окна заливки | Предварительный результат перед применением операции. Кнопка Preview имеет самостоятельный смысл. |
| `Editor/TextureCompositorProjectPreview.cs`, `TextureCompositorEditor.cs`, legacy writer, Sprite Editor integration | `RenderStaticPreview`, Project/import/sprite previews | Просмотр ассета и контракты Unity; имена override Unity не переименовываются произвольно. |
| `TextureCompositor.LayerPreview.cs`, `Editor/LayerPreviewPanel.cs`, окна Properties | Layer Preview и связанные методы/поля | Это уже согласованный термин, а не остаток старого имени Canvas View. |

`TextureCompositorWindow` также оставлен. Это класс всего окна WhimTex:
он владеет документом, списком слоёв, инспектором и инструментами, а не только
панелью холста. Переименовывать его в CanvasViewWindow было бы неточно.
Семейство скрытых шейдеров `Hidden/TextureCompositor/*` и тип модели
`TextureCompositor` не относятся к переименованию Preview → Canvas View.
Маркеры прежнего namespace удалены отдельным этапом очистки легаси.

Команды `whimtex_*`, идентификатор skill `whimtex-live`, JSON и путь
`Temp/WhimTex/` не изменены.

## Три спорных пункта простыми словами

### ComposePreview и связанные методы

Например, документ 4096 × 2048 при `ComposePreview(1024)` даёт изображение
1024 × 512. Открытая панель Canvas View для этого не нужна.

- `GetPreviewDimensions` вычисляет размеры и масштаб расчёта, сохраняя пропорции.
- `ComposePreview` возвращает Texture2D.
- `RenderPreview` возвращает RenderTexture.
- `RenderCachedPreview` использует тот же путь с кешем эффектов и публикацией результатов для Layer Preview.

Если документ уже меньше лимита, увеличение не производится. Эти методы
используются также API, сравнением изображений, экспортом и сессией документа.

Имена вроде ComposeScaled / RenderScaledComposite возможны как отдельное
уточнение API рендера, но глоссарий этого не требует. Они не стали Canvas View
методами только потому, что панель использует их результат. Оставлены.

### _PreviewScale и previewScale

Это не Zoom % в интерфейсе. Для примера выше один пиксель уменьшенного
результата соответствует четырём пикселям исходного документа, и
`_PreviewScale` равен 4. При полном разрешении он равен 1.

FX использует это соотношение, чтобы учитывать исходный размер при выборке.
Переименование uniform в `_RenderScale` без сохранения старого объявления
и передачи значения может сломать пользовательский HLSL. Добавление нового
алиаса потребовало бы обновить генератор, зарезервированные имена, передачу
значений, документацию и проверки обоих вариантов.

Это отдельная миграция контракта, а не исправление названия панели.
Текущие имена и семантика сохранены.

### PostFxPreview*

Это «посмотреть текстуру с игровой постобработкой», а не другое имя холста.
Эффекты этого просмотра не записываются в сохранённую текстуру.

Настройки, запрос и backend позволяют подключать реализацию такого
просмотра, например для URP. Слово Preview здесь описывает назначение функции
и остаётся корректным. Публичный контракт расширений не изменялся.

## Проверки этого этапа

| Проверка | Результат |
| --- | --- |
| Компиляция в подключённом Unity Editor Test6.6 | Завершена, `failed: false`, ошибок компиляции нет. |
| GUID и импорт трёх переименованных ассетов | Сохранены; фон загружается; общий DisplayChannels material создаётся, шейдер поддерживается. |
| `CanvasTerminologyMigrationSmoke.Run` | 27 проверок: старые поля, направляющие, состояние Layer Preview, roundtrip новых имён и публичный алиас. |
| `DisplayChannelsSmoke.cs` | 96 GPU-проверок: все 16 комбинаций RGBA. |
| `CanvasViewZoomSmoke.cs` | 98 проверок масштаба, перемещения, поворота и кадрирования. |
| `CanvasViewHeaderRefreshSmoke.cs` | 6 проверок обновления поля масштаба без лишнего обновления настроек. |
| `CanvasMarginsSmoke.cs` | 6 проверок рисования у края и за пределами холста. |
| `LayerPreviewPanelSmoke` | 3 145 986 проверок: протягивание по каналам обеих панелей, пиксели, размеры, привязка слоя и освобождение ресурсов. Повторён после импорта нового шейдера. |
| `LayerPreviewReuseSmoke` | 2 752 550 проверок: кеш, семплирование, владение GPU-ресурсами, группы, обтравка и независимость от экспорта/миниатюр. |
| Node: CanvasTerminology, Branding, CanvasViewBackdrop, CanvasViewGuides, CanvasViewRotation | Все 5 файлов прошли; проверки направляющих — 1584, геометрии поворота — 3480. |
| `check-docs.mjs source` | 84 страницы: ссылки и взаимная навигация EN/RU/ZH проверены. |
| `git diff --check` | Пройден. |

Первый GPU-тест обнаружил старое импортированное имя шейдера в Editor.
Принудительный импорт DisplayChannels.shader штатным AssetDatabase устранил
причину; последующие GPU- и UI-тесты прошли. Записи «shader not found»,
возникшие до повторного импорта, не удалялись из пользовательской консоли.

Проверки использовали временные документы и окна. Пользовательские TIFF,
сцены, префабы и материалы не редактировались. Сборка игры не запускалась.
Версия пакета не повышалась.

### Что не проверялось

- Открытие сохранённого до рефакторинга бинарного layout после полного перезапуска Unity. Миграционные маркеры сохранены, но этот сценарий отдельно не запускался.
- Полная сборка Jekyll и визуальный просмотр сайта; проверены исходные ссылки и навигация.
- Визуальный обзор интерфейса в светлой теме и при разных DPI. Скриншоты существующего smoke-теста не используются как доказательство визуальной проверки.
- Переименованные отдельные фикстуры нижней строки CanvasViewFooter* и полный набор остальных тестов пакета. Проверка перетаскивания каналов нижней строки выполнена в LayerPreviewPanelSmoke.

Отчёт описывает фактический текущий этап. Результаты предыдущих запусков
не выдаются за новые проверки.
