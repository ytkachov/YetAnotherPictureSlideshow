# Orientation fixer

Standalone tool that finds photos whose rotation is baked into the pixels while
their EXIF Orientation tag says *Normal* (or is missing) — the case where a
viewer shows the picture lying on its side. It runs a pre-trained CNN over the
pixels, predicts the upright rotation, and records the correct orientation in
the photo's **`.finfo` sidecar**. **The original image files are never
modified.**

The screensaver reads `FinfoData.Orientation` and rotates the photo on display.
A full scan of this library found **0** photos carrying a real EXIF rotation
flag, so the sidecar — not EXIF — is the authoritative source for rotation.

> Scope: the model handles the four 90° rotations (0/90/180/270). It does **not**
> detect mirrored images. That's the right scope for a slideshow.

## What gets written

The tool stores the standard EXIF orientation **code** (1/3/6/8) in the sidecar:

```json
{
  "SchemaVersion": 2,
  "Faces": [ ... preserved if already present ... ],
  "Orientation": 6
}
```

The screensaver interprets the code exactly like a compliant EXIF viewer:
`1` = none, `3` = 180°, `6` = 90° CW, `8` = 90° CCW. Any existing sidecar data
(faces, GPS, place name) is preserved — only the `Orientation` field is added.

## Where the `.finfo` is written

By default the sidecar is written **next to the image**. If the registry value
`HKCU\SOFTWARE\PictureSlideshowScreensaver\FinfoFolder` is set, the tool follows
the same rule the screensaver and GeoTagger use:

- `FinfoFolder` is a `;`-separated list **positionally paired** with the
  `ImageFolder` list (`ImageFolder` entries may carry a trailing `\*` recursion
  marker, which is ignored for matching).
- For the photo folder an image belongs to, an **empty** paired entry keeps the
  sidecar next to the photo; a **non-empty path** stores it (mirroring the
  photo's subtree *relative to that photo folder*) **only** under that folder.
- An image not under any configured photo folder falls back to next-to-photo.

Example — `ImageFolder = C:\Photos\A;D:\Photos\B`, `FinfoFolder = ;E:\finfo\B`:
`C:\Photos\A\2023\x.jpg` → `C:\Photos\A\2023\x.finfo` (empty entry), while
`D:\Photos\B\sub\y.jpg` → `E:\finfo\B\sub\y.finfo`. This lets a **read-only**
photo library still get its sidecars written to a separate, writable folder.

## Install

```pwsh
cd tools\orientation
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
```

`torch` is a large download. CPU-only is fine; there's no GPU requirement.

## 1. Calibrate & verify (run once)

Pick any clearly **upright** JPEG and run the self-test. It (a) proves the
factor→code mapping with pure rotation math, and (b) learns the model's
rotation convention (clockwise vs counter-clockwise) and caches it to
`.orientation_calib.json` next to the script:

```pwsh
python fix_orientation.py --self-test C:\path\to\some_upright_photo.jpg
```

Expect `mapping is CORRECT` and `model recognised 4/4`. If either fails, **do
not** use `--apply` — open an issue with the printed table.

## 2. Dry run (default — writes nothing)

```pwsh
python fix_orientation.py "Z:\PHOTO_FRAME_STORE"
```

Produces `orientation_report.csv`. Add corrected-preview thumbnails to eyeball
the decisions before committing:

```pwsh
python fix_orientation.py "Z:\PHOTO_FRAME_STORE" --previews previews_out
```

Each preview is named `<stem>__f<factor>.jpg` and shows how the photo will look
once corrected (`factor` = number of 90° CCW turns the stored pixels are off).

### HTML report (recommended for review)

Generate a single scrollable page — a 4-column thumbnail grid where every
scanned photo is shown **already corrected**, and the ones whose orientation
changed are **outlined in red**:

```pwsh
python fix_orientation.py "Z:\PHOTO_FRAME_STORE" --html report.html
```

Thumbnails are written to a sibling `report_thumbs/` folder, so keep the `.html`
and that folder together. Open `report.html` in any browser and scan for the
red borders — that's the fastest way to eyeball every proposed change at once.

## 3. Apply (writes `.finfo` sidecars)

```pwsh
python fix_orientation.py "Z:\PHOTO_FRAME_STORE" --apply
```

Only files with `predicted_factor != 0` and confidence ≥ `--min-confidence`
(default `0.5`) get a sidecar update. Files whose `.finfo` already has an
`Orientation` are skipped (re-run safe) unless you pass `--overwrite`.

## 180° is unreliable — only 90° is trusted

In practice the model reliably tells 90° CW from 90° CCW, but its **180°
(code 3) calls are essentially always wrong** (they fire on flat ground-level
shots — a crab on sand, a wall — that have no real "up"). So by default the
tool **only acts on codes 6 and 8** (`--codes 6,8`). Pass `--codes 3,6,8` to
include 180° if you ever want it.

### Re-report from an existing CSV (no rescan)

To review a subset of an earlier scan without re-running the model, point
`--from-csv` at the CSV it wrote and filter with `--codes`. It only re-reads the
matching images to make thumbnails:

```pwsh
# HTML of just the 180°-rotation candidates from a previous full scan
python fix_orientation.py --from-csv orientation_report.csv --codes 3 --html report_180.html
```

## Accuracy & limitations (read before `--apply`)

Validated on a 1530-photo travel folder: ~1.4% were flagged as rotated. Spot
checks confirmed the rotation **direction is correct** (e.g. a 90°-rotated
church spire is restored upright). But the model **does make confident
mistakes** on unusual compositions — low-angle shots looking up at interiors,
or flat textures (a wall, the sky) with no real "up". One upright pulpit photo
was flagged at 0.97 confidence and would have been rotated onto its side.

So a high confidence is **not** a guarantee, and tuning `--min-confidence`
alone won't make blind `--apply` safe. Treat this as a review-assisted tool:

1. Dry-run with `--show` (or `--previews`) and watch the corrections.
2. Trust the obvious wins (people, buildings, horizons rotated 90°).
3. For the rest, glance and let your eye veto the false positives.

If you `--apply` across everything unseen, expect a few good photos to get
rotated wrong — review the CSV / previews afterwards and fix those by re-running
with `--overwrite` after deleting the bad `.finfo`, or by editing the sidecar.

## Options

| Flag | Meaning |
|---|---|
| `--apply` | Write Orientation into `.finfo`. Without it, dry run. |
| `--self-test IMAGE` | Verify mapping + calibrate model, then exit. |
| `--report PATH` | CSV output path (default `orientation_report.csv`). |
| `--previews DIR` | Also write corrected-preview thumbnails. |
| `--html FILE` | Write an HTML report: 4-column thumbnail grid, changed photos outlined in red. Thumbnails go in a sibling `<FILE>_thumbs/` folder. |
| `--min-confidence F` | Act only when model confidence ≥ F (default `0.5`). |
| `--overwrite` | Recompute files whose `.finfo` already has an Orientation. |
| `--codes LIST` | Comma-separated orientation codes to act on / filter by (default `6,8`; the model is unreliable on 180°=`3`). |
| `--from-csv FILE` | Rebuild an HTML report from a previous CSV, filtered by `--codes`, without re-running the model. Pair with `--html`. |
| `--show` | Pop up each corrected (rotated) photo via OpenCV so you can watch. Press `q` in the window to stop showing. |
| `--show-seconds F` | How long to show each photo with `--show` (default `2.0`). |

Watch the corrections live while applying:

```pwsh
python fix_orientation.py "Z:\PHOTO_FRAME_STORE" --apply --show
```

## Face accents stay correct

Face rectangles are detected against the *rotated* bitmap, so when this tool
changes a photo's orientation it **drops any cached `Faces`** from the sidecar.
The screensaver treats a missing `Faces` array as "not yet detected" and
re-runs face detection on the corrected image, then merges the result back
(keeping the `Orientation`, geo and place name). A sidecar whose orientation is
unchanged keeps its faces, so re-runs don't trigger needless recomputation.

## Reusing on other collections

It's fully standalone — point `path` at any folder. The calibration is
model-global, so step 1 only needs to run once per machine/model, not per
collection.

## Swapping the model

`OrientationModel._raw_predict` in `fix_orientation.py` is the only model-aware
code; it returns a length-4 probability vector. Replace its body to plug in a
different orientation classifier — the calibration step adapts to whatever
convention the new model uses.
