# Бенчмарк локаций РВМ

Система измеряет существующую сцену и сохраняет исходные кадры для сравнения будущих оптимизаций. Игровые настройки, симуляция, погода, качество рендера и ограничение FPS остаются под управлением проекта.

## Запуск в Editor

Откройте подготовленную сцену `l07_military` и окно **Tools → RVM → Benchmark**. Выберите `Assets/_BENCHMARK/Scenarios/l07_military.asset`. Компонент `BenchmarkRunner` на существующем контейнере точек хранит прямые ссылки на `Level Camera` и четыре Transform. ScriptableObject хранит идентификатор сцены и ключи позиций, а не ссылки на объекты сцены.

Основной протокол: прогрев 8 секунд с обходом всех точек, затем POS 1, POS 2, POS 3, POS 4 по 5 секунд. Первые 0,5 секунды каждой позиции помечаются transition. Прогрев можно отключить. Длительности доступны в окне; изменённый протокол записывается в metadata и считается отличающимся условием сравнения. Между позициями нет пауз. Граница позиции сдвигается до ближайшего следующего кадра, поэтому фактические 20 секунд могут быть немного длиннее.

Сохраните свои изменения сцены перед запуском. **Запустить один прогон** включает Play Mode, собирает данные, сохраняет отчёт и возвращает Editor в Edit Mode. **Серия из трёх** сохраняет три самостоятельных прогона и отдельный отчёт с медианой и диапазоном по запускам. Остановка Play Mode сохраняет завершённые кадры со статусом aborted. Камера восстанавливает исходные local position/rotation. По требованию проекта `Run In Background` постоянно включён в PlayerSettings для benchmark и обычных игровых сборок. Runner фиксирует фактическое значение в metadata и не переключает его при запуске или завершении. Во время прогона не редактируйте сцену и не двигайте камеру. Если её перемещает другая система, runner прерывает измерение.

Для другой локации создайте четыре Transform и выберите **Создать и подключить сценарий текущей сцены**. Автоподключение требует уникальных ключей точек и единственной активной Game camera. При нескольких камерах укажите ссылки вручную и нажмите **Подключить выбранные ссылки**. Новые benchmark-ресурсы должны оставаться внутри `Assets/_BENCHMARK`.

## Development Player

Подготовленная сцена должна быть стартовой сценой сборки. Обычный запуск приложения не включает бенчмарк. Для автоматического запуска:

```text
RvmBenchmark.exe -benchmark -benchmark-scenario l07_military -benchmark-repeat 1 -benchmark-output E:/Projects/stalker_raid_into_darkness_Archive/PerformanceBenchmarks -logFile E:/Projects/stalker_raid_into_darkness_Archive/PerformanceBenchmarks/player.log
```

`-benchmark-repeat 3` запускает серию. После успешного сохранения Player завершается с кодом 0; при ошибке запуска, aborted или ошибке записи — с кодом 2. Путь вывода проверяется до измерений. По политике этого проекта разрешены только подкаталоги `E:/Projects/stalker_raid_into_darkness_Archive`; переходы через junction/reparse point запрещены. Fallback в папки Unity или persistentDataPath не применяется. Для запуска на другом компьютере потребуется доступный Archive по этому пути.

`BenchmarkBuild.QueueDevelopmentBuild(outputFolder)` подготавливает Windows x86_64 Development Player с единственной выбранной сценой, не меняя EditorBuildSettings и PlayerSettings. Сборка запускается только явным вызовом. Результат содержит `RvmBenchmark.exe`, `RunBenchmark.cmd`, `RunBenchmarkSeries.cmd`, `build-status.json`. Директория должна быть новой. Build processor записывает Git SHA, dirty и версию HDRP в копию сцены, используемую сборкой. Сохранённые production-ресурсы для этого не меняются.

## Результаты

Один Run ID соответствует отдельной директории:

| Файл | Содержимое |
| --- | --- |
| `metadata.json` | Окружение, настройки, четыре точных pose, условия мира на старте и финише |
| `frames.csv` | Все завершённые измеряемые кадры, позиция 1–4, transition/stable, timings, GC, counters |
| `timings.csv` | Исходный поток FrameTimingManager с source timestamps, включая прогрев и дренирование задержанных результатов |
| `summary.json` | Overall и POS 1–4, отдельно Full и Stable, доступность и число samples каждой метрики |
| `report.md` | Таблицы статистики и ограничения |
| `benchmark.log` | Старт, результат, ошибки и предупреждения |
| `complete.json` | Подтверждение успешной записи набора файлов; статус completed либо aborted |

При недоступности метрики CSV оставляет пустую ячейку, report пишет unavailable, а JSON содержит `available=false` и причину. Числовые поля DTO без доступной выборки не являются измерениями и должны игнорироваться. Отсутствие GPU данных не блокирует сохранение остальных показателей. Незавершённый при остановке кадр не получает выдуманную длительность.

Average FPS = `1000 / mean(frame time ms)`. Minimum/Maximum FPS = `1000 / maximum/minimum frame time`. N% Low = `1000 / mean(самые медленные ceil(frameCount * N/100) кадры)`. Для 1% требуется минимум 100 кадров, для 0,1% — 1000. Percentiles используют линейную интерполяцию по индексу `(count - 1) * p`. Пятисекундная выборка на 20–50 FPS ограничивает достоверность lows и не описывает редкие длительные пики.

## Источники метрик и привязка кадров

Монотонный Windows QueryPerformanceCounter (Stopwatch на других платформах) измеряет интервалы между соседними ранними Update. Интервал принадлежит камере, установленной при его старте. Перед переключением позиции записывается предыдущий завершённый кадр. ProfilerRecorder читает previous completed frame; название, категория и единицы counters проверяются через `ProfilerRecorderHandle.GetAvailable` перед созданием recorder. Список доступности сохраняется для каждого запуска. Бенчмарк не складывает CPU и GPU times.

FrameTimingManager выдаёт задержанные результаты. Они сохраняются отдельно и сопоставляются после измерений: на Windows используется native QueryPerformanceCounter с проверкой частот CPU timer и QPC; Mono Stopwatch может иметь другую эпоху и для привязки не используется; source `frameStartTimestamp` должен попадать между предыдущим и текущим Update, а Present — следовать за текущим Update. Неоднозначные совпадения и первый кадр исключаются из агрегатов timings. Номер кадра получения GPU данных не используется как номер исходного кадра. На неподтверждённой временной базе GPU и CPU Total остаются unavailable. В конце есть восемь кадров дренирования; они не входят в измеряемую выборку. Если задержка больше, недополученные timings остаются отсутствующими.

Standard включает только основные recorder. Diagnostic добавляет дополнительные memory/GC/shadow counters. Binary Profiler capture включается отдельным флагом только в Diagnostic и восстанавливает глобальные настройки при завершении. Он пишет на диск и меняет overhead, поэтому не сопоставим со Standard. Включённый Deep Profiling блокирует запуск из окна. Метрики рендера в Editor могут включать SceneView и другие камеры. Unity graphics memory — учёт Unity, а не полное измерение резидентности VRAM.

Frame Timing Stats должен быть доступен для CPU Total/GPU/Present measurements. Система проверяет доступность и сама не меняет ProjectSettings. Поддержка recorder в Release Player зависит от API и конкретного counter.

## Сравнение

В окне выберите Baseline и Candidate: `report.md`, `summary.json` либо путь к директории запуска. Comparison Tool сохраняет отдельные JSON и Markdown в Archive/PerformanceBenchmarks/Comparisons. Для Overall и каждой позиции выводятся Full/Stable, FPS, lows, P95/P99, CPU/GPU, render counters и memory. Изменение вычисляется как `(candidate - baseline) / baseline * 100`; при нулевом baseline процент недоступен.

Положительный FPS и отрицательный frame time соответствуют Improvement; обратное направление — Regression. Пользователь задаёт практический порог No meaningful difference, по умолчанию 0%. Это описание наблюдаемого направления, а не статистический тест. Различия Graphics API, разрешения, Quality, позиций, камеры, режима исполнения/профилирования, оборудования, длительностей и записанных условий делают сравнение Incomparable. Статус dirty сохраняется, SHA может различаться между проверяемыми оптимизациями.

Погода читается через существующие публичные свойства XRayWeatherController только до и после измерений. Время, случайное состояние и мир не сбрасываются между повторами. Достоверный агрегированный флаг состояния аномалий без отдельного provider отсутствует; отчёт это указывает и не объявляет контролируемое A/B-сравнение доказанным. Для расширения условий добавьте сценовый компонент с `IBenchmarkMetadataProvider`, который заполняет записи без изменения gameplay.

## Проверка overhead и регрессий

EditMode assembly: `Rvm.Benchmark.Tests.Editor`. Тесты покрывают percentiles, FPS lows и минимальный объём выборки, сравнения, unavailable, сценарий, порядок позиций, сериализацию, пути, восстановление камеры и частичные результаты при Abort. Sampler microbenchmark измеряет 1000 прогретых итераций и проверяет managed allocations. `collector_us` в CSV показывает время Update-инструментария, не полную стоимость native profiler/driver.

Полный overhead оценивается отдельным контролируемым экспериментом: одинаковая сцена, pose, разрешение и условия; чередующиеся серии Player без recorder и с Standard, внешний измеритель либо одинаковая базовая инструментализация в обеих сериях. Нельзя вычитать время C# collector и считать, что native profiler/GPU overhead исчез. Такой A/B эксперимент не меняет игровые настройки автоматически.

API проверены по Unity 6000.6 и установленным assemblies: [FrameTiming](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/frametiming), [ProfilerRecorder.StartNew](https://docs.unity.com/en-us/engine/6000.6/script-reference/unity/profiling/profilerrecorder/startnew), [Frame Timing Manager](https://docs.unity.com/en-us/engine/6000.3/manual/analysis/graphics-performance-profiling/profile-rendering/frame-timing-manager/manager).
