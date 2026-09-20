# Проверка TIFF перед переездом

2026-09-19; Unity **6000.7.0a6**, Windows Editor / Windows64 Mono Player, DX12.
Проверяется экспериментальная реализация, не готовность всех платформ к релизу.
Старый `.asset` и публичные команды агентов не заменены.

## Дополнение 2026-09-20: Burst SHA-256

Подключён переносимый Burst SHA-256 для stored-block integrity при записи/чтении;
малые блоки и отключённый Burst остаются на .NET. Формат и digest побайтно совместимы.
**812 DocumentBurstHashProbe + 32 DocumentBurstIntegritySmoke + 54 DocumentSaveCacheSmoke** — пройдены.
Независимый .NET-only writer подтвердил byte-exact output, повреждённые raw/compressed блоки отвергаются.
Компиляция через Unity прошла. Только memory-only проверки: пользовательские документы/сцены не менялись,
новые Assets не создавались. Настройки Burst не переключались; fallback проверен отдельно.
Измерения и границы вывода: [TIFF_SAVE_PERFORMANCE.md](TIFF_SAVE_PERFORMANCE.md#burst-sha-256-2026-09-20).
Windows Editor / Burst 2.0 проверены; остальные ОС, Burst 1.8 и Player здесь не запускались.

## Дополнение 2026-09-20: точность, отмена, streaming, recovery

Unity **6000.7.0a6 / DX12**, та же экспериментальная ветка. Компиляция через подключённый Editor без ошибок.
Новый `Tests~/DocumentProductionSmoke.cs`, entry `DocumentProductionSmoke.Run`: **68 проверок**:

- Auto / EightBit / Float32: bit depth, сохранение настройки, неизменный GUID, no-op timestamp;
  Float32 сохраняет точные значения half-float композиции внутри 0–1.
- Новый streamed TIFF byte-exact с прежним writer на RGBA32/RGBAHalf, rectangular/single/multi-strip;
  повреждение полосы отклоняется. Проверка использует самостоятельный старый encode path как reference.
- Отмена до render, во время подготовки/записи и перед Commit оставляет прежний TIFF и правки;
  отмена при совпавших байтах не меняет `.meta`. Собственный staging удаляется.
- Отмена Drawing Open на worker-этапе освобождает частичную модель; следующий Open читает все байты точно;
  повреждённый pixel block обнаруживается SHA-проверкой. Частичные Unity objects не остаются.
- Отменённый Live Save возобновляет Live Update; Stop возвращает Read/Write.
- Предельные размеры/бюджет и дубликаты hierarchy отвергаются до рендера; mismatch metadata/pixel length — до Texture2D allocation.
- Recovery копирует полный staged TIFF в новый asset с новым GUID, оставляет оригинал/staging;
  существующее назначение и неполная запись не допускаются.
- Настоящий UITK control Precision создаётся в отдельном временном окне, меняет модель и поддерживает Undo;
  окно уничтожается, фокус/Selection возвращаются. Визуальная проверка всех ширин/тем не выполнялась.

Повторно: **54 DocumentSaveCacheSmoke + 38 DocumentPreparationSmoke + 44 DocumentReliabilitySmoke**;
в сумме с новым тестом **204 проверки**. Source-тесты DrawingReload/EmptyDocumentSave и проверка 75 страниц
EN/RU/ZH документации пройдены. Уникальные тестовые Assets удалены; пользовательские сцены/документы не менялись.

4K stress benchmark и честные ограничения сравнения: [TIFF_SAVE_PERFORMANCE.md](TIFF_SAVE_PERFORMANCE.md).
Отмена — не async-редактирование: уже запущенный пакет должен завершиться, render/import могут блокировать UI.
После начала Commit отмены нет. Новый progress не меняет public agent API.
Player в этом дополнении **не пересобирался**; реальный hard crash/power loss не выполнялся.
Ранее выполненный Player/build результат ниже относится к предыдущей проверке.

## Live Update и Player

`WhimTexDocumentBuildGuard` использует публичный `IPreprocessBuildWithReport`.
До упаковки прекращает Live Update, восстанавливает Read/Write и переимпортирует сохранённый TIFF.
Не сохраняет несохранённую модель; при ошибке восстановления останавливает сборку.
Live Update не включается автоматически после сборки.

Проверка выполнена реальной сборкой и запуском Player, не только вызовом callback:

- В TIFF сохранён зелёный результат 256×256; в документе оставлена несохранённая красная правка,
  опубликованная через Live Update перед сборкой. Player прочитал **зелёный**.
- Документ содержит два отключённых Drawing-слоя по 1024×1024 с псевдослучайными RGBA32-пикселями.
  Исходный TIFF — 8 391 481 байт; TIFF без модели и Drawing — 452 байта.
- В BuildReport оба импортированных варианта занимают одинаково: **196 608 байт пикселей + 144 байта
  объекта Texture2D**. Других типов объектов от этих TIFF в отчёте нет.
- Вариант с Standalone override: **DXT5, 128×128, 8 mip-уровней**; 21 872 байта пикселей + 148 байт объекта.
- Все три текстуры в Player имеют `isReadable == false`. Сборок `DCFApixels.WhimTex*` в Player нет.
- Одиночный `Resources.Load` в запущенном Player: 1,30 мс (документ), 0,87 мс (обычный TIFF),
  0,31 мс (DXT5). Это smoke-проверка загрузки, **не** cold-load benchmark и не статистическое сравнение.
- Общая сборка тестового проекта: 149 534 867 байт, 58,18 с. В неё входят прочие зависимости проекта;
  этот размер нельзя приписывать WhimTex.

Итог: в проверенном Player используется обычный texture artifact. Слои не распаковываются,
HLSL документа не компилируется и композиция не пересчитывается при загрузке.
Полный TIFF нельзя вручную класть в StreamingAssets: такой путь writer запрещает.

## Drawing: время и память

`DocumentPerformanceProbe.Run(size, layers, hdr, randomPixels)` создаёт собственные временные
assets, измеряет Save/Open и удаляет их в `finally`. Все строки ниже — `randomPixels=true`.
LDR: независимые RGB-каналы и непрозрачная альфа. HDR: коррелированные R/G, постоянные B=2 и A=1;
это разные по сжимаемости данные, не сравнение скорости LDR и HDR на одинаковом изображении.

| Документ | Исходные Drawing | TIFF | Первый Save | Без правок | Правка одного пикселя | Open |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 2048² × 4, RGBA32 | 64 МиБ | 67,80 МиБ | 6,90 с | 2,77 с | 6,51 с | 2,66 с |
| 4096² × 4, RGBA32 | 256 МиБ | 271,17 МиБ | 25,65 с | 10,87 с | 24,97 с | 10,52 с |
| 4096² × 2, RGBAHalf | 256 МиБ | 125,91 МиБ | 18,06 с | 10,80 с | 16,52 с | 3,89 с |

Условия и ограничения:

- Save включает синхронный импорт. Unity использовала обычный Default Max Size **2048**:
  выходной artifact 4K-документов имеет размер 2048², Drawing и TIFF сохраняются в 4096².
  LDR импортирован в DXT1, HDR — BC6H. Это не замер 4K несжатого artifact.
- Повторный Save сохранил timestamp во всех трёх случаях: файл действительно не перезаписывался.
- Единичные замеры в работающем Editor, не изолированный процесс. GC принудительно выполнен до фазы,
  но GPU/дисковые/Unity-кеши не сброшены. Числа не являются гарантией на других машинах.
- CPU-память всего Editor опрашивается каждые 10 мс. На Windows используется публичный
  `GetProcessMemoryInfo`: Mono `Process.PrivateMemorySize64` здесь возвращал нули.
  Короткие пики могут быть пропущены; GPU memory этим способом **не измеряется**.
- Рост private committed memory относительно начала первого Save: примерно **618 / 2563 / 1966 МиБ**
  соответственно. Пиковый managed heap: **410 / 1188 / 857 МиБ**. Это не размер runtime-текстуры.
  Базовое потребление большого открытого Editor уже составляло около 12 ГиБ private memory.

### Выводы по производительности

Ниже — baseline до оптимизации. Текущие результаты и реализация кеша:
[TIFF_SAVE_PERFORMANCE.md](TIFF_SAVE_PERFORMANCE.md). После оптимизации 4K×4 LDR Save
занимает 8,49 с вместо 25,65 с, no-op — 1,46 с вместо 10,87 с на той же тестовой нагрузке.

Большие Drawing-документы пока **не готовы к быстрому интерактивному Save**.
На 4K×4 LDR диагностический лог первого сохранения показывает около 11,09 с подготовки модели,
14,06 с carrier/сжатия/проверки и 0,48 с импорта. Только ускорение TextureImporter проблему не решит.
Даже no-op Save читает и хеширует все исходные Drawing-байты.

Исправлено найденное при замере: preview-поверхность без новых мазков больше не перечитывается
с GPU при Save/Copy. Ранее это не только добавляло readback, но и могло квантовать исходные пиксели,
делая неизменённый документ изменённым. Регрессия проверяет побайтовое сохранение RGBAHalf.

Приоритеты следующей оптимизации:

1. Измерить отдельно хеширование/сериализацию; кешировать digest только с надёжным учётом всех
   изменений Drawing, включая Undo, GPU-рисование и замену CPU-текстуры. Нельзя полагаться лишь на GUI dirty.
2. Уменьшить полные копии композита при записи TIFF, писать полосы непосредственно в staged stream.
3. Профилировать compressed-block cache (192 МиБ меньше данного 256-МиБ документа), не увеличивать
   глобальный лимит без оценки памяти. Подготовку независимых байтов можно вынести с main thread,
   Unity API/рендер/импорт — нельзя.
4. Ленивое открытие Drawing требует отдельного lifecycle/Undo-дизайна; текущий Open восстанавливает все пиксели.

## Сбои и восстановление

`DocumentReleaseValidation.Faults`: **33 проверки**, включая:

- исключение/отмена в staged write, занятый файл, конкурентную внешнюю запись;
- отсутствие частичного результата/оставленных staging-файлов после обработанной ошибки;
- «осиротевший» `.whimtex-tmp` не подменяет сохранённый документ и не удаляется без решения владельца;
- сохранность GUID, `.meta` и несохранённых правок при build boundary;
- сохранение исходного Read/Write=true, запрет билда во время Save;
- восстановление по journal, удержание journal при пропавшем asset/ошибке импорта,
  запрет его перезаписи другой Live-сессией;
- ошибка импорта после успешной записи не уничтожает TIFF; повторный Save повторяет импорт,
  даже если байты совпадают;
- внешняя замена файла блокирует запись из устаревшей модели.

Отдельно `PrepareDeferredFailure` → следующий Editor update → `VerifyDeferredFailure`:
отложенный импорт сообщает ошибку, ставит dirty для повторения, успешный повтор восстанавливает результат.
Диагностика использует `EditorApplication.update`, а не ожидание обновления Inspector.
Инъекция сбоя намеренно пишет `WHIMTEX_EXPECTED_IMPORT_FAILURE` в Console; это не ошибка пользовательских документов.

Также повторно пройдены **38 DocumentPreparationSmoke**, **44 DocumentReliabilitySmoke**,
**50 AgentApiSmoke** для старого `.asset`-API, source/control-flow тесты DrawingReload и EmptyDocumentSave.
Сборка скриптов — только Unity Editor.

Не проверено: реальное убийство Editor/отключение питания, все сетевые файловые системы,
8K/16K нагрузка, все import-worker режимы, все Sprite Editor сценарии, AssetBundles/Addressables,
другие платформы/компрессоры. Состояние после crash моделируется через journal и orphan, процесс не завершался принудительно.

## Как повторить

Через подключённый Unity CLI/Pipeline с явным `--project-path`:

1. `run_script`, файл `Tests~/DocumentReleaseValidation.cs`, entry `DocumentReleaseValidation.Install`.
   Затем штатная перекомпиляция и проверка `recompile_status`.
2. `DocumentReleaseValidation.Faults`.
3. `DocumentReleaseValidation.PrepareDeferredFailure`, затем отдельным вызовом `VerifyDeferredFailure`.
4. `DocumentReleaseValidation.PreparePlayer` вернёт **свой** путь сцены и выходной каталог.
   Использовать их для разрешённого пользователем Windows64 Player build, не менять Build Settings.
   `RestartPlayerLive` нужен только перед повторной сборкой уже созданной fixture.
5. `DocumentReleaseValidation.InspectBuild`. Запустить полученный Player с
   `-batchmode -logFile <свой каталог>/player.log --whimtex-probe-output <свой каталог>/runtime.json`.
   Нужен GPU; не использовать `-nographics`.
6. `run_script`, `Tests~/DocumentPerformanceProbe.cs`, entry `DocumentPerformanceProbe.Run`,
   `args: [2048,4,false,true]`, `[4096,4,false,true]`, `[4096,2,true,true]` по очереди.
7. `DocumentReleaseValidation.Cleanup`, штатная перекомпиляция после удаления временных scripts.

Тесты создают уникальные папки Assets; нужна явная авторизация пользователя. Пользовательские документы
не изменяют. JSON-замеры сохраняются в `Temp/WhimTex/TiffValidationResults`, Player/log — в отдельном
`Temp/WhimTex/WhimTexValidation_<guid>`. Артефакты Temp не входят в пакет и Git.
