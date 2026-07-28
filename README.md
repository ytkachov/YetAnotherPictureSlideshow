# YetAnotherPictureSlideshow

A WPF screensaver for Windows that pans Ken-Burns–style through a photo
library and overlays metadata: EXIF capture date, a reverse-geocoded
place name, face-detected accents, the current time, and live weather
with a 3-day forecast.

Built to run unattended on a digital photo frame.

## Features

- Smooth Ken-Burns pan/zoom transitions between photos picked at random
  from a configurable folder tree, with optional per-folder bucketing
  so successive photos cluster thematically.
- EXIF reading (capture date + GPS) via `ExifLibNet`.
- Reverse-geocoding via Nominatim (OpenStreetMap) with internal rate
  limiting, results cached next to the photo as `.finfo` JSON sidecars.
- Face detection via OpenCvSharp Haar cascade; detected face counts
  drive small accent glyphs on the overlay.
- Weather subsystem with pluggable providers (Open-Meteo, Yandex
  Weather API) and a layered NSU point-thermometer override for current
  temperature. Default is Open-Meteo — no API key, generous free-tier
  quota. Every provider and the NSU override are plain HTTP — no browser
  or web-driver is involved.
- Companion utilities: `GeoTagger` (batch reverse-geocode a folder),
  `WeatherCrawler` (one-shot debug fetch), `SlideshowLouncher`
  (auto-restart helper).

## Requirements

- Windows 10 / 11.
- .NET 8 SDK to build, .NET 8 Desktop Runtime to run.
- No browser or web-driver required — every weather source (providers
  and the NSU override) is a plain HTTP call.

## Building

```pwsh
dotnet build YetAnotherPictureSlideshow.sln -c Release -nologo -v minimal
```

The solution builds clean with zero warnings; that's the bar enforced
for every change.

## Running

After building, the screensaver executable supports the standard
Windows screensaver command-line conventions:

```pwsh
PictureSlideshowScreensaver.exe /s   # fullscreen slideshow
PictureSlideshowScreensaver.exe /c   # configuration dialog
PictureSlideshowScreensaver.exe /p   # preview mode — currently no-op
```

To install as the active Windows screensaver, copy the publish output
to a permanent location (Microsoft no longer accepts `.scr` from
arbitrary folders) or rename the binary to `.scr` and place it under
`C:\Windows\System32\`.

`SlideshowLouncher.exe` wraps the screensaver and relaunches it if it
exits — useful on a kiosk-mode photo frame.

While the slideshow is running, four keys are bound:

- `Escape` — close the screensaver.
- `F` — toggle the 3-day weather forecast overlay.
- `L` — open a viewer that tails the active Serilog file (useful on an
  appliance where the log folder isn't easy to get at).
- `S` — open the show registry: how evenly the library is actually being
  rotated, and which photos fail to load. See below.

## Show registry

Every photo the slideshow picks is counted, and every photo that fails to
decode is recorded with the error. Counters live in memory and are written
to `photo_stats.json` every `StatsFlushHours` (default 6) plus once on
clean shutdown — an appliance's storage should not be touched once per
slide. The file sits in the log folder (`WriteLogFolder`, falling back to
`WriteStatFolder`, then `%TEMP%\PictureSlideshow`).

`S` renders the analysis on demand: share of the library never shown,
histogram of show counts, Gini coefficient (0 = perfectly even), most-shown
photos, the unreadable ones, and a per-folder table. That last table is the
interesting one — the slideshow picks a random *folder* and then
`PhotosPerFolder` photos out of it, so a folder holding five photos gives
each of them far more screen time than one holding three thousand.

With `WriteStat=1` the same report is also written to `WriteStatFolder` as
`pss_stat_<date>.txt`, one file per day, rewritten in place.

## Configuration

All runtime configuration lives under
`HKEY_CURRENT_USER\Software\PictureSlideshowScreensaver` in the
registry. `PictureSlideshow.reg` is a template you can edit and
double-click. Notable keys:

| Key | Type | Purpose |
|---|---|---|
| `ImageFolder` | string | Wildcard root for the photo library, e.g. `D:\PHOTOS\*` |
| `Interval` | string (seconds) | Time between photo transitions |
| `FadeTime` | string (ms) | Cross-fade duration |
| `PhotosPerFolder` | string | How many random photos from one folder before switching folders |
| `WeatherProvider` | string | `open-meteo` (default) or `yandex-api` |
| `YandexApiKey` | string | Yandex Weather API key (only needed by `yandex-api`) |
| `WeatherPollingMinutes` | string | Minutes between provider polls (default `60`, clamped 1..1440) |
| `WriteLog` | string `0`/`1` | Enable structured Serilog file output |
| `WriteLogFolder` | string | Where to write the log files |
| `WriteStat` | string `0`/`1` | Write the daily show-registry report as a text file |
| `WriteStatFolder` | string | Where that report goes (also the fallback home of `photo_stats.json`) |
| `StatsFlushHours` | string | Hours between show-registry writes (default `6`, clamped 1..168) |
| `LogLevel` | string | Serilog minimum level: `Verbose` / `Debug` / `Information` / `Warning` / `Error` (default `Verbose`) |
| `PerformanceOptions` | dword | Bit flags for fade/scale/accent disable at night |

The default `open-meteo` provider has no API key and no per-day quota
to worry about. `yandex-api` is kept as a backup; without a valid
`YandexApiKey` it degrades cleanly (warnings logged, widgets hide
themselves rather than crashing the slideshow).

## Project layout

```
YAPS.Core                    net8.0          POCOs, abstractions, no Windows deps
YAPS.Infrastructure          net8.0-windows  Concrete impls (HTTP, OpenCV)
PictureSlideshowScreensaver  net8.0-windows  WPF host + composition root (UseWPF)
WeatherCrawler               net8.0-windows  Dev harness for weather providers
GeoTagger                    net8.0-windows  Batch reverse-geocoding console
SlideshowLouncher            net8.0          Auto-restart launcher
```

Cross-cutting build infra at the root:

- `Directory.Packages.props` — Central Package Management; all
  `PackageReference` entries are version-less and resolved here.
- `Directory.Build.props` — `LangVersion=latest`, `AnalysisLevel=latest`.

## Architecture sketch

The app is composed via `Microsoft.Extensions.Hosting` with a single
`Host.CreateApplicationBuilder()` per executable.

- `YAPS.Core` holds the contracts and POCOs (no WPF, no Windows
  dependencies; targets `net8.0` so the layer stays portable).
- `YAPS.Infrastructure` holds the implementations that depend on
  Windows-only stacks: `HttpClient`-based geocoder, weather providers
  and NSU temperature override, plus the OpenCV face detector.
- Each executable composes via an `AddXxx(this IServiceCollection)`
  extension (`AddInfrastructure`, `AddWeatherProviders`,
  `AddScreensaver`). New code never news up an `HttpClient` or wires a
  static singleton — both are anti-patterns the refactor has been
  paying off and CLAUDE.md catalogues explicitly.

The weather subsystem is the cleanest example of the plugin shape:

- `IWeatherProvider` (async, `IAsyncDisposable`, `Capabilities` flags)
  has three implementations registered as keyed singletons.
- `IWeatherProviderRegistry` looks providers up by string name.
- `IWeatherSnapshotStore` holds the last successful fetch; the UI
  consumes that store, not the providers directly.
- `WeatherPollingService` (`BackgroundService`) ticks on the interval
  set by `WeatherPollingMinutes` (default 60 — sized to the Yandex
  free-tier budget of ~30 requests/day) and applies every
  `ICurrentTemperatureOverride` registered in the container to the
  fetched snapshot — that's the seam the NSU thermometer override
  plugs into.

To add a new weather provider, implement `IWeatherProvider` in
`YAPS.Infrastructure/Weather/Providers/`, register it as a keyed
singleton in `AddWeatherProviders`, and add a matching
`WeatherProviderDescriptor`. No other code needs to change.

## Status

The codebase is in the middle of a multi-stage architectural refactor.
Stages 0–5 are fully landed, most of stage 6 (UI/MVVM, ComboBox for
the weather provider, `ConfigurationViewModel`, `Fant` scaling,
diagnostics) and stage 7 (`TreatWarningsAsErrors`, README, lifecycle
logging) are done; the open items are the `LocalImageInfo` split into
a Core POCO + Infrastructure bitmap loader (stage 4 tail), the
weather control's MVVM rewrite (6.2b/6.4) and `DecodePixelWidth` for
the slideshow image (6.7b). See `CLAUDE.md` for the conventions and
anti-patterns established along the way.

## License

MIT. See [LICENSE](LICENSE).
