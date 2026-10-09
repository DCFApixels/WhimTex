# T01. Оставшиеся оптимизации TIFF Save

- Назначение: оценить кандидатов, сохранивших смысл после уже выполненных ускорений Save/Open.
- Статус: кандидат; требуется повторная проверка кода и измерение полного Save, не только отдельных компонентов.
- Источники истины: [действующий Save/Open и методика измерений](../TIFF_SAVE_PERFORMANCE.md), [DocumentFile](../../src/WhimTexDocumentFile.cs), [DocumentContainer](../../src/WhimTexDocumentContainer.cs), [TiffImage](../../src/WhimTexTiffImage.cs), [DocumentOperation](../../src/WhimTexDocumentOperation.cs).

## Кандидаты

1. Не входить в worker dispatch при пустом `PrepareStoredBlocks`; для малого непустого задания подобрать порог по измерению. Сохранить отмену длинной работы.
2. Ускорить Auto precision scan через portable reduction или проверенную half-bit классификацию. Сохранить точные thresholds/NaN semantics; настройки слоёв не доказывают LDR-диапазон пикселей.
3. Переиспользовать conversion LUT и избежать ненужного GPU upload временного CPU-readback только в save-пути. Не менять обычный preview-контракт.
4. Разделить storage-only edits и изменения картинки только при надёжном render/dependency stamp. Учесть внешние текстуры, время, FX и native reimport.

## Границы

Reuse, streaming, ленивое Open и completion wait уже реализованы; не выполнять
их заново под видом этих задач. Старый icon stall document `.asset` закрыт
удалением backend. Дисковый second-level cache, async Save, тайлы Drawing и
новое FX dependency tracking не входят автоматически в этот список.

Оптимизация должна сохранять integrity, точное сравнение при reuse, атомарный
commit, отмену и владение буферами. Сначала измерить first/unchanged/changed Save,
Open и storage-only edit по [существующей методике](../TIFF_SAVE_PERFORMANCE.md#проверки-и-измерения).
Исторические измерения не задают нынешний приоритет и не подтверждают speedup.
