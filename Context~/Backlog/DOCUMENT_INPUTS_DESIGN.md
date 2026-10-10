# Входные параметры: дизайн F02

- Назначение: спроектировать общий механизм параметров документа, привязок к полям, портов слоя и прямых входов shader.
- Статус: предложение от 2026-10-10, не утверждённый контракт. Подготовлен отдельный интерактивный UI-макет; реализации системы в WhimTex нет.
- Источники истины: требования [F02](DOCUMENT_PARAMETERS.md); текущие [Layer и LayerRenderContext](../../src/Layers/Layer.cs), [Noise](../../src/Layers/NoiseLayerBehaviour.cs), [ShaderFX](../../src/ShaderFX.cs), [генератор shader](../../src/ShaderFXSourceBuilder.cs), [панель FX](../../src/Editor/ShaderFXParameterView.cs), [кеш](../../src/EffectRenderCache.cs).

## 1. Предложение в одном экране

UI — **Inputs**, полное название — **Document Inputs**, по-русски «Входные параметры».
Input означает внешний вход расчёта документа, не входное изображение слоя.
Это рабочее название: глобальный глоссарий и существующие API пока не переименовывать.

Художник создаёт Detail, Tint, Seed или Time, задаёт тип и значение, затем связывает
с ними настройки. Один Detail может менять масштаб Noise, толщину Shape и силу FX.
Основной путь — привязки к подходящим полям, дополнения — необязательные смысловые
порты слоя и прямые shader inputs. Все три используют один список и один вычислитель.
Node graph не требуется: создание и подключение остаются в Inputs и Layer Settings.

```text
Inputs + временные overrides → единый снимок значений
                               ↓
                    поля / порты / shader inputs
                               ↓
                   effective settings → рендер
```

Главное правило: документ хранит base и связь, но результат связи не записывается
обратно в поле. Disconnect возвращает исходную настройку. Проигрывание анимации
не создаёт Undo и не меняет сохранённые данные на каждом кадре.

## 2. Текущий код и необходимые изменения

| Наблюдение по коду | Что требуется |
| --- | --- |
| `WhimTexDocument` — SO; `Layer` и `LayerBehaviour` — сериализуемые классы. Оба находятся в `Layer.cs`. | Inputs не должны требовать переноса всех слоёв в SO. Это отдельное решение F01. |
| `LayerRenderContext` содержит документ, текстуру, размеры и scale, но не снимок Inputs. | Передать общий context; не искать открытое окно при рендере. |
| `Noise.Render` читает поля напрямую; thumbnail считает хеш base settings. | Слой и его thumbnail должны использовать effective settings. Одного upload в FX недостаточно. |
| `ShaderFX.GetMaterial` выбирает draft/applied по name/type и загружает параметры в Material. У параметров есть ID. | Разрешать значение перед upload, без покадровой записи в `floatValue`/`vectorValue`. Не использовать индекс списка как постоянную ссылку. |
| `ShaderFXSourceBuilder` генерирует uniforms из `@param`, обрабатывает includes, резервирует `_WhimTex_`. | Ввести явные декларации прямых Inputs и проверку коллизий; сейчас этого механизма нет. |
| `EffectRenderCache` учитывает сериализованные настройки и FX stamp. FX с Unity time не кешируются обычным путём. | Учитывать реально используемые Inputs во всех зависимых кешах. |
| Изменение FX в UI проходит через ID, Undo, dirty и `NotifyValuesChanged`; отдельные controls имеют адаптеры. | Связи, Random, custom editors и агент должны использовать общий mutation path. |

Далее описана будущая архитектура, не уже реализованное поведение.

## 3. Сравнение способов подключения

| Подход | Действие пользователя | Ответственность слоя/FX | Польза | Ограничение |
| --- | --- | --- | --- | --- |
| Поля | Цепочка возле Opacity, Scale X, Offset Z или Amount FX. | Дескриптор редактируемого поля: type, base reader, операции, ограничения. | Подключает существующие настройки без отдельного порта для каждой. | Не каждое сериализованное поле безопасно или осмысленно подключать. |
| Порты | Подключает Uniform Scale, Motion или Density в блоке Inputs слоя. | Объявляет типизированный вход и его влияние на settings. | Один смысловой вход управляет несколькими настройками согласованно. | Набор возможностей ограничен предусмотренными портами. |
| Shader inputs | Создаёт Input с shader key; FX явно объявляет и читает этот key. | Объявляет type, local uniform и fallback. | Не нужно связывать общий Time отдельно с каждым FX. | Работает для автора shader; не заменяет поля обычных слоёв. |

### Поля: произвольная настройка, но не любой участок памяти

Предоставлять подходящие редактируемые настройки, не произвольную reflection-ссылку.
ID слоя, тип поведения, дети, FX collection, GPU-буферы, служебные флаги и пути
файлов не являются целями. Кнопки — действия, не значения.

Числа, векторы и цвета допускаются через descriptor; Bool — только Replace.
Структурные поля и размер ресурсов требуют явного допуска подсистемы и её limits.
Enum — возможное расширение Replace с проверкой конкретного enum-типа, не произвольный int.

Один descriptor обслуживает UI, агентский API, validation, mixed values и evaluation.
Для C# его строить из поддерживаемого описания/атрибутов; для FX — из `@param`.
Reflection допустима при построении карты, не при поиске property path на каждом кадре.
Это не отдельная ручная карта соответствий для каждого UI режима.

### Порты: добавлять смысл, а не копию списка полей

Пример Uniform Scale: базовый множитель 1, применённый к Scale X/Y/Z с сохранением
пропорций. Пример Motion: смещение домена, которое слой переводит в свои координаты.
Это примеры дизайна, не утверждённый набор портов Noise.

Port definition: стабильный key, тип, base setting, диапазон, операции, применимость,
перечень затрагиваемых settings и dependencies. C#-реализация рассчитывает settings,
не мутирует документ и не содержит сериализованный delegate. Обычное поле может
стать автоматически предоставленным портом, но ручные порты нужны для составного смысла.

Прямой binding поля и порт, влияющий на это поле, одновременно активировать нельзя.
Показать конфликт и предложить явно заменить связи; не использовать скрытое правило
«кто последним применился». Пересекающиеся порты требуют отказа либо заранее
объявленного контракта композиции слоя.

Рекомендация: начать с полей, сразу предусмотреть target `Port`, но проверить
один полезный составной порт перед массовым добавлением. Shader access подключать
к тому же snapshot после проверки обычного binding pipeline.

## 4. Данные и идентичность

Названия ниже — проектируемые сущности, не существующие C# API.

| Сущность | Данные |
| --- | --- |
| `DocumentInput` | `id`, display name, value type, saved value/default, presentation range, optional shader key, optional time role. |
| `InputBinding` | `id`, `inputId`, target, operation, component selector, enabled. |
| Target | `layerId`; optional `fxInstanceId`; Field/Port kind; stable field/port key; для FX — идентификатор декларации и проверяемый type/name contract. |
| Port definition | Объявление в коде типа слоя, его base setting и adapter; не исполняемый код из файла документа. |
| `InputEvaluationContext` | Временный неизменяемый snapshot Inputs/overrides, time/frame, dependency signature, diagnostics. |

Base остаётся в поле владельца; базовые значения портов — в settings слоя.
Bindings — централизованный список документа, удобный для usages, clipboard и API.
Не дублировать одну связь одновременно в FX, поле и документном реестре.

`DocumentInput.value` — единственное сохранённое значение и значение по умолчанию
для расчёта без override. UI подпись Value редактирует именно его; второй постоянно
сериализованный current value не нужен. `default=` в декларации shader — fallback
при отсутствии Input, а не копия его сохранённого значения. Значение Timeline Time
в контексте не перезаписывает `DocumentInput.value`.

Переименование подписи Detail не ломает связь: используется input ID.
`shaderKey` — отдельный публичный key, например Time, а не автоматическое производное
от display name. Обычно скрыт в Shader Access. Input ID — не Unity asset GUID.

Позиция в списке, имя слоя и `SerializedProperty.propertyPath` не являются
постоянными адресами. Динамическим элементам нужны ID; фиксированным структурам —
field key и selector X/Y/Z/R/A. При возможном Layer SO меняется storage adapter,
но не смысл target. Идентификатор FX instance отделён от Source ID пресета F14.

### Правила ID и изменение интерфейса

Input, binding и FX instance получают UUID при создании; уникальность проверяется
в документе, значения сохраняются в TIFF/JSON. Layer использует свой существующий
ID. Дубликаты в файле — структурная ошибка, не команда генерировать новые ID при
чтении. GUID presets из F14 не является идентификатором экземпляра блока.
Один редактируемый Shader FX instance в документе принадлежит одному блоку;
повторное добавление создаёт новый instance, хотя source может быть общим.

Для FX field target использовать существующий ID сохранённого параметра плюс
ожидаемые name/type. При reparsing сохранять ID совместимой декларации;
несовместимый или исчезнувший параметр не заменяется соседом по индексу.
Для прямого shader input slot key — локальное имя объявленного uniform и его тип.
Field/port keys объявлены автором типа, стабильны при переименовании C# private
fields или UI labels. Намеренное изменение ключа интерфейса — breaking change:
старую ссылку оставить unresolved с diagnostic, без aliases и автоматической миграции.

## 5. Типы и точные правила вычисления

Предлагаемый полноценный набор: Number, Integer, Boolean, Vector2/3/4, Color.
Textures, Transform целиком, Gradient, Curve, enum и произвольные массивы — позже.
Transform подключается осмысленными position/rotation/scale components, не сложением
проективных матриц. Point — Vector2 с указанным пространством; units/space цели
показаны при подключении, нормализованные координаты не смешиваются с pixels автоматически.

| Цель | Replace | Multiply | Add |
| --- | --- | --- | --- |
| Number | Number или выбранный числовой компонент | Number | Number |
| Integer/Seed | Integer | Integer с overflow validation | Integer с overflow validation |
| VectorN | Та же размерность | VectorN покомпонентно или Number для всех компонентов | VectorN или Number для всех компонентов |
| Color | Color | Color покомпонентно или Number для RGB, без изменения A | Color или Number для RGB, без изменения A |
| Boolean | Boolean | — | — |

Scalar Replace целого Vector не подразумевает заполнение всех компонентов:
выбрать компонент или явное преобразование. Normal нормализуется по контракту
цели с определённым fallback для нулевого вектора. Color-арифметика — linear RGBA;
для скалярного управления альфой выбрать компонент A. Не делать неявный RGB average,
sRGB conversion или float→seed. Эти правила требуют согласования.

Расчёт: snapshot Input → selector/conversion → `input`, `base × input` или
`base + input` → hard constraints и групповая нормализация цели → consumer.
UI различает raw result и ограниченный result.

Диапазон Input slider по умолчанию мягкий, не скрытый clamp. Hard Limit возможен
как отдельный явный выбор. Целевой hard range обязателен; HDR и negative не
обрезаются глобально. NaN/Infinity и overflow диагностируются.

Constraint adapter цели имеет две фазы: проверка type/finite/overflow отдельного
raw значения, затем согласованная нормализация группы effective settings.
Результат содержит value, status и diagnostics; не бросает исключение из произвольного
UI callback. При невалидном field/port значении interactive preview использует
валидированную base, как broken binding; strict export отказывает. Если сама base
невалидна, слой отдаёт штатную ошибку/bypass, не пропускает NaN в GPU. Для прямого
shader input — объявленный fallback или ошибка FX. Number→Integer без явного
правила не разрешён; Integer arithmetic вычислять с проверкой в более широком
типе и отклонять выход за Int32. Clamp обычного конечного значения не считается
ошибкой и не запекается в base.

Максимум одна связь на целое поле. Можно независимо связать непересекающиеся
компоненты; цель целиком и её компоненты одновременно не активируются.
Для Shape corners сначала вычислять raw группу, затем ограничивать effective copy
по геометрии. Анимация не имеет последнего отредактированного угла: применять
пропорциональное ограничение. Интерактивный base edit сохраняет обычный приоритет.

MVP: только Inputs→targets, без выражений, Inputs→Inputs и обратной записи shader.
В этом binding graph нет циклов; texture dependencies остаются отдельным графом.

## 6. Shader access без обязательной привязки каждого FX

### Не переопределять Unity time

`_Time` — встроенный Unity `float4`, связанный со временем Unity, не документа:
[официальный контракт](https://docs.unity3d.com/6000.0/Documentation/Manual/SL-UnityShaderVariables.html).
Input с именем Time не переопределяет её и не снимает действующее предупреждение.
Time остаётся обычным ручным значением, пока ему явно не назначена роль F06.

Предлагаемый namespace — `_WTInput_Time`. Значение передаётся каждому FX перед
его использованием через поддерживаемые
[material properties](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Material.SetFloat.html).
Конкретная изоляция Material/PropertyBlock зависит от draw API: не обещать
PropertyBlock для blit-вызова, который его не принимает.
`Shader.SetGlobal*` не использовать: документы и экспорт не должны делить
изменяемое состояние. Даже общий Material полностью заполняется на каждый pass,
включая fallback, чтобы не унаследовать значения прошлого документа.

### Переносимые декларации

Пример будущего, сейчас **неподдерживаемого** синтаксиса:

```hlsl
// @input _WTInput_Time float key=Time default=0
// @input _WTInput_Tint color key=Tint default=(1,1,1,1)

float4 ApplyFX(float2 uv, float4 color)
{
    float pulse = 0.75 + 0.25 * sin(_WTInput_Time);
    return color * float4(_WTInput_Tint.rgb * pulse, 1);
}
```

FX объявляет key/type/local variable/fallback; генератор объявляет только эти uniforms.
Все FX с key Time автоматически получают один Input. `@param` — локальные настройки,
`@input` — документные значения: они не заменяют друг друга. Таким образом ручная
поточечная настройка Time в каждом FX не нужна, но зависимости известны.

При первом успешном разрешении key хранить mapping к input ID. Изменение display
name безопасно. Изменение key — отдельная операция с usages, не переписывание HLSL
по строковой эвристике. Между документами preset переносится по key/type, не чужому ID.

Если key отсутствует — явно объявленный fallback + warning; без fallback — ошибка
входа FX и штатный bypass. Type mismatch, коллизия uniforms, дубли key и
несовместимые декларации из includes диагностируются. Не создавать Inputs при
открытии/рендере автоматически; Create missing inputs — явная Undo-операция.

Не инжектировать все Inputs в каждый shader: несвязанный новый параметр не
перекомпилирует весь документ. Значения меняются upload-ом. Декларации/interface
меняют compiled interface. Dependencies собираются после обработки includes,
не эвристическим поиском произвольных переменных в коде.

### Точность предлагаемого интерфейса

Грамматика первого этапа: `@input <uniform> <type> key=<key> [default=<literal>]`.
Uniform — HLSL identifier в namespace `_WTInput_`, key — ASCII identifier,
сравнение case-sensitive. Типы: `float`, `int`, `bool`, `float2/3/4`, `color`;
соответствие Inputs — Number, Integer, Boolean, Vector2/3/4, Color. Color отличается
от float4 семантикой linear RGBA. Literal — конечное число, целое Int32,
true/false либо список ровно N компонентов в скобках; color default имеет 4.
Для bool применить текущий совместимый shader representation 0/1, не обещать
новый backend-specific upload. Точный shader wrapper для Integer подтвердить
тестом supported APIs при реализации, не хранить seed в float с потерей точности.
Одинаковые повторные декларации после include expansion объединяются, если
uniform/type/key/default совпадают. Любое различие — ошибка interface.

Автоматические соответствия хранить централизованно как `ShaderInputLink`:
FX instance ID, slot key, ожидаемые type/key, resolved input ID либо unresolved.
Новые слоты разрешаются по key/type при добавлении FX или изменении interface
одной document transaction, не изменением модели из render. После разрешения ID
имеет приоритет; Rename display name не влияет. Изменение shader key Input по
умолчанию запрещено при использованиях: UI предлагает оставить key либо явно
переподключить по ключам все затронутые links с показом fallback/ошибок.
Изменение key в декларации FX означает новый interface и повторное разрешение
этого slot; остальные links не сбрасываются. Удалённый input ID не заменяется
новым одноимённым объектом автоматически: Repair/Resolve missing inputs — явное действие.
Link records входят в сохранение, Undo и clipboard remap наравне с field bindings.

Для Material references возможно отдельное явное сопоставление Input ID→существующий
uniform. Оно не объявляет переменные и не мутирует общий Material asset. В MVP —
только Shader FX; неподдерживаемые объекты списка FX не обещать подключать автоматически.

## 7. UI и действия пользователя

Inputs — отдельная вкладка рядом с Layers, не ряд в шапке Canvas View.
Список: Name / Type / Value, Add Input. Детали: имя, тип, saved/default value,
мягкий диапазон; свёрнутые Shader Access и роль Timeline Time. Usages показывает
число и раскрываемые ссылки на потребителей.

У подходящего поля справа цепочка. Popup: Input, selector, mode, Base/Result;
несовместимые источники недоступны с причиной. После подключения effective value
read-only, подпись `Detail × Base`. Base value редактируется в отдельном раскрытии;
при Replace прямо указано, что base сейчас не влияет. Не менять base незаметно,
когда пользователь думает, что редактирует result.

Create Input from this value создаёт совместимый Input и Replace без изменения
картинки. Для Multiply нейтральное 1, Add — 0. Если исходная цель имеет constraints,
показывать результат перед подтверждением, не менять их молча.
Disconnect возвращает base; Bake value and disconnect записывает resolved result
одной Undo-операцией. Save не запекает связи. Drag Input→field — возможный shortcut
того же popup, не обязательная механика первого этапа.

Порты находятся в компактном блоке Inputs слоя и используют те же controls.
Затрагиваемое поле показывает управление портом, а не второй конфликтующий edit.
Прямые shader inputs: свёрнутый список Key / Result / Source. При совпадении key
подключение автоматическое. Per-FX override прямого входа не включать в MVP.

### Мультиредактирование и Random

Сравнивать отдельно base, input ID/selector/mode и effective result. Равный результат
не означает равную связь. Shared fields — пересечение применимых descriptors;
mixed управляющий режим не создаёт случайный общий блок. Связанный Bool управляет
видимостью по resolved state, а при multiselect берётся пересечение.
Binding edit — одна транзакция для всех подходящих выбранных целей.

Random Seed/Random All выполняются независимо для объектов. При Replace обычный
Random не должен менять неработающую base или общий Input молча: предложить
изменить Input либо отключить связь и рандомизировать локально. Add/Multiply может
менять base с показом результата. Custom FX editor F13 использует общий field context.

`SerializedObject` обслуживает сохранённые данные и Undo, но
[hasMultipleDifferentValues](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SerializedProperty-hasMultipleDifferentValues.html)
не сравнивает вычисленные значения из внешнего context автоматически. Им нужен
общий resolved-value view. `BindProperty` не является вычислителем трёх операций.

## 8. Рендер и кеш

Один immutable snapshot на render request: transient overrides по ID, затем saved
values. Неизвестный ID/type override — явный отказ. Роль Timeline Time получает
значение из этого же context, не определяется по имени. CPU time double, shader
получает согласованное float-представление; точность больших времён относится к F06.

Binding plan разрешает IDs/descriptors при смене структуры. На рендере — typed
values и известные dependencies, без покадрового поиска строковых путей/JSON.
LayerBehaviour читает typed effective settings или небольшой view поверх base+overrides.
Не клонировать весь документ на кадр и не подменять public fields с последующим
возвратом: вложенный рендер и исключения делают это небезопасным.

Effective transforms определяют transform cache. Constraints, curve/gradient
LUT и связанные группы settings используют effective values, не base.
Snapshot проходит composite, group, Target, clipping, FX texture-layer references,
Layer Preview, thumbnails, export и будущие Outputs. Собственные preview caches
слоёв тоже получают context/signature; `GetPreviewTexture(size)` без context недостаточен.

Stamp включает effective settings, plan/interface revision и значения реально
используемых Inputs. Rename не меняет pixels; неиспользуемый Input не инвалидирует
весь документ. Группы/Target наследуют зависимости обычным графом. Overrides двух
документов/кадров не считаются равными из-за одного revision counter: учитывать
значения, для будущих ресурсов — content signature.

CPU-работа должна масштабироваться по активным связям/входам; GPU — по изменённым
settings. Это цель, не benchmark. F04 On Refresh показывает stale state после
смены Input до ручного пересчёта; точная политика остаётся в F04.

## 9. Undo, ошибки и потерянные ссылки

Create/edit/remove/bind/Bake/Random идут через один mutation service: validate
сначала, потом atomic commit и один Undo. В нынешней модели Undo записывает document
и затронутые FX SO; после возможного Layer SO — владельцев. Play/seek/overrides
не dirty и не Undo. Agent revision checks/live locks действуют также при смене
общего Input, который затрагивает заблокированную цель.

Предлагаемая политика broken field/port binding: сохранить связь, применять base
в interactive preview и показывать warning. Save не удаляет её и не запекает fallback.
Строгий unattended export отказывает; продолжение с fallback требует явной опции
и возвращает diagnostics. Нескомпилированный FX — штатный bypass. Невозможность
сохранить данные без потери по-прежнему требует защиты Save/подтверждения пользователя.

FX inputs используют общий FX diagnostics. Документные проблемы имеют ту же
структуру: code, severity, input/binding/target IDs, message, resolution. UI и агент
видят одно состояние. Console dedup по причине/состоянию, не на каждый render.
Missing source, type mismatch, missing target и conflict различаются. Ожидаемый clamp
показывается у значения, не спамит warning каждый кадр.

## 10. Жизненный цикл и файлы

| Операция | Предлагаемое поведение |
| --- | --- |
| Rename | Display name независимо от shader key; key edit показывает usages. |
| Change type | Проверить всех потребителей; не выполнять частичную конверсию. Предложить совместимое изменение либо новый Input. |
| Delete Input | Показать usages: cancel, Disconnect to base или Bake current values and disconnect. Для shader access отдельно показать будущий fallback/error: bake поля не заменяет HLSL-вход. |
| Delete/retype Layer | Явное удаление убирает bindings одним Undo. Retype сохраняет общие keys; потерянные behaviour keys остаются с diagnostic до подтверждённого удаления. |
| Duplicate locally | Новые layer/FX instance/binding IDs, те же input IDs. |
| Paste between documents | Фрагмент содержит используемые Inputs; Import as new или Reuse compatible input. Display name не разрешает слияние. Shader key conflict требует выбора. |
| FX source update | Перепроверить declaration/schema; сохранить base/связи либо диагностировать конфликт, не сдвинуть индекс. Связать с F14. |
| Rasterize | Запечь текущий snapshot, явно сообщив об исчезновении процедурных зависимостей. |
| TIFF/JSON | Один смысловой контракт inputs/bindings. Resolved values, кеш и Material IDs не сериализуются. |

При реализации определить версии форматов и генераторов схем; сейчас не поднимать
их и не мигрировать существующие документы. Full/FullOptimized/Compact сохраняют
влияющие base, ссылки и неактивные связи, если их потеря не разрешена явно.

Концептуальный фрагмент будущих данных — **не готовый к вставке документ**:

```json
{
  "inputs": [{ "id": "input-detail", "name": "Detail", "type": "Number", "value": 1.25 }],
  "bindings": [{ "id": "binding-scale", "inputId": "input-detail", "operation": "Multiply",
    "target": { "layerId": "layer-noise", "kind": "Field", "key": "noise.scaleX" } }]
}
```

## 11. API и связи с беклогом

Проектируемые операции: list/create/update/remove Inputs; list capabilities/usages;
bind/update/unbind; evaluate с transient overrides. Ответы различают base, binding
и effective result. Сначала запрос descriptor key/type/allowed operations, потом
изменение; не угадывать target по подписи UI. Общие transaction/dryRun/revision/lock
правила используются UI и агентом, а не копируются двумя реализациями.

- [F01](MULTI_EDITING.md): descriptors/mutation path; SO не обязательная предпосылка Inputs.
- [F06](ANIMATION_DESIGN.md): Time — роль Input; timeline и bake передают overrides, не меняют base.
- [F09](WHIMTEX_IMPORTER.md): reimport/Bake берёт saved values или явно заданный snapshot, не состояние окна.
- [F10](TEXTURE_SETS.md): один snapshot для нескольких Outputs, без отдельной копии параметров каждой карты.
- [F11](FX_SEED_PARAMETERS.md): Integer seed и общий контракт действий Random.
- [F13](FX_CUSTOM_EDITORS.md): общий field context для bindings/mixed values при любом внешнем виде.
- [F14](FX_IDENTITY_VERSIONING.md): source version/interface отдельно от input IDs документа.

## 12. Этапы и критерии проверки

1. Вертикальный срез Number/Integer: Inputs, Opacity, Noise Scale/Seed, FX Float,
   три операции, Disconnect/Bake, Undo и файлы; реальный context, не только UI.
2. Vectors/Color/Bool, components, constraints, multiedit и охват остальных допустимых полей.
3. Один полезный составной порт и конфликты через тот же UI/API; оценить пользу до массового внедрения.
4. Shader declarations/includes, key/fallback, изоляция, diagnostics и кеш; затем F06.

Будущие проверки: операции/нейтральные значения; hard/soft ranges/HDR/negative/NaN;
component overlap/port conflict; два документа с одинаковыми keys; вложенный рендер;
неиспользуемый Input не ломает кеш; Time обновляет зависимости; все render paths;
Undo/Redo, remap, missing links, FX update, TIFF/JSON roundtrip; совпадение UI/API diagnostics.
Наборы запускаются по этапам, не после каждой косметической правки.

Макет сравнивает «Поля / Порты / Шейдер», показывает создание Number/Integer/Color,
диапазон, связи, операции и Base/Result. Картинка схематическая; нет настоящих
слоёв, HLSL-компиляции, Unity Undo, файлов и замера производительности.
Это проверка понятности взаимодействия, не технической готовности системы.
Макет намеренно ограничивает Color режимом Replace, не демонстрирует component
selectors, конфликты полей с портами, HDR и typed conversions из полного дизайна.
Shader key в макете разрешается напрямую по строке: persistent links, подтверждение
переподключения и versioned interface здесь намеренно не реализованы. Проверка
читателем без истории разговора выявила эти границы; ID, links и invalid-value
policy выше уточнены после неё.

Проверка макета 2026-10-10: в браузере подтверждены создание Number/Integer/Color,
Replace/Add/Multiply, обычный Disconnect (возврат base), Bake & Disconnect,
изменение Input→результат FX, общий Time для двух FX и составной Uniform Scale.
Проверены широкая/узкая компоновка, отсутствие JS errors в этих сценариях,
синтаксис fragment и относительные ссылки этого дизайна/карточки/индекса.
Повторная проверка читателем без истории не обнаружила противоречий в уточнённых
правилах идентичности, shader links, типов и недопустимых значений. Это не Unity
прогон и не подтверждение будущих render/serialization контрактов.

Перед реализацией согласовать название Inputs; поля как основной путь плюс порты;
стартовые типы и Color/Integer conversions; `@input`/shader key; strict export/fallback.
Все перечисленные детали остаются предложением, не новыми обязательными правилами.
