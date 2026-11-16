# Карта тестов WhimTex

Начинай с подсистемы в `Cases/`. Один файл может обслуживать
несколько сценариев; точные ID и вызовы находятся в [каталоге](scripts/test-catalog.json).

Для замеров компиляции графа: [полигон Shader editing](Fixtures/ShaderGraphEditing/README.md),
профиль `shader-editing-playground` и отдельный прогон `shader-editing-sequence`.

```text
Tests~/
  Cases/       Проверки продукта, сгруппированные по подсистемам
  Framework/   Общие проверки, владение ресурсами и помощники запуска
  Fixtures/    Входные файлы и эталоны, не результаты запусков
  scripts/     Каталог и запускатель
```

## Где искать проверку

| Папка в Cases | Что проверяет |
| --- | --- |
| `Documents` | TIFF/JSON, Save/reopen, recovery, Live Update, ошибки импорта |
| `Layers` | Модель и выбор слоёв, трансформы, Shape, Noise, SDF |
| `Rendering` | Композиция, clipping, HDR, кеш, thumbnails, состояние рендера |
| `FX` | Эффекты, HLSL, параметры, пресеты и инструменты их редактирования |
| `Painting` | Кисти, Drawing, заливка, выделение, Healing / Content Fill |
| `Canvas` | Canvas View, окно, навигация, направляющие и общие UI-контролы |
| `Color` | Цвет, пипетка в Color Picker, градиенты, история и библиотеки |
| `Clipboard` | Перенос слоёв, изображений, кистей и FX |
| `Seamless` | Бесшовность, quilting, histogram и управление краями |
| `UV` | UV-острова, источники mesh и интерфейс UV |
| `Automation` | Agent / Live API, команды и примеры |
| `Export` | Экспорт и независимый PSD writer/reader |
| `Compatibility` | Файловая база 0.12.5 и удаление ненужных rename-маркеров |
| `Contracts` | Общие договорённости, терминология, настройки и запрет старых API |
| `Runner` | Запускатель и протоколы тестов, не функции продукта |
| `Player` | Отдельно разрешаемая сборка и проверки в настоящем Player |

C# и Node-проверки одной подсистемы лежат рядом. Расширение `.mjs` не гарантирует,
что тест обойдётся без Unity: reload/Player workflows тоже оркестрируются через Node.
Смотри `runner`, `requiresUnity`, `category` и `effects` в каталоге.

## Как выбрать ограниченный набор

Имена тематических профилей соответствуют папкам: `documents`, `layers`, `fx`,
`painting`, `canvas`, `color`, `clipboard`, `seamless`, `uv`, `automation`,
`export`, `rendering`, `contracts`. Они включают проверки поведения и исходников,
но не самостоятельные диагностики. `compatibility-files` охватывает раздел совместимости;
старый маленький `compatibility` остаётся выборкой, не полной гарантией.

После 0.13.0 обратная совместимость не является требованием разработки. Эти профили
проверяют ещё существующие readers, а не обязывают сохранять их: при снятии контракта
убрать соответствующие сценарии из каталога/профилей и обеспечить тесты текущего формата.
Политика — в [AGENTS.md](../AGENTS.md#identity-and-compatibility).

- `--id` / `--ids` — одна проверка или точный список.
- `--profile node` — read-only Node-проверки без Unity.
- `--profile regressions` — только регрессионные сценарии.
- `--profile source-guards` — только проверки исходников и контрактов.
- `--profile verify` — все недиагностические сценарии, включая инфраструктуру.
- `--profile ci-docs` — ограниченная проверка документации.
- `--profile tiled-drawing-integration` — Raster в Drawing: рендер, потребители, история и сохранение.
- `--profile tiled-drawing-8k` — отдельные тяжёлые замеры, Undo и TIFF roundtrip на 8К.
- `--profile tiled-drawing-cycle-audit` — отдельная диагностика полного цикла Drawing/viewport и влияния скрытых тайлов. Ограниченные подвыборки: `tiled-drawing-cycle-smoke` (2К), `tiled-drawing-cycle-8k` и `tiled-drawing-occupancy`.
- `--profile paint-undo-commit` — quota/Reserve/Complete, отказы записи, освобождение резервов и точная история; `paint-undo-commit-8k` отдельно проверяет реальные 8К, маску, signed-тайлы и TIFF.
- `--profile paint-undo-reuse` — повторное использование предыдущих тайлов, очередь, ветвление, native Undo и восстановление после отказа чтения; `paint-undo-reuse-8k` проверяет точные байты на 8К, `paint-undo-reuse-existing` — существующие сценарии истории, `paint-undo-reuse-benchmark` — отдельные штампы и спам 2048/4096 px внутри и снаружи Transform. См. [замеры и ограничения](Cases/Painting/PaintUndoReuseBenchmark.md).
- `--profile drawing-native-undo` — точное восстановление CPU/GPU после HDR → 8-bit для обычных и тайловых слоёв, три Undo/Redo, сохранность исходника при наличии превью и отсутствие лишних загрузок при фоновой фиксации мазков другого документа.
- `--profile drawing-initialization-edges` — пустые CPU/GPU пиксели без рабочего RT, формат, фиксированная рамка UV и сохранность существующего содержимого; `drawing-initialization-8k` — создание слоя и первый мазок Brush/Pencil с маской и точным Undo/Redo. См. [сравнение до/после](Cases/Painting/DrawingInitializationBenchmark.md).
- `--profile drawing-first-stroke-paired` — короткие чередующиеся пары старой/новой инициализации для 81/4096 px; `drawing-first-stroke-settled` отдельно проверяет влияние ожидания GPU перед мазком. Измеряется видимый незавершённый мазок; длинные синхронные серии 8К-аллокаций не использовать.

`quick` и `core` — небольшие базовые выборки. Выбор профиля не разрешает ассеты,
изменения пользовательского состояния или Player build. Команды, review fingerprint,
разрешения и трактовка результата — в [RUNNING_TESTS.md](RUNNING_TESTS.md).

## Общие помощники

`Framework/TestApi.cs` и `test-api.mjs` — утверждения и единый результат.
`Framework/Unity` — владение transient-объектами и восстановление состояния Editor;
`Framework/Node` — расчётные и текстовые помощники;
`Framework/Workflows` — общая оркестрация реального reload;
`Framework/Native` — исходники временных native/Player probes.

Opt-in диагностики `tiled-paint-readiness` и `tiled-paint-comparison` используют
рабочий backend из `src/Layers/TiledPaint` и его шейдеры. В bundle входит только
адаптер простых штампов из `Framework/Painting`; копия backend больше не компилируется.
Dense-эталон `TiledPaintNativeOracle` отдельно вызывает `PaintStrokeBuffer`.
Фактическую интеграцию Drawing проверяют случаи `TiledDrawing*`.

`TiledDrawingCycleBenchmark` вызывает реальный цикл мазка и региональный рендер
по обновлениям Editor, без управления пользовательским окном. `Start`/`Poll`/`Cancel`
возвращают структурированный статус; результаты сохраняются в
`Temp/WhimTex/raster-cycle-audit`. Это измерение вызовов модели, очереди GPU и истории,
а не частоты отображения кадров на экране. `TiledDrawingOccupancyBenchmark` отдельно
изолирует стоимость 0/64/256/1024 невидимых тайлов при неизменном видимом результате.
Обе диагностики запускаются явно, вне `quick`, `regressions` и `verify`; сценариям
цикла нужен эксклюзивный доступ к Editor и стеку Undo на время замера.
Перед тяжёлой матрицей нужны не менее 4 ГиБ свободного места на томе истории PaintUndo
и запас оперативной/видеопамяти: исходная 8К Half-текстура занимает 512 МиБ CPU,
а GPU-пул допускает до 2 ГиБ сверх отдельных исходников, масок и результата рендера.
Нужен также запас общей квоты Undo, чтобы тест не вытеснял историю других документов.
Измерения при нехватке диска учитываются отдельно от обычного цикла рисования.
Результаты и ограничения текущего прогона — в [аудите цикла Raster](Cases/Painting/TiledDrawingCycleBenchmark.md).

В Unity-помощниках сохранены прежние имена `UnityAScope`, `UnityBRun`, `FixtureContext`,
`MigrationD`, `AsyncD`. Это существующие реализации разных жизненных циклов, а не разделы
продукта. Их не объединяли и не переписывали ради перестановки папок.

Новую проверку добавляй в подходящий `Cases/<подсистема>`, указывай поддержку явно
в каталоге и включай ID в нужные профили. Само наличие файла в папке не регистрирует тест.
Данные добавляй в `Fixtures`; отчёты и временные входные bundle — только в проектный
`Temp/WhimTex`. [Эталонные входы и снимки](Fixtures/Oracles/README.md) сохранены в пакете;
для запуска тестов не нужны Git, прежняя папка Legacy или проектная папка `output`.

## История

Перенос старых тестов завершён; инструменты его сверки удалены из рабочей системы.
Оригиналы восстанавливаются из Git-коммита `ca8603c0961ce36064280f952259f8a6142d46cc`,
путь `Tests~/Legacy`. Карты покрытия и итоговый отчёт сохранены в коммите
`0539ee83110630664344ef4c274d6c87fd454556` (`Tests~/Batches`, `Tests~/CoverageAudit`,
`Tests~/ArchiveRetirement.ru.md`). Это история, не входные данные текущего запускателя.
