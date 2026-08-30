# FFmpeg Recipe — Lagtrain кадры локально / Local frame extraction

> Только для личного использования. Не распространяйте извлечённые кадры. См. Legal note внизу.

Источник: Inabakumori — Lagtrain (ヌクヌク) — https://www.youtube.com/watch?v=UnIhRpIT7nc

Все команды — copy-paste ready для PowerShell / cmd.

---

## 1) Скачать исходник 1080p h264 / Download source

Требует `yt-dlp` (https://github.com/yt-dlp/yt-dlp).

```powershell
yt-dlp -S "res:1080,vcodec:h264" --merge-output-format mp4 -o "lagtrain.%(ext)s" "https://www.youtube.com/watch?v=UnIhRpIT7nc"
# -> lagtrain.mp4 (1920x1080, h264)
```

Пояснение:
- `-S "res:1080,vcodec:h264"` — предпочесть 1080p h264 (совместимость с ffmpeg на Windows без vp9/av1 сборок)
- `--merge-output-format mp4` — склеить видео+аудио в mp4 если ютуб отдаёт раздельные потоки
- Альтернатива без предпочтения кодека: `yt-dlp -f "bestvideo[height<=1080]+bestaudio/best" --merge-output-format mp4 ...`

---

## 2) Дедупликация → кадры 1 fps, удаление повторов / Dedup to 1 fps

Требует `ffmpeg` (https://ffmpeg.org/).

```powershell
ffmpeg -i lagtrain.mp4 -vf "fps=1,mpdecimate" -vsync vfr -q:v 2 "frames\frame_%04d.jpg"
# -> frames\frame_0001.jpg, frame_0002.jpg, ... (только уникальные, ~1 fps)
```

Пояснение:
- `fps=1` — сначала проредить до 1 кадра/сек (для обоев достаточно; для плавности используйте `fps=12` или выше)
- `mpdecimate` — выкидывает дублирующиеся кадры (высокая статичность в аниме-клипе)
- `-vsync vfr` — variable frame rate, не дублировать удалённые кадры
- `-q:v 2` — качество JPEG 2..31 (2 = высокое, 5 = норм)

Вариации:
```powershell
# Сохранить как PNG без потерь (тяжелее)
ffmpeg -i lagtrain.mp4 -vf "fps=1,mpdecimate" -vsync vfr "frames\frame_%04d.png"

# Оставить 12 fps но дедуплицировать
ffmpeg -i lagtrain.mp4 -vf "fps=12,mpdecimate" -vsync vfr -q:v 2 "frames\frame_%04d.jpg"
```

Подсчёт результата:
```powershell
(Get-ChildItem frames\*.jpg).Count
```

---

## 3) Lyric-free, crop и ч/б / Lyric-free windows, crop, desaturation

### 3a) Вырезать окна без текста (lyric-free)

В клипе текст появляется примерно в нижней трети. Чтобы получить чистые кадры — вырезайте временные окна без лирики.

```powershell
# Пример: взять только первые 12 секунд и сегмент 45-60 секунд (без текста)
ffmpeg -i lagtrain.mp4 -vf "select='between(t,0,12)+between(t,45,60)',setpts=N/FRAME_RATE/TB" -vsync vfr -q:v 2 "clean\frame_%04d.jpg"

# Более сложный: несколько окон
ffmpeg -i lagtrain.mp4 -vf "select='between(t,0,12)+between(t,22,28)+between(t,45,55)+between(t,78,88)+between(t,110,130)',setpts=N/FRAME_RATE/TB" -vsync vfr -q:v 2 "clean\frame_%04d.jpg"
```

Пояснение:
- `between(t,0,12)` — выбрать секунды 0..12 (t = время в секундах)
- `+` — логическое ИЛИ (любое окно)
- `setpts=N/FRAME_RATE/TB` — пересчитать таймкоды после select, иначе ffmpeg растянет клип

Как найти окна: откройте клип, засеките где нет субтитров/текста, подставьте свои интервалы.

### 3b) Обрезать нижнюю часть с текстом / Crop
```powershell
# Обрезать 15% снизу (убрать зону текста), оставить 85% высоты
ffmpeg -i lagtrain.mp4 -vf "crop=iw:ih*0.85:0:0" -q:v 2 "cropped\frame_%04d.jpg"

# Комбо: crop + dedup + 1 fps
ffmpeg -i lagtrain.mp4 -vf "fps=1,crop=iw:ih*0.85:0:0,mpdecimate" -vsync vfr -q:v 2 "cropped\frame_%04d.jpg"

# Центр-кроп 16:9 если исходник 4:3 или наоборот (пример)
ffmpeg -i lagtrain.mp4 -vf "crop=ih*16/9:ih:(iw-ih*16/9)/2:0" -q:v 2 "cropped\frame_%04d.jpg"
```

Пояснение:
- `crop=iw:ih*0.85` — ширина = исходная (iw), высота = 85% (ih*0.85), x=0 y=0 (верх)
- Чтобы резать снизу: `crop=iw:ih*0.85:0:0` (от верха). Для снизу вверх: `crop=iw:ih*0.85:0:ih*0.15`

### 3c) Ч/б / Desaturation (чёрно-белые обои)

```powershell
# Вариант 1: eq фильтр (рекомендуется — сохраняет яркость)
ffmpeg -i lagtrain.mp4 -vf "eq=saturation=0" -q:v 2 "bw\frame_%04d.jpg"

# Вариант 2: hue
ffmpeg -i lagtrain.mp4 -vf "hue=s=0" -q:v 2 "bw\frame_%04d.jpg"

# Вариант 3: format gray
ffmpeg -i lagtrain.mp4 -vf "format=gray" -q:v 2 "bw\frame_%04d.jpg"

# Комбо: все фильтры вместе — lyric-free + crop + ч/б + dedup
ffmpeg -i lagtrain.mp4 -vf "select='between(t,0,12)+between(t,45,60)',setpts=N/FRAME_RATE/TB,crop=iw:ih*0.85:0:0,eq=saturation=0,fps=1,mpdecimate" -vsync vfr -q:v 2 "final\frame_%04d.jpg"
```

Рекомендуемый фулл-пайплайн для `cycles\` (скопируйте и вставьте):
```powershell
# 1. Скачать
yt-dlp -S "res:1080,vcodec:h264" --merge-output-format mp4 -o "lagtrain.%(ext)s" "https://www.youtube.com/watch?v=UnIhRpIT7nc"
# 2. Вырезать чистые окна + crop + ч/б + dedup → final\
mkdir final -Force
ffmpeg -i lagtrain.mp4 -vf "select='between(t,0,12)+between(t,45,60)',setpts=N/FRAME_RATE/TB,crop=iw:ih*0.85:0:0,eq=saturation=0,fps=1,mpdecimate" -vsync vfr -q:v 2 "final\frame_%04d.jpg"
# 3. Переименовать под cycles (0001.png...)
$i=1; Get-ChildItem final\*.jpg | Sort-Object Name | ForEach-Object { Copy-Item $_.FullName ("cycles\my_scene\{0:D4}.png" -f $i); $i++ }
# 4. Создать scene.json
Copy-Item cycles\_template\scene.json cycles\my_scene\scene.json
# отредактируйте id/fps/mode в cycles\my_scene\scene.json
```

---

## Legal note / Юридическое примечание

- **Japan Copyright Act Article 30** — частное использование (копирование для себя) разрешено, распространение — нет.
- **US Fair Use (17 USC §107)** — личное некоммерческое использование может считаться добросовестным, но не даёт права распространять.
- **Personal-only:** скачивайте и обрабатывайте только для своих обоев. Не коммитьте кадры в git, не выкладывайте архив кадров, не шарите папку `cycles\my_scene`.
- YouTube ToS разрешает скачивание только официальными средствами; `yt-dlp` — на ваш риск для личного архива.
- Если правообладатель (Inabakumori / Nukunuku) запросит удаление — удалите кадры.

## Проверка результата / Verification

```powershell
# Сколько кадров получилось
(Get-ChildItem final\*.jpg).Count
# Проверить наличие дубликатов (mpdecimate должен был убрать)
ffmpeg -i lagtrain.mp4 -vf "fps=1,mpdecimate" -vsync vfr -f null -
# Скопировать в cycles и проверить натуральную сортировку
dir cycles\my_scene | Sort-Object Name
```

## Troubleshooting

| Проблема | Решение |
|----------|---------|
| `yt-dlp: command not found` | `pip install yt-dlp` или `winget install yt-dlp.yt-dlp` |
| `ffmpeg: command not found` | `winget install Gyan.FFmpeg` или скачайте с ffmpeg.org и добавьте в PATH |
| `mpdecimate` не удаляет кадры | Клип динамичный — нормально; попробуйте без `fps=1` или с `mpdecimate=hi=64*48:lo=64*48:frac=0.33` |
| Текст всё ещё попадает | Сузьте `between(t,…)` окна или увеличьте `crop` до `ih*0.75` |
| Ч/б не применяется | Проверьте порядок фильтров: `eq=saturation=0` должен быть после `crop` |
