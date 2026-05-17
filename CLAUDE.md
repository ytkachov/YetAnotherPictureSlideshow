# CLAUDE.md

Conventions for working on **YetAnotherPictureSlideshow** — a WPF screensaver that pans Ken-Burns through a photo library with face-detected accents, EXIF dates, reverse-geocoded place names and live weather.

The refactor in progress is tracked in `~/.claude/plans/reflective-scribbling-bee.md`; current state lives in `~/.claude/projects/d--Projects-YetAnotherPictureSlideshow/memory/project_refactor_state.md`. **Read that state file before recommending any "fix" — most of the obvious defects are already addressed.**

---

## Solution layout

```
YAPS.Core              net8.0           — POCOs + abstractions, no Windows/WPF
YAPS.Infrastructure    net8.0-windows   — impls (HTTP, OpenCV, file I/O)
PictureSlideshowScreensaver  net8.0-windows WPF  — main app, composition root
Weather                net8.0-windows   — weather providers (legacy, Stage 5)
WeatherCollector       net8.0-windows   — Task Scheduler driven collector
WeatherCrawler         net8.0-windows   — debug harness
GeoTagger              net8.0-windows   — batch reverse-geocoding console
SlideshowLouncher      net8.0           — autorestart launcher
```

Build infra at the root:
- `Directory.Packages.props` — Central Package Management. **All `PackageReference` entries are version-less; versions live here.**
- `Directory.Build.props` — `LangVersion=latest`, `AnalysisLevel=latest`.

---

## Architectural rules

### Layer boundary

- **Core** must not reference WPF, `System.Windows.*`, `System.Drawing.Common` (Bitmap/Graphics), OpenCvSharp, Selenium, ExifLibrary, or anything Windows-only.
  - `System.Drawing.Primitives` (Rectangle/Point/Size) is fine — it's cross-platform base BCL.
  - Core depends on Serilog (FinfoData logs deserialisation warnings). If you remove Serilog later, swap to `Microsoft.Extensions.Logging.ILogger<T>`.
- **Infrastructure** is the home of every implementation that pulls in a Windows / native / WPF dependency.
- **Screensaver** project owns the WPF + composition root and references both Core and Infrastructure.
- The screensaver project keeps `OpenCvSharp4.runtime.win` even though the managed API lives in Infrastructure — runtime packages belong at the deployment leaf so the native DLLs land in `publish` output.

### Dependency injection

- Compose via `Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder()`. Both the screensaver (`App.xaml.cs`) and GeoTagger (`Program.cs`) follow the same pattern.
- Each layer exposes a single `AddXxx(this IServiceCollection)` extension that is the only thing other projects call into:
  - `Yaps.Infrastructure.ServiceCollectionExtensions.AddInfrastructure()` — `IFinfoStore`, `IGeocoder` (+ `HttpClientFactory`).
  - `PictureSlideshowScreensaver.Composition.ServiceCollectionExtensions.AddScreensaver()` — `IClock`, `Settings`, `ImagesProvider`, `IFaceDetector`, ViewModels, Window.
- **Never call `new HttpClient()`.** Register via `services.AddHttpClient<TClient, TImpl>(client => …)`.
- **Never wire a static singleton with `_self` + `_refcounter` + lazy init for a new service.** That pattern is the Weather-lib legacy we're paying off in Stage 5. New code uses container lifetimes.

### Shape of contracts

- Interfaces live in `YAPS.Core/Abstractions/`. Pattern: `I<Noun>` (`IGeocoder`, `IClock`, `IFinfoStore`, `IFaceDetector` lives in Infrastructure because its input is `Bitmap`).
- Async methods take `CancellationToken cancellationToken = default` as the last parameter.
- Return `Task<TResult?>` with `?` (Core has `<Nullable>enable</Nullable>`) and `return null` on a handled failure rather than throwing — existing callers tolerate null.
- Constructors take dependencies, not "factories of factories". Make dependencies optional only when the type must be constructable for one-off scripts (e.g. `LocalImageInfo` accepts `IGeocoder = null`).

---

## Code style baseline

- `Nullable enable` in new code. Files in legacy folders may still be `disable` — flip them when you touch them.
- `Random.Shared`, never `new Random()` (especially never `new Random(DateTime.Now.Millisecond)`).
- `FrozenDictionary<TKey, TValue>` for any `static readonly` lookup table that's built once and read many times — see the weather/wind encoding tables.
- `System.Text.Json` for new serialisation. Use `JsonSerializerContext` source generators when you can. **Don't enable `TypeNameHandling.Auto`** anywhere — it's the same deserialisation gadget System.Text.Json avoids by design.
- `Volatile.Read` / `Volatile.Write` for fields written by one thread and read by another without a lock (see `LocalImageInfo._placeName`).
- Wrap unmanaged / IDisposable resources:
  - `System.Drawing.Bitmap` → `using`.
  - WPF `BitmapImage` → call `.Freeze()` before handing it to another thread.
  - `WebDriver` / `CascadeClassifier` / `DispatcherTimer` / `CancellationTokenSource` → owning class implements `IDisposable` and stops/disposes them.
- Don't add `Co-Authored-By: Claude` trailers to commits.

---

## Anti-patterns explicitly banned

These all existed in the codebase and have been removed; don't reintroduce them:

| Anti-pattern | Replacement |
|---|---|
| `Thread.Sleep(x)` inside an `async` method | `await Task.Delay(x, cancellationToken)` |
| `lock(_lock) { Thread.Sleep(…); }` for rate limiting | `SemaphoreSlim` + `await Task.Delay` |
| `if (_self == null) _self = new T(); _refcounter++` | DI container with `Singleton` / `Transient` lifetime |
| `Process.Kill("chrome")` to clean up Selenium | `IDisposable` on the reader, `using` at the call site |
| `new HttpClient()` (or `static readonly HttpClient`) | `services.AddHttpClient<TClient, TImpl>` |
| `JsonConvert.SerializeObject(…, TypeNameHandling.Auto)` | `JsonSerializer.Serialize(…)` with typed DTOs |
| `Dictionary<…> = { … }` for static read-only lookups | `.ToFrozenDictionary()` |
| `double.Parse(registryString)` | `double.TryParse(…, NumberStyles, CultureInfo.InvariantCulture, out …)` |
| Hardcoded path like `C:\Projects\…` | `AppContext.BaseDirectory` + fallback chain |
| `#if STATISTICS` with no project ever defining `STATISTICS` | delete it |
| DispatcherTimer started in a constructor without a Stop path | own it via `IDisposable` or hook `Dispatcher.ShutdownStarted` |

---

## Build / run

```pwsh
dotnet build YetAnotherPictureSlideshow.sln -c Debug   -nologo -v minimal
dotnet build YetAnotherPictureSlideshow.sln -c Release -nologo -v minimal
```

- **The bar is 0 warnings.** Every refactor commit is expected to leave the solution that way.
- Run the screensaver standalone:
  - `PictureSlideshowScreensaver.exe /s` — fullscreen
  - `PictureSlideshowScreensaver.exe /c` — configuration window
  - `/p` is a no-op stub.
- Run GeoTagger: `GeoTagger.exe <photo-folder>` — recursively reads JPEG EXIF GPS, writes `.finfo` siblings.
- WeatherCollector: invoked by a Windows Task Scheduler entry (`WeatherFileReaderWriter.checkcollectorparams` registers it) every ~15 min.

The screensaver and GeoTagger can't be smoke-tested headlessly. After a non-trivial change to either, build clean is the only automated check; **say so explicitly** when reporting status rather than claiming the change "works".

---

## WPF gotchas worth remembering

- **`BitmapImage` not frozen ⇒ XAML can throw `InvalidOperationException: The calling thread cannot access this object`** when a background task (e.g. fire-and-forget geocoder) updates anything observable on the same VM. `LocalImageInfo.Bitmap2BitmapImage` freezes — preserve that.
- **`DispatcherTimer` keeps a strong reference to its `Tick` handler**, which keeps its owning VM alive. Without an explicit `Stop()` + `Tick -=` the VM survives window close. Every timer in this codebase has a teardown — keep it that way.
- **WPF `Image` with `Stretch="Uniform"`** + huge JPEG = lots of GC pressure. The plan calls for `DecodePixelWidth = ScreenWidth` in Stage 6; don't add new code paths that load full-resolution Bitmaps if you don't need them.
- **CalcBinding** (`xmlns:cb`) is in use in `Screensaver.xaml`. It's a one-line library that lets you write `'IsActive ? 1 : 0'` in `Panel.ZIndex`. Don't be surprised by the namespace.

---

## .finfo persistence

`FinfoData` (in `YAPS.Core/Models/`) has a backward-compat reader:

- A file starting with `[` is parsed as legacy `Rectangle[]` and wrapped into a fresh `FinfoData`.
- A file starting with `{` is parsed as the modern shape, ignoring unknown properties (so Newtonsoft-written files with `Top`/`Left`/`Right`/`Bottom` from `Rectangle` still load).
- All writes stamp `SchemaVersion` and emit indented JSON.

If you bump the schema:
1. Increment `FinfoData.CurrentSchemaVersion`.
2. Either keep the property additive (the default behaviour stays correct) or branch in `TryDeserialize` on the read version.

Read/Write always goes through `IFinfoStore` from now on — that's the seam for tests and for any future encrypted / SQLite-backed store.

---

## Weather library (Stage 5, not yet done)

`Weather/` keeps the pre-refactor shape on purpose: static `_self` + `_refcounter`, `release()` on the interface, Selenium scrapers. **Don't bolt new features onto that surface.** When you touch it, port it to the same pattern `IGeocoder` uses:

1. New abstraction in `YAPS.Core/Abstractions/IWeatherProvider.cs` (no `release()` — use `IAsyncDisposable`).
2. New `IWeatherProviderRegistry` returning descriptors so the Configuration window can list providers and let the user pick.
3. Implementations in `YAPS.Infrastructure/Weather/` registered via `services.AddWeatherProvider<TImpl>("name", caps)`.
4. Migrate `WeatherCollector` to the same `Host.CreateApplicationBuilder()` it can now share.

Until then, the screensaver's `WeatherInformer.cs` still calls `WeatherProviderYandexApi.get()` — leave that wiring alone if you're not in the middle of Stage 5.

---

## EXIF / ExifLibrary gotcha

`reader.Properties[ExifTag.X].Get<T>()` does **not** work for the array-shaped GPS tags (`GPSLatitude`, `GPSLongitude`). Use the indexer and cast to `Array`:

```csharp
var latProp = reader.Properties[ExifTag.GPSLatitude];
if (latProp?.Value is Array latArr && latArr.Length == 3) { … }
```

See `GeoTagger/Program.cs` and `LocalImages.cs` for the established pattern (`GpsArrayToDouble`).

---

## Git / PR hygiene

- Commit messages: imperative, short subject (≤72 chars), body explains *why* not *what*. Match the existing log style — short, declarative, no "feat:" / "fix:" prefixes.
- One logical change per commit. Mixing a correctness fix with a rename costs us bisection.
- **No `Co-Authored-By: Claude` trailer.**
- Don't push to `master` unless asked. Don't `--force` to a shared branch.
- The repo is configured for CRLF on Windows; you'll see git warnings on commit. Ignore them.

---

## Memory files you should keep in sync

`~/.claude/projects/d--Projects-YetAnotherPictureSlideshow/memory/` has the live state:

- `project_refactor_state.md` — what's done / what's left / non-obvious decisions. **Update this when you complete a stage.**
- `reference_key_files.md` — the file map. Update when you add a Core abstraction or Infrastructure impl.
- `project_geolocation.md` — original .finfo / geocoding intent. Update only if the schema changes.

When a stage finishes, prune obsolete entries (e.g. the old `project_known_defects.md` was deleted once Stages 1+2 closed it out) so the next session doesn't try to re-fix something.
