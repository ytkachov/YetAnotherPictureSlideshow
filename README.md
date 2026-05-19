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
- Weather subsystem with pluggable providers (Yandex Weather API,
  Yandex Pogoda scrape, NGS Pogoda scrape) and a layered NSU
  point-thermometer override for current temperature.
- Companion utilities: `GeoTagger` (batch reverse-geocode a folder),
  `WeatherCollector` (Task Scheduler–driven cache writer),
  `WeatherCrawler` (one-shot debug fetch), `SlideshowLouncher`
  (auto-restart helper).

## Requirements

- Windows 10 / 11.
- .NET 8 SDK to build, .NET 8 Desktop Runtime to run.
- Google Chrome or Microsoft Edge installed — required only by the
  Selenium-based weather providers (`yandex-scrape`, `ngs-scrape`) and
  the NSU temperature override. The default `yandex-api` provider is a
  plain HTTP call and needs no browser.

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
| `WeatherProvider` | string | `yandex-api`, `yandex-scrape`, or `ngs-scrape` |
| `YandexApiKey` | string | Yandex Weather API key (only needed by `yandex-api`) |
| `WriteLog` | string `0`/`1` | Enable structured Serilog file output |
| `WriteLogFolder` | string | Where to write the log files |
| `PerformanceOptions` | dword | Bit flags for fade/scale/accent disable at night |

A `WeatherProvider` of `yandex-api` without a valid `YandexApiKey`
degrades cleanly: warnings are logged and the weather widgets hide
themselves rather than crashing the slideshow.

## Project layout

```
YAPS.Core                    net8.0          POCOs, abstractions, no Windows deps
YAPS.Infrastructure          net8.0-windows  Concrete impls (HTTP, OpenCV, Selenium)
PictureSlideshowScreensaver  net8.0-windows  WPF host + composition root (UseWPF)
WeatherCollector             net8.0-windows  Task Scheduler–driven snapshot writer
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
  Windows-only stacks: `HttpClient`-based geocoder and weather
  provider, OpenCV face detector, Selenium driver factory and the
  Selenium-based weather scrapers.
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
- `WeatherPollingService` (`BackgroundService`) ticks every 10 minutes
  and applies every `ICurrentTemperatureOverride` registered in the
  container to the fetched snapshot — that's the seam the NSU
  thermometer override plugs into.

To add a new weather provider, implement `IWeatherProvider` in
`YAPS.Infrastructure/Weather/Providers/`, register it as a keyed
singleton in `AddWeatherProviders`, and add a matching
`WeatherProviderDescriptor`. No other code needs to change.

## Status

The codebase is in the middle of a multi-stage architectural refactor;
stages 0–5 are landed and stages 6 (UI/MVVM, ComboBox for weather
provider in the Configuration window, WPF perf) and 7 (logging
polish, README/CLAUDE cleanup, `TreatWarningsAsErrors`) remain. See
`CLAUDE.md` for the conventions and anti-patterns established along
the way.

## License

MIT. See [LICENSE](LICENSE).
