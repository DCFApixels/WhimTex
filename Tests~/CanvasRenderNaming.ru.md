# Имена рендера Canvas

4 октября 2026 года. Переименование по назначению, без изменения рендера и без старых API-алиасов.

| Было | Стало |
| --- | --- |
| `Compose()` | `ComposeCanvas()` |
| `ComposePreview(int maxSize)` | `ComposeCanvas(int maxSize)` |
| `RenderPreview` | `RenderCanvas` |
| `RenderCachedPreview` | `RenderCanvasWithCache` |
| `GetPreviewDimensions` | `GetCanvasRenderSize` |
| `ComposeAtSize` | `ComposeCanvasAtSize` |
| `RenderComposite` | `RenderCanvasCore` |
| `RenderAllLayers` | `RenderCanvasAtSize` |
| `RenderThumbnailLayer` | `RenderLayerThumbnail` |
| локальные `previewWidth` / `previewHeight` композитора | `renderWidth` / `renderHeight` |

`ComposeCanvas()` остаётся публичным и возвращает читаемый HDR `Texture2D` полного размера.
Перегрузка с `maxSize` остаётся internal: ограничивает длинную сторону, сохраняет пропорции
с округлением до пикселя и не увеличивает изображение. `RenderCanvas` возвращает временный
`RenderTexture`, который вызывающий код должен освободить через `ReleaseTemporary`.

`RenderCanvasWithCache` использует кеш промежуточных результатов эффектов и публикует
результаты для Layer Preview; название не обещает постоянный кеш всего холста.
`RenderCanvasAtSize(width, height)` принимает точные размеры и сохраняет прежний
`scaleMultiplier = 1f`. Это не замена ограничению длинной стороны.

Обновлены вызовы в окне, API, экспорте, сохранении, миниатюрах и тестах, включая reflection.
Layer Preview, Brush Preview и PostFxPreview не переименованы. `_PreviewScale` остаётся
контрактом пользовательских HLSL из файлов 0.12.5. Имена `TextureCompositor`, окна и скрытых
шейдеров также сохранены. Форматы файлов, эталоны 0.12.5 и их контрольные суммы не менялись.

## Проверка

Проверено в подключённом Unity 6000.7.0a6, только штатным Editor/Pipeline.
Навык `unity-cli` использован для компиляции и проверок именно проекта Test6.6;
отдельные сборщики и player build не запускались.

| Проверка | Результат |
| --- | --- |
| Компиляция пакета | completed, failed=false, errors=[] |
| Основной набор | 231/231 выбранный entry point выполнен без ошибок |
| Новая Unity-проверка имён и поведения | 17 583 проверки: сигнатуры, размеры, HDR, CPU/GPU/cache parity, состояние рендера, точный размер и миниатюра |
| Node | 77/77 файлов, без ошибок и пропусков |
| Layer Preview reuse | 2 752 550 assertions; проверен конечный Result, не только запуск Start |
| SeamlessRelease: Preview / Workflow | 1 572 870 / 393 608 проверок |
| TIFF golden Gradient / Drawing | Оба эталона открываются и композятся; размеры и SHA-256 неизменны |
| NoiseSmallScaleExperiment | Только compile-only; эксперимент не запускался |
| Документация | Source: 84 страницы; обе generated schemas проходят |
| Production Jekyll / GitHub Pages | Не проверено: Ruby/Bundler недоступны |

Дополнительный `SeamlessReleaseSmoke.Workflow` сначала выявил устаревший шестой аргумент
reflection-вызова `PasteCopiedLayersAt`. Тест актуализирован под действующую сигнатуру
из пяти аргументов; повторный прогон проходит. Возврат совместимости в API не потребовался.

В `LayerScrollViewSmoke` проходят assertions арифметики clamp, но синхронные диагностические
высоты UI равны NaN до layout; это не доказательство измеренного или визуально проверенного layout.
Полный набор stress/manual вариантов не запускался. Entry points, аргументы, результаты
и исправленная неудачная попытка сохранены в [машинном отчёте](CanvasRenderNaming.results.json).
Исторические результаты в `CanvasTerminology.md` относятся к предыдущему этапу.

Изменения локальные: commit, push и изменение версии не выполнялись.
