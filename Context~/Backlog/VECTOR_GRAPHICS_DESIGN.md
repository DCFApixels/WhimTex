# Векторная графика WhimTex

- Назначение: подготовить F08 — собственная модель редактируемых кривых, фигуры, обводки, процедурные экземпляры, растеризация/SDF и установка на Unity 6.0+.
- Статус: исходный дизайн от 2026-10-12; 2026-10-16 пользователь поручил реализовать первый Vector-прототип. Его фактический объём, ограничения и проверки собраны в [F08](SVG_LAYER.md). Остальные предложения ниже не считаются утверждёнными или реализованными; полный SVG не входит в этот этап.
- Источники истины: запрос пользователя; [граф F18](NODE_GRAPH_DESIGN.md), [карточка F08](SVG_LAYER.md), текущие [Shape](../../src/Layers/ShapeLayerBehaviour.cs), [ShapeContour](../../src/Layers/ShapeContour.cs), [SDF](../../src/Layers/SDFLayerBehaviour.cs), [Outline](../../src/Layers/OutlineLayerBehaviour.cs), [Transform](../../src/TextureTransform.Projective.cs), [градиенты](../../src/WhimTexGradient.cs); официальные API Unity по ссылкам ниже.

## Граница функции

Нужна векторная модель документа и инструменты её редактирования. SVG — только
один из будущих форматов обмена. Импорт SVG не является условием создания кривой
или процедурного набора фигур внутри WhimTex.

Собственный VectorPath layer/node хранит данные кривых и styles. Graph может
передавать их через Transform, Repeat и Scene до Rasterize. Прикреплённые Raster
FX добавляются после явно выбранной границы растеризации, не уничтожая исходные
кривые. Canvas показывает результат и handles редактирования этих же данных.

Существующий Shape остаётся удобным процедурным генератором; его параметры
и художественное поведение не заменяются произвольными кривыми автоматически.
Будущее `Convert to Path` создаёт редактируемый результат по явной команде.
Boolean paths, variable-width strokes и SVG interoperability — отдельные этапы,
не обязательный объём первого рабочего VectorPath.

## Какие средства Unity использовать

| Возможность | Unity 6.0–6.2 | Unity 6.3+ | Роль в WhimTex |
| --- | --- | --- | --- |
| Painter2D | Встроенное рисование UI-контуров. | Встроенное; расширенный набор API. | Handles, guides, wires и UI-значки, не сериализованная модель документа. |
| Программный Vector Graphics | Отдельный `com.unity.vectorgraphics` 2.x preview. | Базовый модуль встроен в Editor. | Возможный backend tessellation/rendering. |
| Дополнительный пакет 3.x | Не целевой вариант для этих Editor. | Дополнительные Sprite/uGUI варианты SVG-импорта. | Не обязателен для собственного векторного документа. |

Разделение старого пакета и встроенного модуля описано в официальном
[руководстве Vector Graphics](https://docs.unity3d.com/Packages/com.unity.vectorgraphics@3.0/manual/index.html).
Прежний 2.x обозначен экспериментальным в
[его руководстве](https://docs.unity3d.com/Packages/com.unity.vectorgraphics@2.0/manual/index.html).
Это риск зависимости для ранних Editor, а не доказательство непригодности
встроенного модуля 6.3+.

Для UI базовые кривые и заливки доступны в
[Painter2D 6.0](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.Painter2D.html).
В [Painter2D 6.3](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/UIElements.Painter2D.html)
появляются дополнительные fill/clip/dash API; например
[SetDashPattern](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/UIElements.Painter2D.SetDashPattern.html).
Нельзя использовать их без adapter на минимальной версии 6.0.
Однако настраиваемый пунктир старого Vector Graphics уже описан в
[Stroke.Pattern и PatternOffset](https://docs.unity3d.com/Packages/com.unity.vectorgraphics@2.0/api/Unity.VectorGraphics.Stroke.html).

Рекомендуется собственная model и интерфейс `IVectorBackend`: backend получает
нормализованные данные WhimTex, выдаёт geometry/render data, но не определяет
схему TIFF, IDs, инструменты и семантику styles. Реализации — Package2 для ранних
Editor и BuiltIn для 6.3+. Собственные переносимые алгоритмы закрывают то,
чего выбранный backend не умеет, вместо запрета функции на одной версии.

## Данные кривых и сцены

| Запись | Состав и инвариант |
| --- | --- |
| VectorResource | Stable resource ID, revision, paths и styles; документ владеет редактируемыми данными. |
| VectorPath | ID, упорядоченные контуры, fill rule, список fills/strokes, logical bounds. |
| Contour | ID, ordered point IDs, closed; пустой/одноточечный contour допускается как edit state, но не как площадь заливки. |
| PathPoint | ID, position, in/out tangent offsets, tangent mode: Corner, Aligned, Mirrored. |
| Segment | Производная пара соседних points; cubic Bézier. Прямая — частный случай, не иной storage-путь. |
| VectorInstance | Ссылка на immutable geometry, transform, style overrides, stable instance path. |
| Scene | Ordered items/instances, группы и clip references; порядок является частью результата. |

Хранить координаты authoring в document units с double precision, если это
совместимо с действующими Transform; при GPU upload переводить в float относительно
локального origin. Tangents — offsets от точки, поэтому её перенос не требует
отдельно двигать handles. Backend-specific SceneNode, Mesh, VectorImage и
tessellated triangles не сериализуются вместо точек.

Из кубических сегментов собираются и открытые линии, и закрытые фигуры.
Замыкающий сегмент не дублирует первую точку. Compound paths поддерживают holes
и явный NonZero/EvenOdd rule; направление контура сохраняется. Заливка открытого
контура может использовать виртуальное замыкание по заданной политике, но не
меняет `closed` в данных; stroke остаётся открытым. Первая версия может разрешать
fill только closed contours, показав это ограничение явно.

Ручное редактирование ресурса и procedural expression различаются. Generated
geometry может экспортировать readonly handles/параметры генератора; для правки
его точек нужна явная `Make Editable` с потерей procedural связи. Repeat
использует instance matrices и style overrides, не клонирует весь path для
каждого одинакового круга. Изменение shared source меняет все instance; Make
Unique создаёт отдельный ресурс одной Undo-командой.

## Инструменты Canvas

Предлагаемый первый набор: Pen, Path Select/Direct Select и обычный Transform.
Pen click создаёт Corner point; click-drag создаёт tangents. Клик по первой
точке замыкает contour; Enter завершает открытый, Esc отменяет незавершённый
жест, не удаляя ранее завершённые contours. Insert Point разбивает cubic через
de Casteljau и сохраняет прежнюю форму с численной погрешностью.

Direct Select поддерживает отдельные точки/handles, marquee по точкам, move,
delete и смену tangent mode. Для Corner handles независимы; Aligned сохраняет
коллинеарность, но не длину; Mirrored — коллинеарность и равные длины. Auto/Smooth
можно добавить позднее с явным правилом обновления соседей, не смешивая с Aligned.

Все жесты вызывают общий mutation service F18. Pointer capture, потеря фокуса,
смена инструмента и отмена заканчивают транзакцию предсказуемо. В процессе
drag используют candidate preview; один завершённый жест — один Undo.
Snapping учитывает document edges, guides и выбранные points; Ctrl временно
отключает snap. Guides имеют самый низкий hit/draw priority. Координаты hit-test
приводятся в View для постоянного размера handle, но положение записывается
в document/local coordinates, не в пикселях окна.

При выборе процедурного instance UI предлагает Edit Source или параметры
placement. Нельзя изменять «точку копии», если она принадлежит shared resource,
без понятного выбора. Instance path содержит Repeat ancestry; он нужен для
pick/render cache, а не заменяет ID authoring resource.

## Заливки и обводки

Fill первой версии: None/Solid/Gradient; Texture fill — следующий небольшой
этап. Цвета и градиенты используют существующую семантику WhimTex, включая
interpolation и encoding. Не приравнивать engine GradientFill к нашим режимам
по названию: при необходимости backend получает подготовленный gradient LUT.
Fill transform отдельный от object transform; Local/Document anchoring задаётся
явно, иначе копирование/вращение неожиданно перемещает цветовой рисунок.

| Параметр stroke | Рекомендуемый контракт |
| --- | --- |
| Enabled / Color / Opacity | Независимая запись style. |
| Width | Неотрицательная ширина, units Document или Local. |
| Alignment | Center; Inside/Outside только для closed shape с определённым interior. |
| Cap | Butt, Round, Square для открытой линии и каждой dash части. |
| Join | Miter, Round, Bevel; Miter Limit ограничивает длинные пики. |
| Dash Pattern | Положительные длины штрих/пробел, массив; единицы те же, что у dash settings. |
| Dash Offset | Сдвиг вдоль длины path; нормализуется по длине цикла без изменения saved base. |
| Placement | Continuous over contour, с явной политикой seam закрытого contour. |
| Stroke Layers | Ordered array styles; ID каждого style стабилен. |

Width Document означает одинаковую видимую толщину после nonuniform scale:
centerline сначала переводится в Document, затем строится stroke. Width Local
строится в Local и трансформируется вместе с фигурой. Dash units задаются явно:
absolute document/local length либо multiplier of stroke width; режим должен
быть один в данных и UI. Stroke не использует zoom окна как размер.

Пунктир раскладывается по **arc length**, не по параметру `t` Bézier. Для каждой
кривой кешируется таблица длины с допуском, dash endpoints уточняются по ней;
части режутся по полученным `t`, затем строятся cap/join. При изменении Width
перестраивается только то, что зависит от него. Нечётный pattern повторяется
дважды для чередования on/off; пустой pattern означает solid. Нулевые, отрицательные
и nonfinite entries запрещены, чтобы не получить бесконечный цикл. Для очень
мелкого pattern действует явный geometry budget, а не зависание Editor.

На закрытом contour seam: default Preserve Offset — не меняет длины pattern;
optional Fit Pattern — подбирает масштаб цикла/количество, показывая иной
результат. Round/Square cap может визуально удлинять dash; геометрические
длины остаются расстоянием по centerline. Miter Limit выражается отношением
miter length к half-width, не в абсолютных пикселях.

Двойная/составная обводка — несколько stroke styles или специальное построение
двух полос. Прозрачный промежуток должен оставаться прозрачным: нельзя рисовать
«широкая цветная линия + узкая линия цветом фона», потому что фон может быть
изображением/другой веткой. Для настоящего gap нужны отдельные полосы покрытия
или маска геометрии. На open path Inside/Outside не имеет универсального смысла;
в UI скрыть эти значения, а не выбирать произвольную сторону.

Self-intersection, узкие отверстия и offsets могут менять topology. Center stroke
и fill rule — базовый обязательный путь. Inside/Outside и double gap для сложной
фигуры требуют проверенного offset/boolean algorithm; до него такие случаи
явно ограничены и не выдаются за полностью поддержанные. Не вводить native
библиотеку без отдельного разрешения; переносимый C# backend допустим после
проверки лицензии и поведения.

## Геометрические операции и граф

Первый набор узлов: VectorPath, Circle/Rectangle как vector generators,
Transform, Set Fill/Stroke, Repeat, Assemble Scene, Rasterize. У Shape→Path
нужен renderer-independent geometric exporter: нельзя восстанавливать points
из пикселей Shader по умолчанию.

Перемещение, поворот, reflection и scale обновляют matrix instance. Merge
геометрии означает собрать объекты в Scene, а не автоматически boolean union.
Union/Intersection/Subtract/Xor — отдельные операции с правилами winding,
tolerance и propagation styles. Boolean result может утратить parametric identity
исходных углов; undo возвращает inputs, а не пытается восстановить их из результата.

Repeat из F18 принимает подграф и count. Для одинаковой формы со случайным
положением разделяется geometry, а не её appearance/placement. Для `radius(index)`
возникают разные geometry variants. Пример «один круг → разные размеры и места»
полностью реализуем в C#/Unity без shader graph; CPU вычисляет данные/матрицы,
GPU растеризует конечную сцену. Миллион вариантов не обещается бесплатно:
count/segments/geometry memory имеют budgets и cancellation.

## Растеризация и качество

Путь обычной отрисовки: geometry → adaptive flatten/tessellation → mesh/coverage
→ working RenderTexture → ImageFrame. Canvas, thumbnail, export и Output Bake
задают общий RenderRequest F18. UI Painter2D не используется как скрытый экспортный
renderer всего документа: его сглаживание и ограничения UI не определяют pixels
готовой карты.

Публичные `VectorUtils` включают tessellation, построение mesh/sprite и получение
Texture2D. Atlas для fills следует кешировать, а не строить каждый drag;
это отдельно отмечено в [руководстве программной векторной графики Unity](https://docs.unity3d.com/6000.3/Documentation/Manual/ui-systems/work-with-vector-graphics.html).
Для нашей линейной/HDR RT и прямого рендера без Texture2D readback нужен backend
adapter и проверка материалов, alpha и кодирования. Наличие helper raster API
не является доказательством совпадения с нашим compositor.

Допуск flatten/tessellation выражается в document pixels **заданного Output**.
Начальные кандидаты: Interactive 0.5 px, Final 0.25 px; это параметры качества
для измерения, не окончательные production constants. На zoom-in можно показывать
более точную preview geometry, но Final Bake не зависит от случайного zoom.
Adaptive subdivision использует flatness bound в преобразованном пространстве,
max depth и лимит сегментов. Singular transform, NaN и бесконечные coordinates
получают диагноз, а не бесконечное дробление.

Bounds учитывают extrema кубических кривых, transform, stroke/caps/joins,
Miter Limit, feather и clip. Bounding box только по control polygon допустим
как консервативный early reject, не как обрезающий точный output region.
При projective transform обрабатывать пересечение/приближение к горизонту;
отклонять или явно ограничивать неограниченные bounds.

Сглаживание — свой проверяемый coverage путь. Рекомендуемый fallback:
tile-local supersampling с downsample для Final, более дешёвый preview;
позднее analytic edge coverage. Не предполагать, что AA VectorImage в UI
автоматически работает на экспортной RT, особенно с HDR и несколькими blends.
Pixel Art режим Step бинаризует **итоговое** coverage после всех resample:
иначе bilinear/downsample снова создаст дробную alpha. Фильтр Output и следующие
FX могут вновь изменить alpha — это видно как отдельный этап.

## SDF и растушёвка

Уточнение пользователя 2026-10-13 для **будущего** переноса SDF в FX:
векторный слой должен предоставлять поле из исходной геометрии, без
промежуточного растра. Требование и открытый вопрос стадий после FX/масок
сохранены в [F19](SDF_OUTLINE_DESIGN.md#будущий-sdf-из-векторного-источника).
Описанный ниже mask-based MVP не заменяет это требование к будущему FX-пути;
реализация векторного producer сейчас не поручена.

Distance field — данные расстояния в объявленных units, не готовая grayscale
картинка со случайным remap. Предлагается `DistanceField` как typed semantic
resource поверх float ImageFrame: negative inside, positive outside, zero на
границе; metadata содержит distance units, sampling region и max represented
distance. При encoding в обычную текстуру scale/bias задаются явно.

Растушёвка использует `alpha = 1 - smoothstep(inner, outer, d)`; inner/outer
расположены в тех же units. Shift меняет уровень границы, а не geometry resource.
Fill SDF и Stroke SDF различаются: stroke учитывает caps/joins/dash; нельзя
получить точную сложную обводку одним `abs(fillDistance) - width/2`.

| Метод | Что предлагается | Ограничение |
| --- | --- | --- |
| Raster mask → distance transform | Быстрый базовый режим, используя существующий SDF путь после проверки его контракта. | Точность ограничена сеткой маски; это не бесконечно точная кривая. |
| Curve/flattened boundary → distance | Более точный vector режим: adaptive polyline + nearest segment и inside rule. | Ошибка зависит от допуска аппроксимации, topology и units. |
| Exact distance to Bézier | Поздняя специализированная опция, если измерения покажут пользу. | Корни distance derivative, degenerate curves и sign требуют отдельного надёжного solver. |

Для более точного режима строить BVH/пространственные bins сегментов и
ограничивать расчёт tiles расширенными bounds. Interior sign вычисляется
по выбранному fill rule; holes/self-intersections не определяются только
ближайшим сегментом. Отрицательная determinant matrix не теряет ориентацию
NonZero. Near-boundary случаи используют согласованный epsilon, чтобы tessellation
и SDF не давали разные holes.

Nonuniform scale не позволяет преобразовать scalar distance одной средней
длиной scale. Для Document-space field расстояние вычисляется до уже
трансформированной границы; Local-space field явно растягивается вместе с ней.
Простое умножение локального SDF на `max(scale)` не является точным решением.

Текущий SDF уже вычисляет signed distance в CPU Jobs/Burst после чтения Color32,
загружает float field в RFloat и затем делает gradient/encoding проход shader.
В exact-finalize отрицательный знак соответствует внутренней области. Это
кандидат для переиспользования mask-based solver, но не готовый vector-distance
renderer: readback, порог/8-bit source, edge expansion и последующий encoding
влияют на результат и стоимость. Raw DistanceField нужно выдавать до цветового
remap/normalization, сохраняя units.

В MVP можно оставить mask-based SDF с повышаемой resolution/density и явным
качеством. При переносе solver измерить readback, precision, стоимость и sign
во всех метриках, а не переиспользовать только SdfGradient shader.
Vector-aware field — следующий этап. Ни один режим не обязан пересчитывать
весь документ при перемещении одного handle: bounds, revisions и dependencies
должны ограничивать область/ветку. Реальный GPU tile pipeline добавляется
после профилирования, не обещается готовым вместе с первым Path.

## Кеш и ограничения производительности

Разделить geometry, transform, stroke expansion, fill atlas/LUT, SDF и output
cache. Изменение цвета не перестраивает path subdivision; перемещение instance
не копирует authoring points; изменение Dash Offset может менять stroke geometry,
но не cubic resource. Замена Output resolution меняет tolerance/coverage вариант,
не source points.

Shared geometry кешируется по resource revision и tolerance variant. Render
instance path, transform и style участвуют только в зависимых стадиях.
Очень разные nonuniform/projective matrices могут требовать другой subdivision;
нельзя всегда разделить один грубый mesh на любые масштабы. Повторимые одинаковые
style/geometry группируются для batching, но reorder/blending сохраняет порядок.

Лимиты: points, generated segments, Repeat count, tessellation time, per-request
memory, distance-field pixels и draw calls. Числовые пределы выбрать по benchmark
среза, до публикации API. При превышении — причина/размер/вариант уменьшить
качество, а не молчаливое изменение формы. Incremental preview может использовать
последний успешный результат с stale badge; Final не выдаёт его за новый успех.

## Установка в один клик

Цель: пользователь устанавливает WhimTex один раз. На Unity 6.0–6.2 необходимая
зависимость подключается автоматически; на 6.3+ программный backend использует
встроенный модуль. Пользователь не ищет dependency вручную.

Обычный `package.json.dependencies` — фиксированная карта зависимостей,
не условие по версии Editor; `unity` задаёт минимальную версию. Документированный
формат не содержит conditional dependencies:
[UPM package manifest](https://docs.unity3d.com/6000.0/Documentation/Manual/upm-manifestPkg.html).
[asmdef versionDefines/defineConstraints](https://docs.unity3d.com/6000.0/Documentation/Manual/assembly-definition-file-format.html)
выбирают компиляцию, а не устанавливают пакет.

### Рекомендуемый установщик

Малый Editor bootstrap компилируется без ссылки на Vector Graphics и работает
через поддерживаемый PackageManager API. Core модели тоже не имеет такой ссылки.
Платформенные adapters изолированы; отсутствие старого пакета не должно лишать
Editor возможности запустить bootstrap.

```text
WhimTex installed
  → determine supported Editor version / installed packages
  → 6.0–6.2: compatible Package2 present?
      yes → validate adapter → Ready
      no  → async Client.Add(exact tested version) → resolve/reload → validate → Ready
  → 6.3+: validate built-in adapter → Ready
  → failure: vector unavailable + shared diagnostic + Retry
```

`Client.Add` асинхронен. Preview требует точного `name@version`; вызов по одному
имени выбирает compatible released version, не гарантирует нужную preview.
Это указано в [Client.Add](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/PackageManager.Client.Add.html).
`2.0.0-preview.25` — кандидат для матрицы проверки по опубликованной документации,
не автоматически утверждённая зависимость нашего релиза.

Запросы UPM идут последовательно, completion проверяется на Editor updates,
без busy wait. Installation intent/status переживает domain reload. Если пакет
уже подходящий — ничего не менять; если несовместимый — показать конфликт,
не молча откатывать чужую зависимость. На новом Editor не удалять старый/новый
пакет автоматически: он может принадлежать другому инструменту проекта.
Обычная загрузка документа не запускает установку неизвестного пакета из данных.

Однократная initial setup может автоматически добавить только объявленную
зависимость поставляемого WhimTex; отказ/offline не вызывает бесконечные retries.
UI показывает vector setup status и Retry, остальная растровая часть остаётся
доступной. Network failure означает невозможность завершить установку сейчас,
не потерю geometry payload. Для offline работы нужный пакет должен быть уже
доступен в cache/проекте; «один клик» не обещает скачивание без сети.

Критический технический срез до одобрения способа распространения: проверить
изолированную Package2 asmdef с отсутствующей reference assembly. Одни только
defineConstraints нельзя объявить решением, пока не подтверждено, что Unity
не выдаёт compilation error до bootstrap. Если assembly isolation/version
defines не позволяют безопасный cold install, выбрать поставляемый переносимый
backend либо раздельные совместимые выпуски/installer distribution. Не обходить
это нештатной компиляцией или отражением внутренних API Unity.

Альтернативы:

| Вариант | Установка | Цена и риск |
| --- | --- | --- |
| Один package + bootstrap + adapters | Предпочтительный UX после успешной проверки cold install. | Initial setup меняет зависимости проекта; нужны reload/error/conflict правила. |
| Отдельные Editor-compatible выпуски | UPM тянет фиксированные dependencies каждого выпуска. | Git URL/tag не выбирает релиз по Editor сам; совместимый выпуск всё равно надо разрешить installer/registry. |
| Собственный переносимый backend | Нет этой зависимости. | Больше своей геометрии/рендера; нельзя обещать качество без реализации и измерений. |

Для разработчика/CI дать явную setup-команду того же service, работающую в
поддерживаемом Editor; не обязательный второй ручной этап обычного пользователя.
Новые native binaries и package installs сейчас не разрешены этим дизайном.

## SVG как отдельный формат обмена

Сначала собственные paths/styles, затем импорт/экспорт поддерживаемого SVG subset.
Parser переводит файлы в VectorResource; текущие engine importer settings не
становятся schema документа. Нужна явная таблица supported elements/styles,
units/viewBox/transforms и обработка непонятных элементов.

Фильтры, external references, embedded script, animation и text layout не
обещаются первой версией. При неподдерживаемом элементе показывать warning и
предложение raster fallback с подтверждением, а не молча удалять содержимое.
Запретить исполнение script и несанкционированные сетевые запросы parser.
Экспорт SVG не может losslessly представить произвольный растровый FX; выбор
embedded raster или unsupported остаётся явным. Текст не конвертируется в paths
по одному названию системного шрифта без отдельного outline extractor.

## Этапы и проверка

| Этап | Объём | Приёмка |
| --- | --- | --- |
| V0 | Backend/setup spike: чистые 6.0, 6.2, 6.3 и текущий Editor, cubic fill/stroke, RT output. | Cold install не блокирует compilation; правильный backend без ручного dependency поиска; raster alpha/encoding совпадает. |
| V1 | VectorPath, Pen/Direct Select, fill, centered constant-width stroke, tangents, Undo/save. | Closed/open curves, holes и duplicate/reparent редактируются из Canvas и roundtrip. |
| V2 | Repeat/Scene/Rasterize F18, dash/caps/joins, несколько stroke styles, gradients. | Разные procedural instance без преждевременного растра; контролируемые length/transform/quality. |
| V3 | Mask SDF/feather/Step, затем vector-aware distance и сложные offsets. | Units, sign, bounds и alpha проверены; нет ложного обещания exact distance. |
| V4 | Boolean paths, variable width, SVG import/export и расширенные styles. | Отдельные contracts, budgets и supported subset перед добавлением в пользовательский API. |

Целевые проверки реализации:

- Line/cubic/closed contour, holes NonZero/EvenOdd, reversed contour, repeated/совпадающие points, почти нулевая длина, tangents противоположного направления и self-intersection.
- Split segment сохраняет форму; move point сохраняет tangent offsets; delete/close/undo не теряют stable IDs и resource ownership.
- Dash length по arc length, negative Offset, нечётный pattern, seam, cap extension, Miter Limit, открытый Inside/Outside и geometry budgets.
- Nonuniform/negative/projective transform: bounds, Width Local/Document и SDF units; near-singular/horizon не зависают.
- Gradient interpolation, texture encoding, shared resource editing/Make Unique, Layer Transform/FX order и direct/instance selection.
- Feather большой/нулевой, holes и перекрытия strokes, Step после downsample, output scale/zoom independence; viewport, thumbnails и Bake совпадают при том же RenderRequest.
- Old package/BuiltIn backend render conformance; документ открывается без потерянного payload при отсутствующем backend; offline/reload/UPM conflict и отсутствие автоматического удаления чужого пакета.
- Large point/Repeat count, повторные drag, cancel и shutdown: budgets, отсутствие leaked Mesh/RT/atlas и лишнего полного SDF расчёта.

После проверки V0 выбрать конкретный dependency pin и числовые budgets. Сначала
реализовать V1 с простым качественным renderer, а не весь SVG и все boolean
operations одновременно. Новая схема данных и основная граница Graph/Rasterize
должны быть согласованы с F18 до публичного формата.
