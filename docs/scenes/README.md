# Сцены — как создать свой цикл

> Портативная система: каждая **папка = сцена**. Внутри папки — кадры `*.png|jpg|webp` + `scene.json`. Никакого `fps` в имени файла.

## Структура

```
.\cycles\                 ← рядом с OsageLagtrain.exe (portable)
  _template\             ← шаблон, копируй его
    scene.json
    0001.png             ← placeholder 1px #b2b2b2, замени своими кадрами
  jump_hand\             ← твоя сцена
    scene.json
    0001.png
    0002.png
    0010.png             ← сортируется натурально: 2 → 10, не 10 → 2
  my_loop\
    scene.json
    ...
```

**Fallback** при установке в `C:\Program Files\` (нет прав на запись): `%APPDATA%\OsageLagtrain\cycles\`.

## Натуральная сортировка

Кадры сортируются через `StrCmpLogicalW` (Windows natural sort):
`0001.png, 0002.png, 0010.png` — правильно. Лексическая сортировка (`10` перед `2`) не используется.

> Именуй с ведущими нулями `0001..9999` — тогда порядок совпадает везде.

## scene.json — спецификация

Схема: [`docs/scene.json.schema.json`](../scene.json.schema.json) (draft 2020-12).

| Поле | Тип | Ограничения | Default | Обязат. |
|------|-----|-------------|---------|---------|
| `id` | string | `^[a-z0-9_-]{1,32}$` | — | **да** |
| `title` | string | 1..128 | `id` | нет |
| `fps` | integer | 1..30 | `12` | нет |
| `mode` | string \| object | `"once"`\|`"loop"`\|`"pingpong"`\|`{"count":1..100}` | `"once"` | нет |
| `loopCount` | integer | 1..100 (alias для `{"count":}`) | — | нет |
| `holdLastMs` | integer | 0..5000 | `0` | нет |
| `postEventDelayMs` | integer | 0..5000 | глобальный `500` | нет |
| `idleColor` | string | `^#[0-9a-fA-F]{6}$` | `"#b2b2b2"` | нет |

- `fps` **никогда** не хранится в имени файла — только в `scene.json`.
- Невалидный JSON **не игнорируется тихо** — лоадер бросает `SchemaValidationException` с `path:line` и UI показывает красный бейдж.

### Минимальный пример

```json
{"id":"jump_hand","fps":12,"mode":"once","holdLastMs":800}
```

### Ещё примеры

**Loop (бесконечно):**
```json
{"id":"loop_run","title":"Loop Run","fps":12,"mode":"loop","holdLastMs":0}
```

**Ping-pong (туда-обратно, off-by-default):**
```json
{"id":"ping_pong","title":"Ping Pong Demo","fps":8,"mode":"pingpong","holdLastMs":200,"idleColor":"#b2b2b2"}
```

**Переопределение задержки (per-scene override):**
```json
{"id":"override_delay","title":"Override Delay","fps":15,"mode":{"count":3},"holdLastMs":500,"postEventDelayMs":1200}
```

`mode: {"count": 3}` — проиграть 3 раза, затем `holdLastMs` и в idle.

## Правила именования

- `id`: только `a-z 0-9 _ -`, 1..32 символа. Пример: `jump_hand`, `train-02`, `nuku_r2`.
- Папка **должна** называться как `id` или быть близкой — лоадер сравнивает, но не требует точного совпадения; главное — `scene.json#id` валиден.
- Не используй пробелы, кириллицу, заглавные буквы в `id`.

## settings.json и history.json

- [`docs/settings.schema.json`](../settings.schema.json) — глобальные настройки (`cyclesRoot`, `postEventDelayMs:500`, `selectionPolicy`, `noRepeatWindow:3`, `idleColor`, `autostart`, `appMap`).
- [`docs/history.schema.json`](../history.schema.json) — `{"recent":["sceneId"],"mtimeCursor":null}`. Перезаписывается атомарно, до 1KB, окно `recent` ≤ `noRepeatWindow`.

## Валидация

```powershell
# dotnet-валидация (встроенная, без ajv)
dotnet test --filter SceneSchema
```

Инвалид `fps:99` → ошибка:
```
SchemaValidationException: fps must be integer 1..30, got 99 at once_scene/scene.json#/fps
```

## Красный бейдж в UI

Если `scene.json` без `id` или с ошибкой — в списке сцен превью показывает 🔴 с tooltip `Missing required property 'id' at ...#/id`.

## Частые ошибки

- Забыл `id` → `Missing required property 'id'`
- `fps: 0` или `99` → `fps must be 1..30`
- `mode: "random"` → `mode must be once|loop|pingpong`
- `idleColor: "b2b2b2"` без `#` → `idleColor must match ^#[0-9a-fA-F]{6}$`
- Дополнительное поле `foo: 1` → `Unknown property 'foo'`
