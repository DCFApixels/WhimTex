# Финальная сверка тестов WhimTex с Legacy

Сверка завершена на уровне исходников: **360 из 360 исходных файлов** имеют
актуальный независимый разбор замены. Пропущенных исходников, устаревших
source-review хешей и зафиксированных пропусков кода нет.
Это не утверждение о полностью доказанной runtime-эквивалентности.

Из **402 регрессионных сценариев** текущие результаты: **398 PASS, 4 SKIP**.
Все **56 source-guard** проверок прошли; они проверяют исходники/контракты,
а не заменяют GPU/UI/файловую интеграцию. Есть также 11 инфраструктурных
сценариев и 49 диагностических. Эти категории не складываются в «518 зелёных тестов».

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
| Зелёный старый отчёт мог относиться к другим исходникам/аргументам | Добавлен read-only coverage gate: raw hashes, порядок native bundle, typed args, lifecycle cleanup и актуальный Node receipt | 26 чистых тестов gate PASS; compilation, entry-validation, diagnostics и recovery-only не дают regression-equivalence |
| Неразобранный внутренний recovery мог быть потерян dispatcher | recoveryRequired сохраняет uncertainty/lock даже при противоречивом или malformed результате; общий cleanup не переигрывает чужой unresolved lifecycle | Framework: 43 cases / 171 checks; отдельные B/D transport cases проверяют границы повторов и cleanup |

Новые случаи не вызывают Legacy. Единственное прямое старое/новое выполнение —
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
| Документ / направляющие / градиент actual reload; fault workflow | 4 PASS; PSD reader отдельно SKIP | `2026-10-04T15-37-30.186Z-129092.json` |
| ContextTools, actual negative runner, три пары Legacy/new | 8 PASS; три пары equivalent-tested | `2026-10-04T15-35-57.541Z-21160.json` |
| Export / Histogram / Gradient History, включая исходные visual/capture modes | 9 PASS; diagnostic-category PASS не является visual-equivalence | `2026-10-04T15-42-43.469Z-124936.json` |
| Все независимые Node-проверки | 83/83 PASS | `2026-10-04T15-43-27.537Z-126276.json` |
| Независимый CI-профиль документации, повтор перед commit/push | 11/11 PASS | `2026-10-04T16-32-42.000Z-124588.json` |
| Все 28 сценариев с исправленным ReviewedOracle | 15 PASS, 13 SKIP: 10 диагностик и 3 input-blocked регрессии | `2026-10-04T15-46-16.669Z-126508.json` |

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
| `patch-quilting-equivalence-benchmark-v2` — SKIP | Нужен оригинальный `live_4.bin` с проверенными path/SHA/provenance. Тот же input нужен четырём историческим capture/timing помощникам. Синтетические Main/Managed сравнения прошли, но не заменяют этот input |
| `seamless-optimization-compare-v2`, `seamless-optimization-audit-v2` — SKIP | Нужны 252 float snapshots от pre-change renderer. Не генерировать их текущим алгоритмом ради зелёного результата |
| `psd-reader-roundtrip-v2` — SKIP | Независимый декодер не установлен. Writer и fixture прошли, но не заменяют reader. Передать существующий модуль через WHIMTEX_PSD_READER либо отдельно разрешить установку зависимости |
| ScreenedSeamless Preview | Нет исторического `screened-poisson-lab-2026-09-28/00_original.png`; ручной visual producer перенесён, но его input-ветка не выполнена |
| Mirror / SeamlessControls Preview | Input найден и аутентифицирован, render и сохранение artifacts выполнены. Ручная визуальная оценка не сертифицирована; исторического замороженного digest input нет. Наличие PNG/BIN не доказывает визуальную эквивалентность |
| ContextTools | При неизменённых input SHA и product fingerprint сначала реальный failure: temporary button вышел на 52 px за окно 640×420; затем PASS, 701 checks и весь tail. Точная timing/layout причина не установлена. Условия, ожидание 100 ms и assertions не ослаблены; предыдущая ошибка сохранена |
| Диагностические/ручные/performance режимы | Из 49: 8 имеют current diagnostic-only результат, 11 current SKIP, 30 не имеют текущего runtime proof. Их исходные режимы разобраны/перенесены; source review и отсутствие assertions не заменяют фактический запуск или ручную оценку |

Таким образом, пропущенных исходных проверок в разборе не осталось, но полной
проверенной эквивалентности всему Legacy пока нет. Для удаления архива нужно
разобрать перечисленные ограничения и отдельно согласовать завершающий шаг.
`archiveRemovalAllowed` остаётся `false`.

## Артефакты и безопасность

- [Полный машинный инвентарь](MigrationFull.results.json): 360 original source rows,
  518 scenario rows, текущие verdicts, hashes audit inputs и использованных raw reports.
- [CoverageAudit/](CoverageAudit/): подробные условия переноса и история ограничений по каждому оригиналу.
- [RUNNING_TESTS.md](RUNNING_TESTS.md): общий API, `--id` / `--ids` / группы / ограниченные профили и recovery protocol.
- `migration-final-safety.json` в project Temp: Editor ready/stopped,
  без компиляции/reload; Build Settings совпадают с исходными; runner.lock отсутствует.
- Тестовые native scripts/assets удалены только из собственных GUID-папок.
  Незарегистрированный документ при восстановлении export оставлен живым,
  его ссылка и сериализованное состояние подтверждены неизменными.
- Несохранённая сцена закрыта без сохранения с прямого разрешения пользователя;
  открыта сохранённая SampleScene. Пользовательские файлы не удалены.
- Замороженный manifest SHA-256:
  `c70816ea4220d9ebe08d4d2d087d7e5297bd943405bb3297e24f4b69756a6194`.
- Неизменившийся product fingerprint:
  `a3da14871f6e0bf5c49a6f077b1026720e0fb2d3c28a49213dd715f3dd2b7738`.

Raw отчёты, включая прежние красные результаты, сохранены в project Temp.
Машинный инвентарь — снимок, а не вечная гарантия: после изменения исходников,
аргументов, зависимостей или evidence нужно повторить read-only gate.
