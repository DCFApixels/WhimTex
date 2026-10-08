# Производительность сохранения TIFF

- Назначение: устройство Save/Open, границы оптимизаций и методика измерений.
- Статус: реализованное поведение отделено от кандидатов; численные замеры — исторические.
- Источники истины: [DocumentFile](../src/WhimTexDocumentFile.cs), [DocumentContainer](../src/WhimTexDocumentContainer.cs), [TiffImage](../src/WhimTexTiffImage.cs), [DocumentOperation](../src/WhimTexDocumentOperation.cs), [Sha256](../src/WhimTexSha256.cs).

## Действующее поведение

Формат, integrity и атомарный commit описаны в [DOCUMENT_FORMAT.md](DOCUMENT_FORMAT.md).
Оптимизации не отменяют эти проверки и не превращают Save в асинхронный API.

### Подготовка Drawing и reuse

- Native fingerprint выбирает кандидата в bounded LRU; reuse требует точного сравнения
  исходных байтов, при необходимости через streaming inflate. Dirty/updateCount или совпадение
  быстрого хеша недостаточны: возможны прямые CPU-правки, Undo и коллизии.
- Готовый immutable stored block содержит сжатые/сырые байты и их SHA-256.
  Cache hit не повторяет compression/SHA и не удерживает ещё один полный native snapshot.
- Новые блоки сжимаются и хешируются вместе, до четырёх workers и примерно 256 МиБ
  одновременно обрабатываемого raw input. Native snapshots освобождаются до композиции/encode.
  Это бюджет обработки, не предел всей памяти Editor.
- No-op без внешних inputs/произвольных FX может пропустить compose/encode/import после
  проверок модели, пикселей, импортёра и disk revision. Для внешних assets/FX сохраняется
  консервативное сравнение результата; render dependency tracking нельзя заменить dirty-флагом.

### SHA-256

`WhimTexSha256` сохраняет обычный 32-байтный digest и последовательное состояние сообщения,
не tree hash. Burst-ядро использует переносимые uint-операции без intrinsics, OS API/native DLL.
Scratch до 1 МиБ на вызов; блоки меньше 4 КиБ и отключённый Burst используют .NET.
Worker вызывает ядро напрямую, без дополнительного Jobs-планировщика; глобальные Burst settings не меняются.
Сверять known vectors, границы padding/scratch, offsets, параллельные вызовы и фактический fallback.

### Streaming, память и отмена

- TIFF строится из native composite по полосам прямо в staging, без полных managed raw,
  converted image и итогового image byte[]. До четырёх полос в пакете; offsets дописываются seek.
  Half conversion использует небольшие LUT, Adler32 — отложенное modulo.
- Валидация проверяет полосы bounded-пакетами, не создавая второе полное decoded image.
  Контейнер тоже пишется прямо в staged stream, не через объединённый TIFF+payload массив.
- Open читает каталог и нужные блоки; Drawing материализуется лениво. SHA проверяется до
  распаковки. Соседние pixel blocks не предзагружаются; eager raw освобождается после Apply.
  Descriptor проверяет длину/дату файла: внешняя замена требует переоткрытия документа.
- `DocumentOperation.Run` использует `Task.Wait(20)`: завершение будит ожидание сразу,
  timeout оставляет опрос progress/cancel. Ошибка worker разворачивается через GetAwaiter;
  worker всегда завершён до освобождения буферов, даже при ошибке progress callback.
- Progress появляется после 400 мс и опрашивается не чаще 80 мс. До commit отмена удаляет
  свой staging и сохраняет правки; final replace/import и одиночные Unity-вызовы не прерываются.

Первый/изменённый большой документ всё ещё требует snapshots Drawing, CPU/GPU composite,
сжатия, хеширования и импорта. Streaming не делает стоимость O(1) и не ограничивает всю память процесса.

## Кандидаты, не реализовывать автоматически

Перед работой повторно проверить код и измерить полный Save, а не считать исходный приоритет вечным:

1. Не входить в worker dispatch при пустом `PrepareStoredBlocks`; для малого непустого задания
   подобрать порог по измерению, сохранив проверки отмены длинной работы.
2. Ускорить Auto precision scan через portable reduction или проверенную half-bit классификацию.
   Сохранить точные thresholds/NaN semantics; нельзя выводить LDR только из настроек слоёв.
3. Переиспользовать conversion LUT и избежать ненужного GPU upload временного CPU-readback
   исключительно в save-пути. Обычный preview-контракт не менять.
4. Разделить storage-only edits и изменения картинки только при надёжном render/dependency stamp.
   Внешние текстуры, время, FX и необходимость native reimport делают это отдельной задачей.

Дисковый second-level cache, async Save, тайлы Drawing и новое FX dependency tracking не
являются частью уже сделанных ускорений. Старые предложения reuse/streaming не нужно реализовывать повторно.
Диагноз post-save stall старых document `.asset` icons закрыт удалением того backend;
`WhimTexDocumentProjectPreview` больше не является целью оптимизации.

## Проверки и измерения

Использовать [RUNNING_TESTS.md](../Tests~/RUNNING_TESTS.md), читать выбранные cases/support
и отдельно получать разрешения на assets/build. Примеры актуальных catalog IDs:

- `document-operation-wait-smoke-v2`: completion, polling, исключения/cancel, lifetime workers и scope.
- `document-save-cost-probe-cache-safety-v2`: точный reuse, collision fallback, ownership и mutation.
- `document-save-cost-probe-hashes-v2`, `document-save-cost-probe-container-4-opaque-v2`:
  компонентная диагностика digest/compression, не весь Save.
- `small-document-save-scheduling-v2`, `small-document-save-carrier-v2`,
  `small-document-save-carrier-1024-v2`: ожидание и carrier без обещания полной скорости Save.
- `small-document-save-v2` и `document-performance-probe-ldr-random-v2`:
  сценарии диагностики полного сохранения; prerequisites/effects брать из каталога.

Измерять отдельно first / unchanged / changed Save, Open, имя слоя без изменения картинки,
pending GPU Drawing, число и разрешение слоёв, precision/sRGB, importer и Live state.
API Save без `DocumentOperation` не равен оконному Save. Внутренний log начинается после
normalization/SyncDrawing, поэтому общий stopwatch ставить вокруг публичного Save со scope.
Указать warm-up, cold/warm cache, backend/Unity/Burst, набор данных, повторения и median/tail.
Counting sink исключает render/carrier/disk/import; managed worker timing исключает GPU/UI.

Исторические замеры 2026-09-19/20 выполнялись на Windows/DX12, Unity 6000.7.0a6;
Burst-проба — 2.0. Они не подтверждают все ОС/backends, минимальную Burst 1.8 или speedup
на реальных документах. Отдельные компонентные выигрыши не суммировать в обещание общей скорости.
Артефакты новых измерений писать в проектный `Temp/WhimTex`, не в Context/Git.

## История

- [Полные этапы TIFF-оптимизации, baseline и численные замеры](https://github.com/DCFApixels/WhimTex/blob/fc4afbf765e3b7734c3fbf0baab77701367b3f02/Context~/TIFF_SAVE_PERFORMANCE.md).
- [Маленькие документы, исправление completion wait и прежний icon stall](https://github.com/DCFApixels/WhimTex/blob/fc4afbf765e3b7734c3fbf0baab77701367b3f02/Context~/SMALL_DOCUMENT_SAVE_PERFORMANCE.md).

Эти материалы сохранены для сравнения, не как текущие команды запуска или незавершённая очередь.
