# Healing / Content-Aware Fill — алгоритм и ограничения

- Назначение: сохранить устройство общего worker и причины решений о качестве реконструкции.
- Статус: реализовано; известные ограничения и отклонённые эксперименты указаны отдельно.
- Источники истины: [ContentAwareFill](../src/ContentAwareFill.cs), [Healing tool](../src/WhimTexWindow.Healing.cs), [договорённости инструмента](DECISIONS.md#восстанавливающая-кисть).

## Основа алгоритма

Это процедурное заимствование исходных пикселей, не генеративная модель и не illumination matching.
Общий managed worker используется Healing и Content-Aware Fill; GPU-маска/commit, выделение,
трансформы и Undo принадлежат внешним путям. Новых document fields/native dependencies нет.

Независимая адаптация идей Newson et al., [IPOL 2017, sections 3.2–3.4](https://www.ipol.im/pub/art/2017/189/).
Source coverage мотивирована [Self Tuning Texture Optimization](https://onlinelibrary.wiley.com/doi/10.1111/cgf.12565)
и [StyLit](https://dcgi.felk.cvut.cz/home/sykorad/Fiser16-SIG.pdf).
Код эталонных реализаций не скопирован; правила и константы ниже — собственная инженерная
адаптация, не воспроизведение всех стадий/настроек этих работ.

## Действующее поведение

### Признаки фактуры и matching

- Направленные descriptors — 3×3 среднее абсолютных различий соседних известных пикселей
  premultiplied RGB и alpha, включая finite HDR. Missing/invalid/transparent samples не
  создают искусственные feature edges.
- Между уровнями признаки переносятся nearest-neighbor, не пересчитываются из усреднённых цветов.
  Matching складывает premultiplied RGBA error и descriptor distance с весом 4;
  промежуточный voting восстанавливает цвет и descriptors.
- Raw cost допускает early-out. Для сравнения соседних patches ошибка нормализуется по
  фактически доступной weighted support: край не выигрывает из-за меньшего числа samples.
- Target patch готовится один раз на пиксель, маленький sample array переиспользуется на проход.
  Iteration count, random sequence и порядок суммирования от этой оптимизации не меняются.
- После обычных итераций выполняется финальный matching по сошедшемуся изображению.
  Anchor каждого missing pixel — предложение covering patch с минимальной нормализованной ошибкой.
  Progress достигает 100% после реконструкции; cancellation остаётся доступной.

### Мягкое ограничение повторного использования источника

Перед каждым Match посчитать покрытие source pixels всеми назначенными donor footprints,
не только частоту центров/offsets. Rectangle differences и два prefix sums дают
O(image pixels + targets + candidates), запрос donor footprint — O(1).
Карта фиксируется на весь проход и перестраивается между проходами; persistent history
и обновление usage во время сканирования не используются.

- Для patch area A, числа targets T и разрешённых source pixels D allowance:
  `B = 2 A max(1, T/D)`. Обычное перекрытие связного копирования не штрафуется;
  недостаток источника увеличивает allowance.
- При среднем покрытии U штраф `p = max(0, 1 - B/U)`; U=0 даёт 0.
- Сохранить лучший raw error E за весь проход для target. Допустимы лишь проверенные
  кандидаты с raw error ≤ 1.15 E; выбрать по `error + .2 E p`.
  Gate считается от финального raw best, не цепочки уже оштрафованных кандидатов.
- Search идёт по raw-best координатам; clipped early-out estimates не считаются полноценными
  кандидатами. Voting/reconstruction используют исходную нормализованную ошибку без штрафа.
  Exact zero-error match и radius-zero fallback не ухудшаются.

Это эвристика разнообразия, не запрет повторов и не bound ошибки итогового изображения.
Дополнительный payload — 8 байт/prepared pixel (int scratch + float penalty), переиспользуемый
между проходами: до 8 МиБ на brush cap или 32 МиБ на fill cap на полном уровне.
Это размеры массивов, не measured process peak; дополнительных image copies нет.

### Инициализация от границы к центру

На грубейшем уровне заполнять one-pixel rings по 8-connected boundary. Partial patch cost
полностью игнорирует неинициализированные цвета/features. Все matches кольца считаются
до публикации его результата; следующее кольцо использует новый контекст, но donors всегда
берутся из разрешённого исходника. Копируется best-center proposal, не paper weighted reconstruction.

Проверяются propagated offsets и до 512 stratified candidates/pixel, exhaustive ниже cap,
с независимым seeded random. Добавляются две bool arrays и int queue только на этом уровне.
Radius-zero и изолированные invalid/transparent контекстом targets сохраняют nearest-donor fallback.
Исходная soft coverage mask не эродируется. Неизвестные/poisoned pixels не влияют на инициализацию.

### Перенос donor coordinates в более точное разрешение

Upsample coarse coordinates с pixel parity, проверить полный fine donor patch и заполнить
цвет/features исходными donor samples fine level. Не переносить усреднённую coarse working image
или её признаки. Invalid projection использует прежний ближайший допустимый fine donor fallback.
Это direct-center seed + обычный matching/voting, без новых buffers/settings/serialization
или дополнительной contrast postprocessing.

### Избирательная финальная реконструкция

Anchor оценивается по исходным донорам: отношение squared second differences к first-difference
energy в premultiplied RGBA. Smooth confidence между .04 и .5 отклоняет edges/high frequency;
missing permitted neighbor и zero variation оставляют anchor. Это не paper mean-shift clustering.

Другие overlapping proposals тоже должны быть smooth. Их цвет и signed X/Y gradients сравниваются
с anchor при bandwidth относительно source variation, не фиксированной LDR brightness.
Compact squared falloff отклоняет несовместимые предложения. Voting premultiplied RGBA
взвешивается match error и смешивается с anchor по smoothness.
Identical proposals bit-exact; negative/HDR RGB и alpha не clamp и не взвешиваются раздельно.
Radius-zero/insufficient-context сохраняют исходный результат; запись только в target pixels.

Direct-mapped cache donor profiles: 2048 entries, 120 КиБ payload, только на финальный проход.
Exact donor index входит в key; collision заменяет entry, не меняет вычисленный результат.
Нет per-pixel allocations; cache не переживает запуск и не удерживает stale data после Undo/paint.

### Healing lifecycle и Tiled crop

PointerUp владеет отправкой маски; released-button PointerMove не сбрасывает её раньше.
Уведомление другого документа не отменяет работу до owner check. Escape, capture/focus loss
и относящиеся к владельцу context changes отменяют незавершённую работу.
Исправление этих воспроизведённых путей не доказывает причину каждого пользовательского случая.

Если Tiled crop занимает целую ось, разрез — середина самой длинной least-painted band.
Coverage сдвигается тем же целым offset до readback; world coverage/commit coordinates неизменны.
Uniform projection оставляет прежний crop. Это не полностью periodic PatchMatch:
мазок через перемещённый разрез всё ещё может показать переход.

## Ограничения и отклонённые подходы

- Гладкий шум всё ещё может терять контраст/давать серую полосу; неоднозначные donors — стыки patches.
  Улучшение отдельных fixtures не доказывает невидимый ремонт любой картинки.
- Onion initialization улучшает некоторые продолжения линий, но может ухудшать большие
  неоднозначные углы; уникальная утраченная геометрия/яркостный пик не восстанавливаются надёжно.
- Plain weighted reconstruction — сравнение, не production path: сглаживает также фактуру.
  Изменения intermediate voting через relative-error/anchor-color weighting отклонены и удалены:
  контраст существенно не исправили, разрывы усилили. Не представлять их как shipped feature
  и не ослаблять seam/detail tests для их принятия.
- Текстурные descriptors добавляют 28 байт/prepared pixel (3 Vector2 + float), временный gradient
  — 16 байт/base pixel; плюс descriptors pyramid levels. Вместе с color buffers/GC это не peak memory.
  Размеры рабочих областей остаются ограниченными существующими caps.

## Проверки

Через [RUNNING_TESTS.md](../Tests~/RUNNING_TESTS.md), после чтения выбранных cases/support:

- `content-aware-reuse-v2`: brute-force footprint, frozen usage/final-best gate, scarcity,
  coherent overlap, exact/HDR selection, buffer reuse и cancellation.
- `content-aware-cost-v2`: точный scalar reference, border/HDR/alpha, early-outs;
  `content-aware-onion-v2`, `content-aware-pyramid-v2`: masks/rings, parity, fallback, poisoned unknowns.
- `content-aware-reconstruction-v2`: blending/rejection, radius/context, HDR/alpha,
  cached/uncached bit-exact equivalence на изменённом source.
- `content-aware-quality-edge-v2` и соседние quality cases: все качества, seeds 7/123/877,
  determinism, source preservation, finite/range output, categorical donor colors и cancellation.
  Baseline diagnostics отдельны; отсутствие baseline не подменять успешным сравнением.
- `content-aware-fill-v2`, `healing-brush-smoke-v2`, `healing-perimeter-smoke-v2`:
  реальные GPU writes, soft masks, Tiled edges/corners, HDR/alpha, source dimensions, Undo/Redo.
- `healing-noise-seam-diagnostic-v2`: roughness, seam reduction и tonal variation конкретной
  Noise → Drawing fixture, не универсальная quality score. Fixture использует ColorValues;
  смена на нынешний Noise default LinearData меняет вход и нарушает историческое сравнение.

Managed worker timing исключает GPU/UI/commit и первую компиляцию. End-to-end stroke включает
readback, worker, commit и polling. Не смешивать измерения этих уровней и разных стадий алгоритма.
Исторические наблюдения сделаны на Test6.6 / Unity 6000.7.0a6, не гарантируют текущую скорость.
Снимки/метрики новых диагностик — только в `Temp/WhimTex`, не Assets или Context.

## История

[Полные A/B-таблицы, параметры fixtures и этапы алгоритма](https://github.com/DCFApixels/WhimTex/blob/fc4afbf765e3b7734c3fbf0baab77701367b3f02/Context~/HEALING_QUALITY.md)
сохранены в Git. Текущие правила выше не требуют восстановления физических Legacy helpers.
