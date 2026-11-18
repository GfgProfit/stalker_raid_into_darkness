# Бенчмарк локаций РВМ

Система измеряет существующую сцену и сохраняет исходные кадры для сравнения будущих оптимизаций. Игровые настройки, симуляция, погода, качество рендера и ограничение FPS остаются под управлением проекта.

## FTM-диагностика Release

Отдельная сборка с `BENCHMARK_FRAME_TIMING_DIAGNOSTIC=1` собирает высокоуровневые CPU/GPU timings через Frame Timing Manager. Ей нужен явный `-benchmark-frame-timing-diagnostic` вместе с timing-маршрутом; guard отклоняет этот Player для clean FPS-серии. Результат помечается `instrumented=true`, `eligibleForFpsComparison=false` и `measurementClass=frame-timing-diagnostic`. Основная серия и контроль стоимости инструмента запускаются раздельно.

Файлы `frame-timings.csv`, `frame-timing-clock-sync.csv` и `frame-timing-diagnostic.json` сохраняют delayed publications, исходные timestamp domains и ограничения покрытия. Частота таймера не подтверждает общую эпоху; нулевой GPU sample означает unavailable. Точные команды, путь Archive driver и правила сопоставления приведены в [MEASUREMENTS.md](MEASUREMENTS.md). При удалении `_BENCHMARK` этот режим удаляется вместе с модулем, как описано в [REMOVAL.md](REMOVAL.md).

## CPU-диагностика обычного Release

Явный `-clean-route -benchmark-cpu-diagnostic -location-batching U` включает отдельный режим `release-cpu-diagnostic`. Ему подходит non-Development Player с выключенными Frame Timing Stats; native A/B выбирается отдельно. Результат всегда `instrumented=true`, `eligibleForFpsComparison=false`. PIX, FTM, captures и другие observers одновременно запрещены.

Без дополнительной конфигурации режим сохраняет доступные ProfilerRecorder markers/counters и накопленное Windows CPU-время основного потока. `-benchmark-cpu-marker-config <Archive JSON>` выбирает до 32 точных category/name/unit из фактического inventory. Inventory обновляется один раз после прогрева. Данные, clock brackets и ограничения интерпретации описаны в [MEASUREMENTS.md](MEASUREMENTS.md). Режим целиком удаляется вместе с `_BENCHMARK`.
## PIX-диагностика Release

Сгенерированный `Launchers/run_pix_diagnostic.py` запускает отдельный инструментированный сеанс через внешний `pixtool.exe`. Например: `python Launchers/run_pix_diagnostic.py military-player-eye --pix "<PIX>/pixtool.exe" --label Eye_Pos1 --capture-position 1`. Для live timing используйте отдельный запуск с `--timing` вместо `--capture-position`. Вывод остаётся в Archive рядом с соответствующей сборкой. Ordinary_Game и обычные U/G1 launcher не включают этот режим и не требуют PIX.

Допуск задаётся только сочетанием benchmark-сеанса и `-benchmark-pix-diagnostic`. Результат имеет статус `diagnostic-completed`, `instrumented=true` и не допускается в clean FPS-сравнения. Успешный guard или наличие DLL не подтверждают пригодность capture: необходимо открыть запись, проверить команды/ресурсы и маркеры временной привязки. Контракт и ограничения описаны в [MEASUREMENTS.md](MEASUREMENTS.md), исходники и сборка необязательного native-моста — в [Native/README.md](CleanReleaseRoute07/Native/README.md).

## Нативная подача Camera и теней

В текущем Release-эксперименте `-instance-native-submission A` выбирает исходную подачу, B запрашивает BRG для цветных `Camera` и теневых `Light` views. После успешной подготовки B пропускает полный legacy-цикл подачи инстансов. Неподдерживаемые данные, материалы, движение матриц, LOD, дистанционные ограничения и контексты камер используют исходный fallback. Session override не записывает shared GameConfig. Глобальный GameConfig включает native submission по умолчанию; grouping остаётся выключенным. Удаление `_BENCHMARK` сохраняет эти production defaults.

Тени привязаны к подготовленной Game-камере через функциональный HDRP `HDShadowCullingCameraScope` с проверкой API version/signatures. При несовместимом API native-подготовка отклоняется. Покрытие отражений подтверждают фактические view/command counters и отдельные visual fixtures; вызов callback сам по себе не доказывает исполнение геометрии. Камерная видимость и кандидаты для теней проверяются отдельно. Physical global bounds обеспечивают консервативное покрытие геометрии для регистрации BRG; source/index bounds решают отдельную задачу отбора кандидатов.

Ноль legacy `RenderMeshInstanced` calls означает отсутствие вызовов этого API. Геометрия и тени продолжают поступать через BRG. Native shadow commands/instances повторно учитывают source/submesh и ссылки на инстансы по Light views; они не равны GPU draw calls или числу уникальных объектов. Elapsed callback включает ожидания и не является чистым CPU-временем. Сохранённые старые Players с BRG color и legacy `ShadowsOnly` описывают предыдущий hybrid; их evidence не переписывается. Настоящая серия A1 → B1 → B2 → A2 должна отдельно установить выигрыш и сохранение изображения. Описания не подтверждают достижение 100 FPS.

Перед началом timing-маршрута контроллер ждёт 0,5 секунды устойчивого `Application.isFocused`. Потеря фокуса до старта сбрасывает ожидание; только затем начинается полный восьмисекундный прогрев. После старта прежняя строгая проверка потери фокуса остаётся активной. Win32 foreground не подменяет состояние Unity. Config-only проверка этого ожидания не использует.
## Запуск в Editor

Постоянные `BenchmarkRunner` разрешены только в отдельных сценах внутри `Assets/_BENCHMARK`. Для такого сценария откройте окно **Tools → RVM → Benchmark** и выберите его ScriptableObject. Компонент хранит прямые ссылки на камеру и четыре Transform; ScriptableObject хранит идентификатор сцены и ключи позиций.

Обычная сцена `l07_military` больше не содержит `BenchmarkRunner` и `BENCHMARK_POSITION`. Для текущей кампании используйте build-only `CleanReleaseRoute07`: четыре исторические позы находятся в `Scenarios/l07_military.route.json`, контроллер добавляется только в build-копию сцены. Один Release Player поддерживает `-location-preparation A` для полного history и `-location-preparation B` для подготовленных матриц. Подробности независимого удаления и отдельных технических запусков приведены в [REMOVAL.md](REMOVAL.md).

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

V2 записывает фактические погоду, время, переход/FX, аномалии и NavMeshAgent до и после каждого прогона. Diagnostic добавляет снимки с низкой частотой и на границах POS. `Fixed Weather / Time` ставит часы существующего стабильного погодного профиля на паузу в выбранном часу перед каждым повтором; после окончания, Abort, выключения компонента и выхода из Play Mode восстанавливает исходное время и паузу. Активные переходы/FX и несовпадающий ожидаемый профиль блокируют старт без частичной смены условий. Профиль не заменяется: SetProfile меняет владение погодой, для которого нет полного публичного save/restore API. Конфигурация GameConfig не изменяется.

Флаг `_sourceActive` у AnomalyPlayerReaction читается отдельно от активности компонента и AnomalyVisual. Выключенная визуализация не считается выключенной механикой. Число Reaction-компонентов не выдаётся за охват всех возможных реализаций аномалий. Счётчик NavMeshAgent не заменяет отсутствующий общий реестр NPC/AI. Физика, фазы аномалий, NPC и random state не сбрасываются; строгая сопоставимость требует проверки этих условий.

## Проверка overhead и регрессий

EditMode assembly: `Rvm.Benchmark.Tests.Editor`. Тесты покрывают percentiles, FPS lows и минимальный объём выборки, сравнения, unavailable, сценарий, порядок позиций, сериализацию, пути, восстановление камеры и частичные результаты при Abort. Sampler microbenchmark измеряет 1000 прогретых итераций и проверяет managed allocations. `collector_us` в CSV показывает время Update-инструментария, не полную стоимость native profiler/driver.

Полный overhead оценивается отдельным контролируемым экспериментом: одинаковая сцена, pose, разрешение и условия; чередующиеся серии Player без recorder и с Standard, внешний измеритель либо одинаковая базовая инструментализация в обеих сериях. Нельзя вычитать время C# collector и считать, что native profiler/GPU overhead исчез. Такой A/B эксперимент не меняет игровые настройки автоматически.

API проверены по Unity 6000.6 и установленным assemblies: [FrameTiming](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/frametiming), [ProfilerRecorder.StartNew](https://docs.unity.com/en-us/engine/6000.6/script-reference/unity/profiling/profilerrecorder/startnew), [Frame Timing Manager](https://docs.unity.com/en-us/engine/6000.3/manual/analysis/graphics-performance-profiling/profile-rendering/frame-timing-manager/manager).

## Diagnostic V2

В Tools/RVM/Benchmark выберите Diagnostic. CPU markers, GPU observations, Rendering snapshots и Memory counters включаются отдельными флагами; период снимков по умолчанию 1 секунда, минимум 0,5 секунды. Standard сохраняет прежний протокол: прогрев 8 секунд, четыре существующие позы по 5 секунд, Full/Stable и отдельные три повтора серии. Новые тяжёлые collectors в Standard не создаются.

После warmup, до первого измеряемого кадра, инструмент обнаруживает реальные ProfilerRecorderHandle и проверяет category/unit/flags. Выбор ограничен 96 CPU timing и до 64 дополнительных render/memory counters, а также 64 GPU markers с приоритетом основных HDRP passes. CPU хранит inclusive сумму по завершённому Unity frame и число вызовов, отдельно для Overall/POS и Full/Stable: mean, max, P95. Self time и thread lanes автоматически не восстанавливаются. Вложенные/параллельные значения не складываются в «общее CPU time». Отсутствие вызовов означает unavailable, а не нулевую стоимость подсистемы.

В V2.1 исправлена прежняя ошибочная трактовка bit 7: в установленном HDRP это Shared recorder, не frame-end flush. Benchmark этот флаг не использует. Все recorders принадлежат прогону и освобождаются при окончании/Abort; общие HDRP samplers не переключаются. CPU Profiler area, Profiler.enabled и binary log восстанавливаются в порядке владения. Deep Profiling автоматически не включается.

В `diagnostic/` сохраняются cpu.json, gpu.json, rendering.json, memory.json, world-state.json и diagnostic-report.md. Старые metadata.json/schemaVersion=1, frames.csv, timings.csv, summary.json и report.md сохраняют формат. Новые поля metadata аддитивны: metadataVersion=2, refreshRateHz, renderFrameInterval, timerSignature, instrumentationSignature, fixedWorldState и конечные pacing values.

GPU recorder создаётся только для реально зарегистрированного Render marker с SampleGPU/TimeNanoseconds при supportsGpuRecorder. Наблюдения запаздывают: native sample не содержит source Unity frame. Поэтому sourceUnityFrame/sourcePosition равны -1; observedPosition описывает момент чтения. Для GPU pass observations нет поддельных POS means/P95 и нет суммы вложенных scopes. Отсутствующий/нулевой sample не доказывает, что SSR, SSAO, SSGI, clouds или другой эффект выключен. GPU Frame Time в прежнем CSV по-прежнему использует отдельную проверенную QPC-привязку FrameTimingManager.

### Capture и анализ

Binary CPU capture сохраняет profiler.raw в каталог Run ID. Откройте его в Unity Profiler, найдите RVM.Benchmark.POS1..POS4, сопоставьте Unity frames с frames.csv и исключите transition-кадры. Timeline разделяет потоки, Hierarchy показывает self/inclusive. RVM.XRay.InstancedRenderer включает всю работу callback; MatrixHistory, Submission, Lod и Reflection выделяют соответствующие участки. Frustum/distance culling и остальная batch processing остаются внутри общего callback; для отдельного self анализа используйте Timeline. AnomalyCycle входит в вызывающий код, если native automatic marker отсутствует.

Для достоверной GPU детализации подключите Unity GPU Profiler или RenderDoc к отдельно разрешённому Development Player. Сохраните capture под `<RunID>/captures/POSn/`, добавьте sidecar: Run ID, исходный Unity frame, POS, transition, camera, API, HDRP и версию capture tool. Используйте стабильный кадр после POS marker. Если tool не показывает исходный Unity frame, пометьте привязку unverified либо отдельно удерживайте одну позу вне двадцатисекундного протокола. Capture overhead не относится к baseline. Frame Debugger показывает порядок и наличие passes, не их достоверную GPU стоимость.

Shadow maps, directional/additional shadows, fog/clouds/sky, depth/GBuffer/deferred/forward/transparency/distortion, SSR/SSAO/SSGI, post/TAA/bloom/custom/motion/reflection исследуются только при наличии соответствующих events в реальном capture. Стоимость Custom HLSL нельзя восстановить по числу материалов или разности несопоставимых кадров. Тяжёлый Memory Profiler snapshot делается отдельной операцией вне интервала и сохраняется в Archive.

### Семантика rendering и memory

Serialized batch count берётся из BatchSet.Batches. Отдельный registered GPU batch count API отсутствует и остаётся unavailable. LastVisibleBatches/Instances — результаты camera frustum + distance/wind tests до LOD; shadow-only считается отдельно. DiagnosticSubmittedBatches — уникальные логические batch objects, дошедшие до Submit. DiagnosticCpuSubmissions — число вызовов Graphics.RenderMeshInstanced внутри этого пути, включая submesh/chunk/LOD shadow splits; отдельные impostor/reflection paths в это число не включены. DiagnosticSubmissionInstanceReferences может повторно учитывать instance при разных submeshes/passes. Никакой из этих показателей не заменяет actual GPU draw calls. Данные instancer принимаются только при совпадении camera и завершённого Unity frame; SceneView не получает чужую подпись POS.

Компоненты света, частиц, renderers и мира кешируются перед прогревом; во время замера нет полного обхода сцены каждый кадр. Snapshot cost и время чтения collector сохраняются. Новые динамически созданные компоненты могут отсутствовать в кеше; start/end inventory проверяется заново. Renderer.isVisible относится ко всем камерам, frustum candidates не включают occlusion. Shadow-configured lights не равны реально отрисованным теням. Memory counters проверяют units; texture streaming memory — учёт Unity в bytes, не физическая VRAM резидентность драйвера. GC collections также остаются в исходном CSV.

### Глобальный frame pacing

Во всех Quality Levels (High Fidelity, Balanced, Performant) VSync Count=0. WindowsFramePacingDefaults один раз при SubsystemRegistration устанавливает Application.targetFrameRate=-1 для Windows Editor/Player. Последующий явный пользовательский выбор может изменить значения; Update-цикла принуждения нет. Opt-in precipitation safety probe больше не устанавливает 60 FPS. runInBackground=1 сохранён в PlayerSettings.

Окно и metadata показывают реальные VSync, target, refresh rate, Quality и Editor/Development/Release. VSync, положительный target и OnDemandRendering interval>1 вызывают предупреждение; Benchmark их не меняет. Лимиты драйвера, композитора ОС, Game View и режима Editor этим не снимаются. Default для обычного и Development Windows Player общий, но без новой Player Build нельзя утверждать, что он проверен запуском в обоих вариантах.

Comparison проверяет режим, API/Quality/resolution/scales, оба снимка мира, clock/profile/FX/transition, refresh/pacing, timer availability и instrumentation signature. Разные Git SHA сохраняются и допускаются. Legacy отчёты читаются, но отсутствие V2 условий не позволяет объявить их строго сопоставимыми. Diagnostic CPU/render/memory means сравниваются только при одинаковой signature и достаточных samples; P95 — от 30 samples. Не привязанные к исходному кадру GPU observations не участвуют в автоматических POS comparisons.

### Изменения вне _BENCHMARK и стоимость

QualitySettings.asset и WindowsFramePacingDefaults задают заказанный проектный default. В XRayPrecipitationPlayerSafetyProbe снят opt-in cap. В XRayLevelInstancedRenderer добавлены только диагностические scopes, gated counters и camera/frame stamp. Порядок существующих операций, culling, LOD, материалы, шейдеры и physics не изменяются. Вне активного Diagnostic scopes проверяют выключенный флаг и не создают managed allocations, collectors, снимки или Profiler sessions; небольшая стоимость условных проверок остаётся. Во время Diagnostic появляются marker/native recorder overhead, заполнение preallocated buffers и редкие allocating snapshots. Эти затраты видны в collector_us/snapshot cost, но полную native profiler стоимость нельзя вычитать арифметически. Для её оценки нужен отдельный контролируемый A/B эксперимент.

Snapshot XRayWeatherSnapshot сохраняет реальные rain/fog/wind/sun/sky параметры и blend keyframes; JSON текущего RenderPipelineAsset также входит в условия. После связывания Diagnostic recorders один подготовительный кадр не входит в измеряемые 20 секунд. Native memory totals включают preallocated diagnostic buffers. Полный binary capture этой локации может занимать около 2 ГБ за 30 секунд; по умолчанию он выключен.

## V2.1: достоверность и протокол условий

measurementVersion=3 и новая instrumentation signature отделяют исправленную семантику от V1/V2. Private recorders открываются по native handle, без Shared и без wrap. Consumer читает только новые публикации через монотонный Count; кадр без публикации не повторяет LastValue. После warmup курсоры синхронизируются до измерений. При исчерпании bounded capacity, неподтверждённом DataType, конкурирующей публикации или неоднозначной identity данные исключаются с причиной. Gauge использует последнее опубликованное значение без SumAllSamplesInFrame. Frame-total counters не суммируются потребителем повторно; inclusive timings используют нативную сумму вызовов без деления на Count. Main Thread — native marker и требует timing options.

Jobs/WaitForCompleted помещён в quarantine: V2 содержал uptime-scale значения, и их нельзя считать subsystem cost. Исходные native values, call count, число публикаций, QPC контекст и статус сохраняются ограниченной выборкой. Для неоднозначных одинаково описанных handles есть physical-source-evidence.json; они не удваивают coverage или deltas. Source found, samples seen, valid samples и invalid samples — отдельные поля. Raw enum расшифровывается rawStatusLegend; sourceUnityFrame=-1 означает отсутствие подтверждённого source frame. CPU POS tables относятся к контексту чтения и показывают sample coverage, не гарантируют native frame identity.

Video Memory Bytes описывает память устройства; это не usage и не residency. Used Buffers/Textures и прочие Unity counters описывают Unity accounting. Нативная публикация нуля отличается от отсутствия публикации; неизвестные данные не подменяются измеренным нулём. recorder-evidence.json Standard сохраняется после измерений. Старые recorder aggregates остаются читаемыми, но не получают доверие новой версии; QPC FPS/frame times сохраняются.

Comparison выдаёт Incompatible, Conditional, ConfirmedProtocol или Identity. Несовместимые API/resolution/mode/pacing/instrumentation, ошибки и Abort блокируют выводы. Unknown NPC registry остаётся видимым непроверенным условием и не превращает автоматически любые данные в несовместимые. Conditional разрешает наблюдаемые deltas без причинного/строгого A/B вывода. Identity проверяет сохранённые данные, но не заменяет два независимых запуска.

Подтверждение конкретного сценария оформляется conditions-attestation.json рядом с неизменённым metadata.json: версия, protocolId, exact Run IDs, build sourceDigest, scene, профиль/clock, reviewedBehaviourTypes, reviewSummary и scope. Перед созданием attestation необходимо проверить код участников из фактически записанного inventory, отсутствие неизвестных типов, NavMeshAgents и соответствие build provenance. Loader проверяет привязку; несовпадающие или новые участники не получают подтверждение. Это подтверждает только заявленный envelope сценария, не существование общего NPC registry и не детерминизм physics/random/particles/anomaly phases. Instance IDs нормализуются только при одинаковом непустом renderSettingsDigest.

CLI той же Development сборки:

```
RvmBenchmark.exe -benchmark -benchmark-mode Standard -benchmark-repeat 3 -benchmark-fixed-time 12 -benchmark-weather w_foggy3 -benchmark-output <ArchiveRoot> -logFile <ArchiveLog>
RvmBenchmark.exe -benchmark -benchmark-mode Diagnostic -benchmark-repeat 1 -benchmark-fixed-time 12 -benchmark-weather w_foggy3 -benchmark-cpu-capture 2 -benchmark-output <ArchiveRoot> -logFile <ArchiveLog>
```

Runtime session клонирует scenario; Production asset не изменяется. Binary capture по умолчанию выключен, ограничен 2 секундами (максимум 4), начинается после стабилизации POS1. Metadata фиксирует captureStartUnityFrame/captureEndUnityFrame. Это необходимый репрезентативный CPU capture, а не полная timeline всех четырёх POS. BenchmarkCaptureAnalysis открывает его в Unity Profiler, сохраняет предыдущие Profiler data и извлекает thread/self/inclusive/direct-child evidence. Wait scopes не следует считать busy CPU execution; inclusive/parallel времена не складываются в frame time.
