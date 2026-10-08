# Файловая база WhimTex 0.12.5

Образцы записаны исходным runtime тега `v0.12.5`, коммит
`a72cc9544d39f93555ca6e9f39c137340e3028f0`, в Unity `6000.7.0a6`.
Перед захватом изменений в tracked `src/` относительно тега не было.
Не пересоздавать образцы текущим writer: это уничтожит независимый эталон.
Одноразовый генератор образцов удалён после фиксации базы; эталоны не перегенерируются.

## Что зафиксировано

- TIFF: процедурная композиция, SDR Drawing и HDR Drawing с исходниками 12×8 на холсте 32×24.
- JSON: Full, FullOptimized, Compact и фрагмент слоёв.
- Архивный композитор `.asset` — только отрицательный образец для проверки отказа; он не входит в совместимость и не импортируется.
- Пресет кисти `.sebrush` и JSON градиента.
- Группа, double-повороты, ссылки эффекта на слой, ручной параметр ShaderFX и общая идентичность встроенного FX.
- Исходные байты Drawing, формат и sampling; `.meta` для TIFF/asset; SHA-256 каждого исходного файла.
- `.rgba32f`: последовательные little-endian RGBA float32 итогового рендера; `.png`: картинка для осмотра.

Модель упорядочена сверху вниз: непрозрачный Background находится в конце списка.
Каждый эталонный рендер неравномерен; HDR/SDR дают разные изображения.
Сравнение результата допускает погрешность GPU 0.002 на канал. Исходные Drawing
проверяются по точным байтам, без такой погрешности.

## Проверки

Из корня Unity-проекта, только через его подключённый Editor:

```powershell
unity command run_script --file 'Packages/com.dcfapixels.whimtex/Tests~/Compatibility0125Smoke.cs' --entry Compatibility0125Smoke.Run --timeout_ms 50000 --format json --project-path 'D:\DCFA\Projects\Test6.6'
unity command run_script --file 'Packages/com.dcfapixels.whimtex/Tests~/Compatibility0125ReaderSmoke.cs' --entry Compatibility0125ReaderSmoke.Run --timeout_ms 50000 --format json --project-path 'D:\DCFA\Projects\Test6.6'
```

Первый тест копирует семь поддерживаемых TIFF/JSON файлов в свою временную Assets-папку, открывает, сравнивает
модель/пиксели, сохраняет в TIFF и проверяет повторное открытие. Второй проверяет
узкий пропуск снятых полей, ограничения и защиту неизвестных данных.
Из корня пакета `node Tests~/Compatibility0125.test.mjs` проверяет целостность
эталонов без Unity. JSON-схема проверяется `DocumentJsonSchema.test.mjs`.

## Граница покрытия

Это начальная база, не гарантия для всех файлов. В ней нет внешних File-ссылок,
отдельных ShaderFX-ассетов/HLSL include, кисти с bitmap-tip, multiple-sprite slicing
и эталонного рендера каждого типа слоя. Их нужно добавить перед удалением
соответствующих reader/runtime-путей. Современный `DocumentPayloadCoverageSmoke`
проверяет все типы behaviour, но не заменяет эталоны, записанные старой версией.
Публичный C#/агентский API, EditorPrefs и layouts в эту базу не входят.
