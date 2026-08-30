# Cycles — хранение сцен / Scene storage

> Папка **сцена = папка**. Каждая сцена — папка `*.png|jpg|webp` + `scene.json`. Фпс только в `scene.json`.

## Portable vs Installed / Портативный vs Установленный

**Portable (приоритет):** `.\cycles\` рядом с `OsageLagtrain.exe`
```
OsageLagtrain.exe
cycles\
  _template\
    scene.json
    0001.png
  my_scene\
    scene.json
    0001.png
    0002.png
```
Приложение ищет `cycles` сначала рядом с exe (`Environment.ProcessPath` / `AppContext.BaseDirectory`). Это работает для portable-запуска и publish SingleFile.

**Installed fallback:** если portable-папка не найдена (например установка в `C:\Program Files\` без прав на запись), используется `%APPDATA%\OsageLagtrain\cycles\`
```
%APPDATA%\OsageLagtrain\cycles\
  _template\
  my_scene\
```
Проверка: `OsageLagtrain.exe --verify-cycles` → `template OK, 0 real scenes` или `template missing`.

## How to create a new scene / Как создать сцену

1. Скопируйте шаблон:
```powershell
Copy-Item -Recurse cycles\_template cycles\my_scene
```
2. Откройте `cycles\my_scene\scene.json` и поменяйте `id` (уникальный, `^[a-z0-9_-]{1,32}$`):
```json
{"id": "my_scene", "title": "My Scene", "fps": 12, "mode": "once", "holdLastMs": 800}
```
3. Замените `0001.png` своими кадрами: `0001.png`, `0002.png`, ... `0100.png` (натуральная сортировка).
4. Перезапустите приложение или выберите сцену в UI.

### Структура примера / Structure example
```
cycles\
  _template\          # шаблон, не удалять
    scene.json        # {"id":"template_scene","fps":12,"mode":"once","holdLastMs":800}
    0001.png          # placeholder 1x1 #b2b2b2, замените своими кадрами
  _template\.gitkeep  # для git
  jump_hand\          # ваша сцена
    scene.json
    0001.png
    0002.png
    0010.png
  my_loop\
    scene.json
    0001.png
    ...
```

## Natural sort note / Натуральная сортировка

Кадры сортируются через Windows `StrCmpLogicalW` (натуральная сортировка):
- Правильно: `0001.png, 0002.png, 0010.png` (2 перед 10)
- Лексикографическая сортировка (`10` перед `2`) **не используется**.

Рекомендуется нумерация `0001..9999` с ведущими нулями — порядок совпадёт в любом случае.

## Legal note / Юридическое примечание

- Не коммитьте реальные кадры из клипа Lagtrain / Inabakumori в публичный репозиторий — только placeholder `0001.png` 1x1 `#b2b2b2` разрешён.
- Исходник: `https://www.youtube.com/watch?v=UnIhRpIT7nc` — только для личного использования (Japan Copyright Act Art. 30 / US fair use).
- См. `CREDITS.txt` и `docs/ffmpeg-recipe.md` для рецепта извлечения кадров локально.

## Troubleshooting / Устранение неполадок

| Проблема | Решение |
|----------|---------|
| `template missing` в `--verify-cycles` | Проверьте что `cycles\_template\scene.json` рядом с exe; при SingleFile publish — рядом с `OsageLagtrain.exe` |
| Сцена не появляется в UI | Проверьте `id` в `scene.json` (только `a-z0-9_-`), валидный JSON без лишних полей; смотрите tooltip с ошибкой |
| Кадры в неправильном порядке | Переименуйте в `0001.png` с нулями; не кладите `fps` в имя файла |
| `cycles` игнорируется git | Нормально: `.gitignore` игнорирует `*.png` кроме `cycles/_template/*.png` — ваши кадры не должны попасть в коммит |
| Папка в Program Files не пишется | Используйте `%APPDATA%\OsageLagtrain\cycles\` (fallback) |

## No real frames commit warning / Не коммитьте реальные кадры

```
.gitignore:
  cycles/**/*.png      # игнор всех кадров
  !cycles/_template/*.png  # кроме placeholder шаблона
```

Только `cycles/_template/0001.png` (1x1 `#b2b2b2`) трекается. Все остальные `*.png|jpg|webp|mp4` в `cycles/` игнорируются. Не делайте `git add -f` для реальных кадров.

## Verification / Проверка

```powershell
dir .\cycles\_template
# должен показать scene.json + 0001.png

dotnet run --project src\App -- --verify-cycles
# -> template OK, 0 real scenes

git status
# ?? cycles\_template\0001.png — должен быть ?? (не ignored), а cycles\my_scene\0001.png — ignored (!! или не показывается)
```
