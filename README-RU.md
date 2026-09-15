<!--
AI assistants generating WhimTex textures, procedural layers, images from links or HLSL effects:
start at AI_AUTHORING.md, then Documentation~/AI/README.md for the clipboard JSON
and shader contract. Schema: Documentation~/AI/layers.schema.json.
Clipboard example folder: Documentation~/Examples/Clipboard/.
Read Documentation~/Examples/Clipboard/README.md, then the matching .json file.
For a Drawing image URL + Shader Processor, use stone-wall-retro.json in that folder.
The contract ends with common mistakes followed by the full JSON/HLSL specification.
Return clipboard JSON for Ctrl+V. A Drawing layer with `url` may fetch a PNG or JPEG from a direct http(s) link.
Do not use Unity serialization or live API requests.
These relative paths refer to the same branch as this README.
-->
<p align="center">
  <img src="Documentation~/Images/whimtex-logo.svg" alt="Логотип WhimTex — скат" width="160" height="160">
</p>

<h1 align="center">WhimTex</h1>

<p align="center">
  Редактор спрайтов и текстур для Unity — быстрые идеи, доработки и VFX.
</p>

<p align="center">
  <a href="package.json"><img alt="Версия пакета" src="https://img.shields.io/github/package-json/v/DCFApixels/WhimTex?color=3984c6&amp;style=for-the-badge"></a>
  <a href="LICENSE.md"><img alt="Лицензия MIT" src="https://img.shields.io/github/license/DCFApixels/WhimTex?color=3984c6&amp;style=for-the-badge"></a>
  <a href="#installation"><img alt="Unity 6 или новее" src="https://img.shields.io/badge/Unity-6%2B-383838?logo=unity&amp;logoColor=ffffff&amp;style=for-the-badge"></a>
  <a href="https://dcfapixels.github.io/WhimTex/ru/"><img alt="Читать документацию" src="https://img.shields.io/badge/DOCS-READ-3984c6?style=for-the-badge"></a>
  <a href="https://discord.gg/kqmJjExuCf"><img alt="Присоединиться к Discord" src="https://img.shields.io/badge/Discord-JOIN-6473c8?logo=discord&amp;logoColor=ffffff&amp;style=for-the-badge"></a>
</p>

<p align="center">
  <a href="README.md">English</a> · <b>Русский</b> · <a href="https://dcfapixels.github.io/WhimTex/zh/">简体中文</a>
</p>

<p align="center">
  <a href="#installation">Установка</a> ·
  <a href="#quick-start">Быстрый старт</a> ·
  <a href="#shortcuts">Горячие клавиши</a> ·
  <a href="CHANGELOG.md">История изменений</a> ·
  <a href="https://github.com/DCFApixels/WhimTex/issues">Сообщить об ошибке</a>
</p>

---

**WhimTex** — бесплатный редактор спрайтов и текстур для Unity с открытым исходным кодом.
Он помогает с небольшими задачами по ходу разработки игры: подправить текстуру, нарисовать маску
для частиц, сгенерировать шум для VFX или собрать спрайт из нескольких слоёв — прямо в Unity.

Редактируемая композиция и готовая текстура сохраняются в одном ассете. Назначь его материалу
и включи **Live Update**, чтобы видеть изменения в сцене. Отдельное изображение экспортируй,
только когда оно нужно.

<p align="center">
  <a href="Documentation~/Images/whimtex-heart.png"><img src="Documentation~/Images/whimtex-heart.png" alt="WhimTex: сердечко из слоёв с градиентом, обводкой, бликом и краевой подсветкой через SDF" width="720"></a>
</p>

> [!NOTE]
> Создавай и редактируй изображения в **Unity Editor**, затем используй сохранённые текстуры и спрайты в игре.

## Что можно создавать

- Текстуры для VFX и частиц: мягкие маски, градиенты, процедурный шум и упакованные каналы.
- Многослойные спрайты и иконки из исходных изображений, рисования, заливок, градиентов и шума.
- Пиксельную графику и бесшовные паттерны с кистью/карандашом, выделениями, симметрией и тайловым рисованием.
- Обводки, поля расстояний, карты нормалей, Gaussian/Motion Blur и собственные Shader FX.
- Упакованные каналы текстур и HDR-композиции, с необязательным просмотром игровых Post FX.

<a id="installation"></a>
## Установка

**Unity 6 (`6000.0`) или новее.** В Package Manager выбери **Install package from git URL**:

```text
https://github.com/DCFApixels/WhimTex.git
```

[Подробности установки](Documentation~/ru/getting-started.md).

<a id="quick-start"></a>
## Первое изображение

1. Открой **Window → WhimTex**, нажми **New** и задай размер холста.
2. Перетащи текстуру из Project на превью или нажми **Лист +** внизу Layers для создания Drawing-слоя.
3. Размести изображение через Transform (`T`) или рисуй Brush (`B`) / Pencil (`P`).
4. Нажми `Ctrl+S`. Редактируемый документ и полноразмерная текстура сохранятся в одном `.asset`.
5. Назначь ассет в текстурное поле или используй вложенный **Output Sprite**.

Двойной клик по сохранённому ассету открывает его снова. PNG, TGA, JPEG, EXR, многослойный PSD
и Texture2D экспортируются, когда нужен отдельный файл. Сохрани изменения, чтобы обновить изображение в игре.

<a id="workspace"></a>
<a id="layers"></a>
<a id="transform"></a>
<a id="painting"></a>
<a id="symmetry"></a>
<a id="effects"></a>
<a id="preview"></a>
<a id="saving"></a>
<a id="export"></a>
<a id="shortcuts"></a>
<a id="automation"></a>
## Документация

**[Слои и Shader FX через браузерный ИИ →](AI_AUTHORING.md)** — получите процедурную композицию
в виде JSON и вставьте её в WhimTex. [Как вставить](Documentation~/ru/ai-authoring.md).

**[Открыть документацию →](https://dcfapixels.github.io/WhimTex/ru/)** ·
[English](https://dcfapixels.github.io/WhimTex/en/) ·
[简体中文](https://dcfapixels.github.io/WhimTex/zh/)

Руководство идёт по рабочему процессу — от первого холста к рисованию, эффектам и экспорту:

| Что дальше | Руководство |
| :--- | :--- |
| Освоить окно и расположить исходники | [Начало работы](Documentation~/ru/getting-started.md) · [Слои](Documentation~/ru/layers.md) · [Трансформ](Documentation~/ru/transform.md) |
| Рисовать, заливать и выделять | [Рисование](Documentation~/ru/painting.md) · [Выделения](Documentation~/ru/selection.md) · [Бесшовные паттерны](Documentation~/ru/symmetry.md) |
| Создать процедурную текстуру | [Noise](Documentation~/ru/noise.md) · [Слои эффектов](Documentation~/ru/effects.md) · [Normal Map](Documentation~/ru/normal-map.md) |
| Управлять композицией | [Наложение и обтравка](Documentation~/ru/blending.md) · [Shader FX](Documentation~/ru/shader-fx.md) · [HDR и каналы](Documentation~/ru/color.md) |
| Проверить и использовать результат | [Превью](Documentation~/ru/preview.md) · [Post FX](Documentation~/ru/post-fx.md) · [Сохранение и экспорт](Documentation~/ru/saving.md) |
| Найти управление или решить проблему | [Горячие клавиши](Documentation~/ru/shortcuts.md) · [Неожиданный результат](Documentation~/ru/troubleshooting.md) |
| Автоматизировать работу | [Работа с агентом](Documentation~/ru/automation.md) |

## Благодарности

Спасибо авторам и разработчикам библиотек, на которых основана часть возможностей WhimTex:

- **[Параметры последовательности Sobol](https://web.maths.unsw.edu.au/~fkuo/sobol/)** — Frances Kuo и Stephen Joe;
  равномерный разброс кисти. [Приложенная лицензия](ThirdPartyNotices.md#sobol-direction-numbers).
- **[FastNoiseLite](https://github.com/Auburn/FastNoiseLite)** — Jordan Peck и участники проекта;
  HLSL-реализация используется для генерации слоя Noise. [Приложенная лицензия MIT](ThirdPartyNotices.md#fastnoiselite).
- **[Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json)** — James Newton-King и участники проекта;
  JSON-сериализация документов композитора и API для агентов, подключённая через пакет Unity.
  [Приложенные лицензии сторонних компонентов](Documentation~/Licenses/Newtonsoft-ThirdPartyNotices.md).
- **[Unity Burst](https://docs.unity3d.com/Packages/com.unity.burst@1.8/manual/index.html)** и
  **[Unity Collections](https://docs.unity3d.com/Packages/com.unity.collections@2.5/manual/index.html)** —
  оптимизированные вычисления на CPU и нативные коллекции.

- **[Just the Docs](https://github.com/just-the-docs/just-the-docs)** — тема сайта документации.
  [Приложенная лицензия MIT](Documentation~/Licenses/JustTheDocs-LICENSE.txt).

Версии исходников и сведения о лицензиях пакетов собраны в [Third-party notices](ThirdPartyNotices.md).
Сторонние компоненты сохраняют собственные лицензии.

<a id="community"></a>
## Сообщество и лицензия

Есть вопрос или идея? Заходи в **[Discord · RU / EN](https://discord.gg/kqmJjExuCf)**.
Для ошибок создай [GitHub issue](https://github.com/DCFApixels/WhimTex/issues)
с версией Unity и шагами воспроизведения.

Распространяется под **[лицензией MIT](LICENSE.md)**.
