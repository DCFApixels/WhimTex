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
For brushes use Documentation~/AI/BRUSHES.md and Documentation~/AI/brush.schema.json.
Brush JSON examples: Documentation~/Examples/Brushes/README.md. Format: whimtex.brush.
-->
<p align="center">
  <img src="Documentation~/Images/whimtex-logo.svg" alt="Логотип WhimTex — скат" width="160" height="160">
</p>

<h1 align="center">WhimTex</h1>

<p align="center">
  Создавай текстуры, спрайты и VFX-маски прямо в Unity — без внешнего графического редактора.
</p>

<p align="center">
  <a href="package.json"><img alt="Версия пакета" src="https://img.shields.io/github/package-json/v/DCFApixels/WhimTex?color=3984c6&amp;style=for-the-badge"></a>
  <a href="LICENSE.md"><img alt="Лицензия MIT" src="https://img.shields.io/github/license/DCFApixels/WhimTex?color=3984c6&amp;style=for-the-badge"></a>
  <a href="#installation"><img alt="Unity 6 или новее" src="https://img.shields.io/badge/Unity-6%2B-383838?logo=unity&amp;logoColor=ffffff&amp;style=for-the-badge"></a>
  <a href="https://dcfapixels.github.io/WhimTex/ru/"><img alt="Читать документацию" src="https://img.shields.io/badge/DOCS-READ-3984c6?style=for-the-badge"></a>
  <a href="https://discord.gg/kqmJjExuCf"><img alt="Присоединиться к Discord" src="https://img.shields.io/badge/Discord-JOIN-6473c8?logo=discord&amp;logoColor=ffffff&amp;style=for-the-badge"></a>
</p>

<p align="center">
  <a href="README.md">English</a> · <b>Русский</b> · <a href="README-ZH.md">简体中文</a>
</p>

<p align="center">
  <a href="#installation">Установка</a> ·
  <a href="#why">Почему WhimTex</a> ·
  <a href="#quick-start">Быстрый старт</a> ·
  <a href="Documentation~/ru/shortcuts.md">Горячие клавиши</a> ·
  <a href="CHANGELOG.md">История изменений</a> ·
  <a href="https://github.com/DCFApixels/WhimTex/issues">Сообщить об ошибке</a>
</p>

---

**WhimTex** — бесплатный редактор спрайтов и текстур для Unity с открытым исходным кодом.
Он помогает с небольшими задачами, ради которых обычно приходится открывать графический редактор: подправить
текстуру, нарисовать маску для частиц, сгенерировать шум для VFX, собрать спрайт из нескольких слоёв.

Редактируемая композиция и готовая текстура хранятся в одном ассете. Слои, эффекты и трансформы
остаются доступными, а сам ассет можно сразу назначить материалу. Экспорт нужен только для
отдельного файла изображения.

<p align="center">
  <a href="Documentation~/Images/whimtex-heart.png"><img src="Documentation~/Images/whimtex-heart.png" alt="WhimTex: сердечко из слоёв с градиентом, обводкой, бликом и краевой подсветкой через SDF" width="720"></a>
</p>

<a id="why"></a>
## Почему WhimTex

- **Не выходишь из Unity.** Рисуй и проверяй результат без переноса файлов между редакторами.
- **Работа остаётся редактируемой.** Слои, градиенты, шум, обводки и Shader FX не запекаются безвозвратно — вернуться к ним можно в любой момент.
- **Интеграция с проектом.** Сохранённый ассет работает как **текстура**, вложенный **Output Sprite** — как спрайт. Текстуры, пресеты кистей и HLSL-эффекты можно перетаскивать из Project, а сами пресеты — хранить вместе с проектом.
- **Видно на модели.** **Live Update** показывает результат на объекте в сцене прямо во время рисования.
- **Процедурно, где это уместно.** Шум, градиенты, фигуры, поля расстояний и обводки создаются параметрами, а не кистью.
- **Работа с ИИ.** Подключённый агент добавляет и редактирует слои в открытом документе. Браузерный ИИ может описать слои, кисть или HLSL-эффекты в JSON — достаточно вставить его через `Ctrl+V`. Для генерации есть инструкция, схема и примеры, а ошибки вставки выводятся в консоль.
- **Без runtime-зависимостей.** WhimTex работает только в редакторе; в игру попадают готовые текстуры и спрайты.

## Что можно создавать

- Текстуры для VFX и частиц: мягкие маски, градиенты и процедурный шум.
- Многослойные спрайты и иконки из исходных изображений, рисования, заливок, градиентов и шума.
- Пиксельную графику и бесшовные узоры с помощью кисти, карандаша, выделений и симметрии.
- Обводки, поля расстояний, карты нормалей, Gaussian/Motion Blur и собственные Shader FX.
- Текстуры с упакованными каналами и HDR-композиции с предпросмотром игровых Post FX.

<p align="center">
  <a href="Documentation~/Images/vfx-energy-ring.png"><img src="Documentation~/Images/vfx-energy-ring.png" alt="Энергетическое кольцо для VFX: радиальный градиент и синий шум в слоях" width="250"></a>
  <a href="Documentation~/Images/uv-rubik-cube.png"><img src="Documentation~/Images/uv-rubik-cube.png" alt="Live Update: правки текстуры сразу видны на кубе в Scene view" width="250"></a>
  <a href="Documentation~/Images/brush-settings.png"><img src="Documentation~/Images/brush-settings.png" alt="Окно WhimTex: холст, панель слоёв и настройки кисти" width="250"></a>
</p>

> [!NOTE]
> Требуется **Unity 6 (`6000.0`)** или новее.

<a id="installation"></a>
## Установка

В Package Manager выбери **Install package from git URL** и вставь:

```text
https://github.com/DCFApixels/WhimTex.git
```

[Подробности установки](Documentation~/ru/getting-started.md).

<a id="quick-start"></a>
## Первое изображение

1. Открой **Window → WhimTex** и задай размер холста. Для ещё одного документа нажми **New**.
2. Нажми **+** внизу списка Layers и выбери **Drawing Layer** — появится рисовальный слой. Готовую текстуру можно вместо этого перетащить из Project прямо на превью.
3. Размести изображение через Transform (`T`) или рисуй Brush (`B`) / Pencil (`P`).
4. Нажми `Ctrl+S`. Редактируемый документ и полноразмерная текстура сохранятся в одном `.asset`.
5. Назначь ассет в текстурное поле или используй вложенный **Output Sprite**.

Двойной клик по сохранённому ассету открывает его снова. PNG, TGA, JPEG, EXR, многослойный PSD
и Texture2D доступны через **Export**.

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

**[Слои и Shader FX через браузерный ИИ →](AI_AUTHORING.md)** — получи процедурную композицию
в виде JSON и вставь её в WhimTex. [Как вставить](Documentation~/ru/ai-authoring.md).

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
