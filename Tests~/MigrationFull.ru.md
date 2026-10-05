# Финальная сверка тестов WhimTex с Legacy

Актуальное состояние после отвязки инфраструктуры: [ArchiveRetirement.ru.md](ArchiveRetirement.ru.md).
Новые тесты и аудит работают без физической папки Legacy; 24 старых запуска сняты с регистрации.
520 независимых сценариев сохранены, продукт и архив не изменены. Commit/push и удаление не выполнялись.

Приведённые ниже **402 PASS — исторический итог до изменения runner**, не новый полный прогон.
Неизменённый машинный снимок сохранён в `CoverageAudit/pre-retirement.results.json`; текущий —
`ArchiveRetirement.results.json`. Свежие проверки и отдельное решение об удалении приведены
в отчёте отвязки. Старые receipts не переподписывались.

## Исторический отчёт до retirement

Обновление 5 октября 2026 года: независимый PSD roundtrip закрыт actual PASS.
По прямому запросу пользователя `ag-psd 31.0.2` установлен только в project
`Temp/WhimTex`, с отключёнными npm lifecycle scripts, без изменения зависимостей
WhimTex/Unity. Native writer выполнил70 checks, независимый decoder —72;
условия и допуски не менялись. Gate подтверждает текущие source/input/decoder receipts.
Все74 проверки инвентаризации повторно PASS; физический Legacy пока не удалять
из-за оставшейся зависимости инфраструктуры запуска от архива.
[PSD runtime receipt](CoverageAudit/psd-reader-final-runtime-review.json).
Предыдущая [read-only сверка](CoverageAudit/final-recheck-2026-10-05.json)
с прежним prerequisite SKIP сохранена как история, а не текущий результат.

Сверка завершена на уровне исходников: **360 из 360 исходных файлов** имеют
актуальный независимый разбор замены. Пропущенных исходников, устаревших
source-review хешей и зафиксированных пропусков кода нет.
Это не утверждение о полностью доказанной runtime-эквивалентности.

Из **402 регрессионных сценариев** текущие результаты: **402 PASS, 0 SKIP**.
В этом обновлении заново выполнялся только PSD; остальные raw результаты
проверены на актуальность, а не представлены как новый полный native прогон.
Все **56 source-guard** проверок прошли; они проверяют исходники/контракты,
а не заменяют GPU/UI/файловую интеграцию. Есть также 11 инфраструктурных
сценариев и 50 диагностических. Эти категории не складываются в «519 зелёных тестов».

Из 50 диагностик: **33 имеют актуальный diagnostic-only результат, 15 — SKIP,
2 отдельных helper-записи остаются pending**. Эти две фазы градиента уже проверены
в составе настоящего reload workflow: новый phase-aware gate подтверждает обе
по raw CLI evidence, одному GUID и финальной очистке. Это отдельное покрытие фаз,
не два самостоятельных PASS; запускать их по отдельности некорректно.

На момент завершения сверки изменения были локальными в `dev`, без commit/push. Исходники продукта `src/`
не изменились за этот этап. **Legacy не удалён**: 466 файлов и замороженный
манифест проверены побайтово.

## Проблема → решение → проверка

| Проблема / причина | Что сделано | Как подтверждено |
| --- | --- | --- |
| Одного соответствия файлов недостаточно: могли пропасть assertions, варианты входа или cleanup | Для каждого оригинала разобраны проверки, входы, допуски, публичные режимы и жизненный цикл; дописаны пропущенные ветки | 360 актуальных source-review записей; исходные и replacement/support SHA в `CoverageAudit/` |
| Часть проверок была заменена более слабой публичной альтернативой | Восстановлены разрешённые test-only проверки внутренних API пипетки, dock/tab и profiler; ReadScreenPixel используется только на тестовых окнах | Реальные Unity-прогоны; доступность API не объявляется доказательством фактического захвата рабочего стола, условная docking-ветка — доказательством всех dock layouts |
| Отказ Save раньше проверялся только чистым path guard | Вызван настоящий Save с двумя явно разрешёнными уникальными запрещёнными путями | Проверены отказ и отсутствие файла/meta; очистка могла затронуть только результат данного теста |
| Ephemeral assembly не воспроизводит настоящий domain reload и import postprocessor | Выделены последовательные native workflows с GUID-владением, событием beforeAssemblyReload и проверкой исчезновения старого callback | Документ, направляющие, градиент и fault/retry/deferred workflows PASS; установленный native helper удалён с повторным reload |
| Player-ветки нельзя заменить Editor smoke-тестом | Выполнены отдельно разрешённые сборка и запуск Player; проверены сохранённые зелёные pixels против несохранённого красного состояния | Полный Player workflow PASS, 849 checks; native build, runtime GPU pixels, packed textures, отсутствие WhimTex assembly и cleanup |
| Старый manual setup мог оставить окна и скрыть ошибку очистки | Восстановлены исходные Export/Gradient/Histogram режимы, строгие GUID/ID guards и явный recoveryRequired | Финальные девять сценариев PASS; ошибка тела/cleanup не превращается в успех; отдельное восстановление старого export-журнала не засчитывается как регрессия |
| Unity теряла вложенные DTO-массивы в интерпретируемой Pipeline assembly | Чтение регистрации/Player-данных и сохранение двух Preview artifacts переведены на публичные Newtonsoft token API | Preview raw JSON сохраняет 4 PNG Mirror и 8 PNG + 8 float BIN Seamless; generated output не объявляется историческим эталоном |
| ContextTools проверял кнопку до завершения обновления панели | После исходных 100 ms добавлен ограниченный барьер готовности размера панели и один turn её scheduler; исходное условие fit не изменено | Финальная версия 3/3 PASS по 713 checks: весь старый tail и 12 дополнительных fault checks. Это изменение момента проверки, не доказательство строгой временной эквивалентности старым 100 ms |
| Диагностические samples и PNG/CSV терялись перед cleanup | Публичная сериализация сохраняет полные массивы и реальные артефакты до удаления GUID-файлов; Compare/Audit и парные Poisson-тайминги возвращают фактический текст | Native B/D24, C43 и финальные C13/C5; CSV содержит 12 291 строку. Ошибки тела/cleanup сохраняются, ручные producers не превращены в PASS |
| Два Mirror Preview PNG были однотонными из-за вызова без явного прохода шейдера | Новый тестовый помощник использует pass 0, как код продукта | Все затронутые регрессии повторно прошли; четыре финальных PNG сохранены и осмотрены. Старый helper и продукт не изменены |
| Для трёх регрессий не был зарегистрирован существующий исторический input | Найдены исходный live_4 и 252 pre-change snapshots; зафиксированы точные пути, текущие SHA и происхождение | Quilting benchmark PASS; Compare252 PASS с максимальной разницей 0. Audit тоже PASS, но его условия проверяют finiteness/state и не заменяют допуск Compare. Новые эталоны не генерировались |
| Зелёный старый отчёт мог относиться к другим исходникам/аргументам | Добавлен read-only coverage gate: raw hashes, порядок native bundle, typed args, lifecycle cleanup и актуальный Node receipt; парные helper-фазы проверяются по полному raw workflow | 69 тестов gate и 5 oracle-retention source guards PASS; compilation, entry-validation, diagnostics и recovery-only не дают regression-equivalence |
| Неразобранный внутренний recovery мог быть потерян dispatcher | recoveryRequired сохраняет uncertainty/lock даже при противоречивом или malformed результате; общий cleanup не переигрывает чужой unresolved lifecycle | Framework: 43 cases / 171 checks; отдельные B/D transport cases проверяют границы повторов и cleanup |
| UV capture выполнялся, но PNG терялся при сериализации | Артефакт передаётся публичными JSON token API с явной привязкой к нужной сборке; геометрия и capture не изменены | Actual native SKIP3 + cleanup PASS1, PNG640×650 сохранён и осмотрен. Тёмный Canvas не объявляется визуальным успехом |
| Новый NativeSetup пипетки завершал manual fixture слишком рано и не давал повторяемого getter | Добавлен отдельный opt-in режим: окно15s, публичный getter по GUID, реальный PNG, joined cancellation/cleanup | 4/4 текущих native сценария PASS;22 fresh Poll assemblies, window alive15174ms, PNG SHA проверен после cleanup. Ранняя отмена отдельно остановила работу и освободила контекст |
| Release Status читал старый глобальный SessionState, который новый Stress не обновляет | Устаревший assertion-free stored-status контракт заменён явным SKIP/redirect к выбранному raw Stress receipt | Повторены все14 сценариев:12 регрессий PASS, Status/Visuals SKIP. Размеры1024/2048, sparse enum IDs, finiteness/cache/tolerance и timings не ослаблены |
| PSD writer PASS не подтверждал чтение файла независимым декодером | После прямого разрешения установлен изолированный тестовый decoder; текущий native writer создал новый GUID PSD, затем выполнены все исходные reader assertions | Writer70 checks + decoder72 checks PASS; выбранный receipt, native input SHA и decoder bytes проверены gate. Прежний SKIP сохранён в gapHistory |

Новые продуктовые replacement bodies независимы от Legacy. Инфраструктура запуска
пока требует физический архив — это отдельно разобрано в решении ниже.
Прямое старое/новое сравнение продуктовых результатов —
три явно выбранные пилотные пары; они выполнены в одном текущем отчёте и все
получили `equivalent-tested`. Остальная сверка состоит из независимого
разбора исходников и актуальных запусков новых проверок, а не автоматического
доказательства семантической идентичности.

## Что действительно запускалось

Повторные запуски не суммируются как новые тесты. Для текущей сводки используются
последние результаты с подтверждёнными исходниками/аргументами.

| Финальная проверка | Результат | Отчёт в `Temp/WhimTex/test-runs/` |
| --- | --- | --- |
| Player: install → native compile → prepare → build → runtime → inspect → cleanup | PASS, 849 checks | `2026-10-04T15-15-44.149Z-130408.json` |
| Документ / направляющие / градиент actual reload; fault workflow | 4 PASS; прежний PSD prerequisite SKIP закрыт отдельным прогоном ниже | `2026-10-04T15-37-30.186Z-129092.json` |
| Независимый PSD roundtrip | PASS: native writer70 checks, decoder72 checks; wrapper78 включает decoder checks и6 orchestration assertions | `2026-10-05T03-05-44.229Z-120160.json` |
| Три пары Legacy/new, финальный отдельный прогон | 6/6 PASS; все три пары equivalent-tested по актуальному общему отчёту | `2026-10-04T18-02-02.749Z-120056.json` |
| Export / Histogram / Gradient History, включая исходные visual/capture modes | 9 PASS; diagnostic-category PASS не является visual-equivalence | `2026-10-04T15-42-43.469Z-124936.json` |
| ContextTools, финальная версия с очисткой callbacks при ошибках | 3/3 PASS, по 713 checks, cleanup PASS | `2026-10-04T17-27-24.339Z-54316.json`, `17-27-37.685Z-54316.json`, `17-27-51.017Z-54316.json` |
| Actual negative runner, отдельный актуальный receipt | PASS; ожидаемые отрицательные ответы проверены, а не скрыты | `2026-10-04T17-39-53.869Z-122348.json` |
| Histogram audit / SaveTail / Healing | 4 актуальных PASS из первоначального прогона восьми; четыре Performance cases обновлены в B/D24 | `2026-10-04T17-03-04.282Z-119580.json` |
| Сохранение, Burst, performance и rounded capture | 23 PASS, 1 намеренный diagnostic SKIP с сохранённым CSV | `2026-10-04T17-15-05.924Z-131656.json` |
| C43 после регистрации оригинальных inputs | 30 PASS, 13 diagnostic SKIP; затронутые последующими repairs файлы повторены ниже | `2026-10-04T17-22-11.610Z-124400.json` |
| Финальные Mirror / quilting, включая сохранение Capture PNG | 9 PASS, 4 diagnostic SKIP с реальными PNG | `2026-10-04T17-32-56.564Z-57556.json` |
| Финальные SeamlessOptimization с сохранением дельт и таймингов | 3 PASS, 2 diagnostic SKIP; Compare252: max delta 0 | `2026-10-04T17-40-22.492Z-82120.json` |
| Все независимые Node-проверки с уже установленным движком HLSL-грамматики, повторный итоговый прогон | 83/83 PASS; зависимости не устанавливались | `2026-10-04T18-30-57.824Z-133536.json` |
| Независимый CI-профиль документации | 11/11 PASS | `2026-10-04T17-35-54.165Z-131608.json` |
| UV diagnostic после исправления доставки PNG | SKIP3, actual PNG640×650, cleanup PASS1 | `2026-10-04T18-21-31.630Z-44312.json` |
| Seamless Release после удаления устаревшего Status |12 PASS,2 diagnostic SKIP; все14 выполнены | `2026-10-04T18-33-53.471Z-132724.json` |
| Пипетка: Run, NativePreview, NativeRender и manual inspection15s |4/4 PASS; diagnostic modes не дают visual-equivalence | `2026-10-04T18-42-29.565Z-131380.json` |
| Ранняя отмена manual inspection | cancel acknowledged, cleanup PASS2, затем missing/windowAlive=false | `eyedropper-native-inspection-cancellation.json` |

Актуальные обычные Unity-прогоны A/B/C/D, profiler и вспомогательные проверки
привязаны к конкретным текущим input bundles в машинном инвентаре.
Document reload прошёл ветку `surviving-window`, `partial:false`:
проверены восстановленное окно, несохранённое содержимое и сохранение после reload.
Это не carrier-only fallback.

CI документации выше — проверка Node-контрактов, не отдельное подтверждение
production build/deploy GitHub Pages.

## Что ещё не подтверждено

| Остаток | Причина → следующее действие |
| --- | --- |
| Историческая непрерывность inputs | Происхождение старых inputs сверено с соседними README и замороженной историей; все текущие байты закреплены SHA. Замороженных пофайловых digest от даты создания нет: текущий hash не доказывает неизменность файла с той даты |
| Ручные visual producers | Mirror / SeamlessControls / Screened / Release matrix / quilting и Healing осмотрены технически, PNG и float/CSV данные сохранены. Это не сертификация художественного качества или исторической визуальной идентичности; исходные SKIP остаются SKIP |
| Native sampling/изображение пипетки |15-секундное окно, повторяемый getter и actual PNG восстановлены и проверены. Сам sampling всё ещё заканчивается до capture; причина не установлена. У исходного Setup/Inspect не было проверки15s sampling или image golden, поэтому диагностический PASS не объявляется новым поведенческим oracle |
| Player вне Windows | Настоящий Windows workflow уже PASS; переносимые ветки запуска на macOS/Linux не выполнялись на этом хосте. Cross-platform runtime-эквивалентность не заявлена |
| ContextTools timing | Исторический failure с overflow 52 px сохранён. Поздний trace показывает промежуточный layout до scroll, но не доказывает причину именно того failure. Добавленный readiness barrier устраняет зависимость проверки от одного раннего sampling point; 3/3 финальных PASS не являются гарантией отсутствия любых будущих flakes |
| Begin/End градиента в каталоге | Обе фазы теперь verified по текущему raw Begin → Trigger → ReloadPoll → End → Cleanup с одним GUID. Standalone строки не превращены в PASS: обычный runner не оркестрирует их парой. При retirement убрать дублирующие самостоятельные записи, сохранив workflow и phase mapping |
| Historical Capture | `seamless-optimization-capture-v2` остаётся SKIP: текущий renderer не может заново создать pre-change эталон.252 существующих snapshots и их Compare/Audit сохранены и проверены. Явный CaptureHistorical требует отдельно reviewed исторического renderer; dormant генератор не считается выполненным или новым baseline |
| Диагностические/performance режимы | Из прежних 30 pending отдельно выполнены 28, ещё две фазы покрыты paired workflow. Timing observations не получили новых performance acceptance thresholds. Нулевые process-memory counters в этой Unity явно помечены как недоступные; managed-данные относятся ко всему Editor |

Таким образом, пропущенных исходных проверок в разборе не осталось, но полной
проверенной эквивалентности всему Legacy пока нет. Технический осмотр PNG и
непроверенные другие платформы не выдаются за автоматические условия старых тестов.
Практическое решение об архиве принято отдельно ниже; gate намеренно не принимает
его по числу PASS и всегда оставляет `archiveRemovalAllowed:false`.

## Историческое решение по Legacy

Описанная ниже техническая зависимость теперь снята; актуальное решение — в [отчёте отвязки](ArchiveRetirement.ru.md#решение-об-удалении). Этот раздел сохранён как история предыдущего этапа.

**Сейчас физически удалять папку нельзя.** PSD-препятствие закрыто и все402
регрессии имеют текущий PASS. Остаётся техническая зависимость инфраструктуры:
даже new-only runner проверяет физический Legacy до выбора сценариев и перед каждым
случаем. Каталог содержит24 legacy-сценария; framework protocol/pilot tests,
`LegacyIntegrity.mjs`, `check-migration.mjs` и coverage gate тоже читают архив.
Простое удаление сломает запуск новых тестов.

Следующий шаг — отдельный retirement change, без ослабления assertions:

- Зафиксировать recovery descriptor: commit `ca8603c0961ce36064280f952259f8a6142d46cc`,
  archive path и frozen manifest SHA. Этот commit достижим через `dev`/`origin/dev`
  и содержит все466 архивных entries; каждый blob сверён с manifest. Старый
  `baselineCommit` сам по себе не покрывает весь окончательный архив.
- Удалить24 legacy-сценария и legacy-only profiles из активного каталога; framework
  protocol checks перевести на собственные fixtures, сохранив negative assertions.
  Pilot catalog/mappings/receipts оставить исторической provenance.
- Обычный new-only runner отделить от физического архива; archive-integrity/gate
  должны читать проверенный pinned Git snapshot. Отсутствие commit/байтов — ошибка,
  а не PASS. Обновить audit tools, AGENTS и ссылки документации.
- Сохранить `legacy-manifest.json`, `Fixtures/Compatibility0125`, `Fixtures/ShaderFX0125`,
  зарегистрированные оригинальные inputs и новую dirty/untracked provenance.
  Recovery commit не содержит все результаты текущего этапа — сначала закрепить их отдельно.
- Изменение runner/legacy helpers изменит fingerprints: прежние receipts сохранить
  как pre-retirement evidence, не переподписывать. Перепроверить новый runner без
  физического Legacy, затем отдельно удалять рабочую копию архива.

Это не рекомендация оставлять старые тесты навсегда: перенос исходных ответственностей
готов, но удаление нужно оформить как проверяемый retirement, а не удаление папки.
В текущем этапе Legacy не изменялся; commit/push не выполнялись.

## Артефакты и безопасность

- [Полный машинный инвентарь](MigrationFull.results.json): 360 original source rows,
  519 scenario rows, текущие verdicts, отдельное raw workflow phase coverage,
  hashes audit inputs и использованных raw reports.
- [CoverageAudit/](CoverageAudit/): подробные условия переноса и история ограничений по каждому оригиналу.
- [PSD runtime review](CoverageAudit/psd-reader-final-runtime-review.json): raw report, fixture/input/decoder SHA, pinned npm versions/integrity и сохранённые licenses. Декодер [ag-psd](https://github.com/Agamnentzar/ag-psd) — только временная тестовая зависимость, не часть поставки WhimTex; notices лежат рядом с установкой в `Temp/WhimTex/psd-reader-20261005-9c771397/ThirdPartyNotices.md`.
- [Технический осмотр артефактов](CoverageAudit/diagnostic-artifacts-review.json): 30 сохранённых artifacts семи сценариев, hashes, размеры и finiteness десяти float payloads. Это не visual-equivalence receipt.
- [RUNNING_TESTS.md](RUNNING_TESTS.md): общий API, `--id` / `--ids` / группы / ограниченные профили и recovery protocol.
- `Temp/WhimTex/test-runs/migration-final-safety.json` в проекте: Editor ready/stopped,
  без компиляции/reload; Build Settings совпадают с исходными; runner.lock отсутствует.
- Тестовые native scripts/assets удалены только из собственных GUID-папок.
  Незарегистрированный документ при восстановлении export оставлен живым,
  его ссылка и сериализованное состояние подтверждены неизменными.
- Неуспешная компиляция нового manual helper сохранена как исторический FAIL:
  CS0433 из-за двух JSON-сборок. Все три native attempts завершились до исполнения
  entry (`executeMs=0`); Editor idle и отсутствующий runner PID проверены.
  Lock сохранён отдельным `.compile-failure.lock` с recovery proof, не скрыт;
  JSON binding исправлен через публичные API точной сборки и все четыре сценария повторены.
- Несохранённая сцена закрыта без сохранения с прямого разрешения пользователя;
  открыта сохранённая SampleScene. Пользовательские файлы не удалены.
- Замороженный manifest SHA-256:
  `c70816ea4220d9ebe08d4d2d087d7e5297bd943405bb3297e24f4b69756a6194`.
- Неизменившийся product fingerprint:
  `a3da14871f6e0bf5c49a6f077b1026720e0fb2d3c28a49213dd715f3dd2b7738`.

Raw отчёты, включая прежние красные результаты, сохранены в project Temp.
Машинный инвентарь — снимок, а не вечная гарантия: после изменения исходников,
аргументов, зависимостей или evidence нужно повторить read-only gate.
