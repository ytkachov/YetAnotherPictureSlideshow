#!/usr/bin/env python3
"""Detect sideways photos with a pre-trained model and record the correct
orientation in the photo's ``.finfo`` sidecar, so the slideshow can rotate
them on display WITHOUT modifying the original image files.

Why this exists
---------------
Many photos in a library have their rotation "baked" into the pixels while
their EXIF Orientation tag says Normal (or is missing) -- typically after an
export/re-save pipeline (Google Photos, messengers, scanners). A full scan of
this library found 0 photos with a real EXIF rotation flag, yet some are
visibly on their side. EXIF therefore can't fix them.

This tool looks at the *pixels* with a CNN that predicts the upright rotation,
then writes the matching orientation code into the photo's ``.finfo`` sidecar.
The screensaver reads ``FinfoData.Orientation`` and applies the rotation,
leaving the JPEG byte-for-byte untouched.

.finfo location
---------------
By default the sidecar sits next to the image. If the registry value
``HKCU\\SOFTWARE\\PictureSlideshowScreensaver\\FinfoFolder`` is set, it is a
``;``-separated list **positionally paired** with the ``ImageFolder`` list: for
photo folder *i*, an empty entry keeps the sidecar next to the photo while a
non-empty path stores it (mirroring the photo's relative subtree) ONLY under
that folder. This tool resolves the location the same way the C# code does, so
read-only photo libraries get their sidecars written to the configured folder.

The orientation code stored in ``.finfo`` is the standard EXIF orientation
value (1/3/6/8); the screensaver's RotateFlip mapping interprets it the same
way a compliant EXIF viewer would:
    1 = no transform   3 = rotate 180   6 = rotate 90 CW   8 = rotate 90 CCW

The model only knows the four 90-degree rotations (0/90/180/270); it does not
detect mirroring. That's the right scope for a slideshow.

Safety
------
* Default run is a DRY RUN: writes a CSV report and (optionally) preview JPEGs
  of the corrected result, and touches no sidecars.
* ``--apply`` merges the Orientation field into each ``.finfo`` (preserving any
  existing Faces / geo data), creating the sidecar if absent.
* The model's rotation convention (clockwise vs counter-clockwise) is verified
  and auto-calibrated on your machine via ``--self-test`` so 6-vs-8 can't be
  guessed wrong.

Usage
-----
    python fix_orientation.py --self-test sample.jpg      # verify + calibrate
    python fix_orientation.py PHOTOS/                     # dry run + report
    python fix_orientation.py PHOTOS/ --previews out/      # + corrected previews
    python fix_orientation.py PHOTOS/ --apply             # write .finfo sidecars

See README.md for installation and the full option list.
"""
from __future__ import annotations

import argparse
import csv
import json
import os
import re
import sys
from dataclasses import dataclass
from pathlib import Path

import numpy as np
from PIL import Image

# Heavy ML deps are imported lazily inside _load_model so that --help and the
# model-independent mapping check do not pay the torch import cost.

JPEG_EXTS = {".jpg", ".jpeg", ".jpe", ".jfif"}
TIFF_EXTS = {".tif", ".tiff"}
PNG_EXTS = {".png"}
SUPPORTED_EXTS = JPEG_EXTS | TIFF_EXTS | PNG_EXTS

FINFO_SCHEMA_VERSION = 2  # must match FinfoData.CurrentSchemaVersion

# factor = number of 90-degree CCW turns the *stored* image is away from
# upright (the np.rot90 convention). The orientation code below is the EXIF
# value whose standard correction restores upright; the screensaver applies
# exactly that. Re-checked by --self-test.
FACTOR_TO_CODE = {0: 1, 1: 6, 2: 3, 3: 8}

# Standard EXIF orientation -> rotation a viewer applies, expressed in CCW
# 90-degree turns. Used only by the self-test to prove FACTOR_TO_CODE undoes
# the rotation. (6 = rotate 90 CW = 3 CCW turns; 8 = rotate 90 CCW = 1 turn.)
CODE_TO_CCW_CORRECTION = {1: 0, 3: 2, 6: 3, 8: 1}

CALIB_FILE = Path(__file__).with_name(".orientation_calib.json")

# The model reliably distinguishes 90 degrees CW vs CCW, but is unreliable on
# 180-degree (code 3) calls, so by default only 6 and 8 are acted on. Override
# with --codes (e.g. "3,6,8" to include 180, or "3" to inspect only 180s).
DEFAULT_ACTIONABLE_CODES = "6,8"


def _parse_codes(text: str) -> set[int]:
    out: set[int] = set()
    for part in text.split(","):
        part = part.strip()
        if part:
            out.add(int(part))
    return out


@dataclass
class Prediction:
    factor: int          # 0..3, CCW turns from upright (calibrated)
    confidence: float     # softmax probability of the winning class


# --------------------------------------------------------------------------- #
# Model
# --------------------------------------------------------------------------- #
class OrientationModel:
    """Thin wrapper around the pre-trained `check_orientation` network.

    Isolated on purpose: swap the body of `_raw_predict` to plug in a
    different model without touching the rest of the script.
    """

    def __init__(self) -> None:
        import torch  # noqa: F401  (validate availability early)
        import albumentations as albu
        from check_orientation.pre_trained_models import create_model

        self._torch = __import__("torch")
        self._model = create_model("swsl_resnext50_32x4d")
        self._model.eval()
        self._transform = albu.Compose(
            [albu.Resize(height=224, width=224), albu.Normalize(p=1)], p=1
        )
        # calib maps the model's raw argmax index -> CCW factor (0..3).
        self._calib = {i: i for i in range(4)}

    def _raw_predict(self, rgb: np.ndarray) -> np.ndarray:
        """Return a length-4 probability vector over the model's own classes.

        check_orientation.create_model already appends nn.Softmax, so the
        model output IS the probability vector — do NOT softmax again (doing so
        flattens the distribution and caps confidence near 0.4).
        """
        torch = self._torch
        prepared = self._transform(image=rgb)["image"]
        tensor = torch.from_numpy(np.transpose(prepared, (2, 0, 1))).unsqueeze(0)
        with torch.no_grad():
            return self._model(tensor).cpu().numpy()[0]

    def predict(self, rgb: np.ndarray) -> Prediction:
        probs = self._raw_predict(rgb)
        raw_idx = int(probs.argmax())
        factor = self._calib.get(raw_idx, raw_idx)
        return Prediction(factor=factor, confidence=float(probs[raw_idx]))

    # -- calibration -------------------------------------------------------- #
    def calibrate(self, sample_rgb: np.ndarray, *, verbose: bool = True) -> dict[int, int]:
        """Learn the model's rotation convention from a sample image.

        We synthesise the four CCW rotations with np.rot90 and record which raw
        class the model assigns to each. That yields raw_index -> factor, which
        neutralises any CW/CCW disagreement between the model's labels and ours.
        Cached to disk so normal runs skip it.
        """
        mapping: dict[int, int] = {}
        rows = []
        for factor in range(4):
            rotated = np.rot90(sample_rgb, k=factor).copy()
            probs = self._raw_predict(rotated)
            raw_idx = int(probs.argmax())
            mapping[raw_idx] = factor
            rows.append((factor, raw_idx, float(probs[raw_idx])))

        if verbose:
            print("Calibration (synthetic CCW rotations of the sample):")
            print("  true_factor  model_raw_index  confidence")
            for f, idx, conf in rows:
                print(f"     {f}             {idx}            {conf:.3f}")

        if len(mapping) != 4:
            print(
                "WARNING: calibration was not bijective (model unsure on the "
                "sample). Falling back to identity mapping. Pick a clean, "
                "clearly-upright sample image for --self-test/--calibrate.",
                file=sys.stderr,
            )
            mapping = {i: i for i in range(4)}

        self._calib = mapping
        return mapping

    def load_calibration(self) -> bool:
        if CALIB_FILE.exists():
            try:
                data = json.loads(CALIB_FILE.read_text())
                self._calib = {int(k): int(v) for k, v in data.items()}
                return True
            except Exception:
                return False
        return False

    def save_calibration(self) -> None:
        try:
            CALIB_FILE.write_text(json.dumps({str(k): v for k, v in self._calib.items()}))
        except Exception as exc:  # pragma: no cover
            print(f"WARNING: could not cache calibration: {exc}", file=sys.stderr)


def _load_model() -> OrientationModel:
    try:
        return OrientationModel()
    except ImportError as exc:
        sys.exit(
            f"Missing ML dependency ({exc}).\n"
            "Install requirements first:\n"
            "    pip install -r requirements.txt"
        )


# --------------------------------------------------------------------------- #
# .finfo location resolver (mirrors C# FileFinfoStore / FinfoStoreOptions)
# --------------------------------------------------------------------------- #
_REGISTRY_PATH = r"SOFTWARE\PictureSlideshowScreensaver"


def _read_registry_folders() -> tuple[str | None, str | None]:
    """Return (ImageFolder, FinfoFolder) from HKCU, or (None, None)."""
    try:
        import winreg  # Windows-only; on other platforms there is no config.
    except ImportError:
        return None, None
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, _REGISTRY_PATH) as key:
            def get(name: str) -> str | None:
                try:
                    value, _ = winreg.QueryValueEx(key, name)
                    return value if isinstance(value, str) else None
                except FileNotFoundError:
                    return None
            return get("ImageFolder"), get("FinfoFolder")
    except OSError:
        return None, None


class FinfoResolver:
    """Maps an image path to its .finfo path using the same positional
    photo-folder/finfo-folder pairing the C# code reads from the registry.

    An empty (or missing) finfo entry, or an image not under any configured
    photo folder, resolves to the sidecar next to the photo (legacy behaviour).
    A non-empty entry mirrors the image's subtree (relative to the matched photo
    folder) under that finfo folder.
    """

    def __init__(self, image_folder: str | None, finfo_folder: str | None) -> None:
        self.pairs = self._build_pairs(image_folder, finfo_folder)

    @classmethod
    def from_registry(cls) -> "FinfoResolver":
        image_folder, finfo_folder = _read_registry_folders()
        return cls(image_folder, finfo_folder)

    @staticmethod
    def _normalize_folder(raw: str) -> str:
        return raw.strip().rstrip("\\/")

    @classmethod
    def _normalize_photo(cls, raw: str) -> str:
        p = raw.strip()
        if p.endswith(r"\*"):
            p = p[:-2]
        return cls._normalize_folder(p)

    @classmethod
    def _build_pairs(cls, image_folder: str | None, finfo_folder: str | None):
        if not image_folder:
            return []
        photos = image_folder.split(";")
        finfos = (finfo_folder or "").split(";")
        pairs: list[tuple[str, str]] = []
        for i, raw in enumerate(photos):
            photo = cls._normalize_photo(raw)
            if not photo:
                continue
            finfo = cls._normalize_folder(finfos[i]) if i < len(finfos) else ""
            pairs.append((photo, finfo))
        return pairs

    @staticmethod
    def _is_under(full: str, folder_full: str) -> bool:
        full_n = os.path.normcase(full)
        folder_n = os.path.normcase(folder_full)
        if len(full_n) <= len(folder_n):
            return full_n == folder_n
        return full_n.startswith(folder_n) and full_n[len(folder_n)] in ("\\", "/")

    def path_for(self, image_path: Path) -> Path:
        """`IMG_1234.jpg` -> resolved `.finfo` path (next to it, or mirrored)."""
        full = os.path.abspath(str(image_path))

        best: tuple[str, str] | None = None
        best_len = -1
        for photo, finfo in self.pairs:
            if not finfo:
                continue
            photo_full = os.path.abspath(photo)
            if len(photo_full) > best_len and self._is_under(full, photo_full):
                best = (photo_full, finfo)
                best_len = len(photo_full)

        if best is None:
            return Path(full).with_suffix(".finfo")

        photo_full, finfo = best
        rel = os.path.relpath(full, photo_full)
        mirrored = os.path.join(os.path.abspath(finfo), rel)
        return Path(mirrored).with_suffix(".finfo")


# --------------------------------------------------------------------------- #
# .finfo sidecar I/O
# --------------------------------------------------------------------------- #


def read_finfo(finfo_path: Path):
    """Return the parsed sidecar (dict or legacy list) or None."""
    if not finfo_path.exists():
        return None
    try:
        return json.loads(finfo_path.read_text(encoding="utf-8-sig"))
    except Exception:
        return None  # malformed; treated as "no usable sidecar"


def existing_finfo_orientation(data) -> int | None:
    if isinstance(data, dict):
        for k, v in data.items():
            if k.lower() == "orientation" and isinstance(v, int):
                return v
    return None


def _pop_key_ci(data: dict, name: str) -> None:
    """Remove a key from a dict, ignoring case (C# writes PascalCase)."""
    for k in [k for k in data if k.lower() == name.lower()]:
        data.pop(k, None)


def write_finfo_fields(finfo_path: Path, *, orientation: int | None = None,
                       attempted: bool | None = None) -> None:
    """Merge `Orientation` and/or `OrientationDetectionAttempted` into the
    sidecar, preserving everything else. Mirror of the per-field logic in
    `write_finfo_orientation`, but lets the caller choose what to write — used
    by `--mark-attempted` to record that the model was evaluated on a photo
    without necessarily assigning a rotation.
    """
    data = read_finfo(finfo_path)
    old = existing_finfo_orientation(data)
    if isinstance(data, list):
        data = {"SchemaVersion": FINFO_SCHEMA_VERSION, "Faces": data}
    elif not isinstance(data, dict):
        data = {"SchemaVersion": FINFO_SCHEMA_VERSION}

    if orientation is not None:
        if old != orientation:
            _pop_key_ci(data, "Faces")
        data["Orientation"] = orientation
    if attempted is not None:
        data["OrientationDetectionAttempted"] = attempted

    data.setdefault("SchemaVersion", FINFO_SCHEMA_VERSION)
    finfo_path.parent.mkdir(parents=True, exist_ok=True)
    finfo_path.write_text(
        json.dumps(data, indent=2, ensure_ascii=False), encoding="utf-8"
    )


def write_finfo_orientation(finfo_path: Path, value: int) -> None:
    """Merge the Orientation code into the sidecar, preserving everything else.

    When the orientation actually changes, cached face rectangles are dropped:
    they were detected against the old (un-rotated) bitmap and would land in the
    wrong place once the screensaver rotates the photo. Removing them makes the
    screensaver re-detect faces on the corrected image.

    A legacy bare-array sidecar (Rectangle[]) is wrapped into the modern object
    shape; if the orientation changes those faces are dropped too. The file is
    written as UTF-8 (no BOM), indented, matching the C# writer closely enough
    that its case-insensitive parser reads it back.
    """
    data = read_finfo(finfo_path)
    old = existing_finfo_orientation(data)
    if isinstance(data, list):
        # Legacy bare Rectangle[]: keep faces only if orientation is unchanged
        # (it never is here, since legacy files carry no Orientation).
        data = {"SchemaVersion": FINFO_SCHEMA_VERSION, "Faces": data}
    elif not isinstance(data, dict):
        data = {"SchemaVersion": FINFO_SCHEMA_VERSION}

    if old != value:
        _pop_key_ci(data, "Faces")

    data["Orientation"] = value
    data.setdefault("SchemaVersion", FINFO_SCHEMA_VERSION)
    # The finfo folder may be elsewhere than the photo (read-only library) and
    # the mirrored subtree may not exist yet.
    finfo_path.parent.mkdir(parents=True, exist_ok=True)
    finfo_path.write_text(
        json.dumps(data, indent=2, ensure_ascii=False), encoding="utf-8"
    )


def load_rgb(path: Path) -> np.ndarray:
    """Load pixels exactly as stored (we do not trust/transform EXIF here)."""
    with Image.open(path) as im:
        return np.array(im.convert("RGB"))


def iter_images(root: Path):
    if root.is_file():
        yield root
        return
    for dirpath, _dirs, files in os.walk(root):
        for name in files:
            if Path(name).suffix.lower() in SUPPORTED_EXTS:
                yield Path(dirpath) / name


# --------------------------------------------------------------------------- #
# Self-test
# --------------------------------------------------------------------------- #
def self_test(sample_path: Path) -> int:
    print(f"== Self-test using: {sample_path}\n")

    upright = load_rgb(sample_path)

    # 1) Prove FACTOR_TO_CODE picks the orientation value whose standard EXIF
    #    correction restores upright. Pure numpy, model-independent -- this is
    #    what nails down 6-vs-8.
    print("[1/2] Orientation-code mapping (standard EXIF semantics)")
    all_ok = True
    for factor in range(4):
        stored = np.rot90(upright, k=factor)              # a sideways file
        code = FACTOR_TO_CODE[factor]
        corrected = np.rot90(stored, k=CODE_TO_CCW_CORRECTION[code])
        ok = corrected.shape == upright.shape and np.array_equal(corrected, upright)
        all_ok &= ok
        print(
            f"   factor {factor} -> code {code}: viewer correction restores "
            f"upright  {'OK' if ok else 'FAIL'}"
        )
    print("   => mapping is", "CORRECT\n" if all_ok else "WRONG -- do NOT --apply\n")

    # 2) Calibrate + verify the model recognises the four rotations.
    print("[2/2] Model calibration & recognition")
    model = _load_model()
    model.calibrate(upright, verbose=True)
    correct = 0
    for factor in range(4):
        rotated = np.rot90(upright, k=factor).copy()
        pred = model.predict(rotated)
        hit = pred.factor == factor
        correct += hit
        print(
            f"   stored factor {factor}: model says factor {pred.factor} "
            f"(conf {pred.confidence:.3f})  {'OK' if hit else 'MISS'}"
        )
    model.save_calibration()
    print(f"   => model recognised {correct}/4 synthetic rotations")
    print(f"   calibration cached to {CALIB_FILE.name}")

    if all_ok and correct == 4:
        print("\nAll checks passed. Safe to run with --apply.")
        return 0
    print("\nSome checks failed -- review before using --apply.", file=sys.stderr)
    return 1


# --------------------------------------------------------------------------- #
# Main scan
# --------------------------------------------------------------------------- #
def run(args: argparse.Namespace) -> int:
    root = Path(args.path)
    if not root.exists():
        sys.exit(f"Path not found: {root}")

    model = _load_model()
    if not model.load_calibration():
        print(
            "No cached calibration found -- run `--self-test <upright.jpg>` once "
            "to calibrate the model's rotation convention. Proceeding with the "
            "default (identity) mapping for now.",
            file=sys.stderr,
        )

    previews_dir = Path(args.previews) if args.previews else None
    if previews_dir:
        previews_dir.mkdir(parents=True, exist_ok=True)

    html_path = Path(args.html) if args.html else None
    thumbs_dir = None
    html_entries: list = []
    if html_path:
        thumbs_dir = html_path.with_name(html_path.stem + "_thumbs")
        thumbs_dir.mkdir(parents=True, exist_ok=True)

    show_enabled = args.show  # turned off if the user presses 'q' in the window
    actionable_codes = _parse_codes(args.codes)
    resolver = FinfoResolver.from_registry()

    report_path = Path(args.report)
    fieldnames = [
        "path", "finfo_orientation_before", "predicted_factor",
        "confidence", "orientation_code", "action",
    ]

    counts = {"flagged": 0, "applied": 0, "ok_upright": 0, "skipped_low_conf": 0,
              "skipped_excluded": 0, "skipped_already_set": 0, "errors": 0}
    total = 0

    with report_path.open("w", newline="", encoding="utf-8") as fh:
        writer = csv.DictWriter(fh, fieldnames=fieldnames)
        writer.writeheader()

        for img_path in iter_images(root):
            total += 1
            if total % 200 == 0:
                print(f"  ...{total} scanned", file=sys.stderr)

            row = {"path": str(img_path), "finfo_orientation_before": "",
                   "predicted_factor": "", "confidence": "",
                   "orientation_code": "", "action": ""}
            rgb = None
            display_ccw = 0   # 90-degree CCW turns to make the thumbnail upright
            changed = False
            try:
                fpath = resolver.path_for(img_path)
                before = existing_finfo_orientation(read_finfo(fpath))
                row["finfo_orientation_before"] = before if before is not None else ""

                if before is not None and not args.overwrite:
                    row["action"] = "skip_already_set"
                    counts["skipped_already_set"] += 1
                    display_ccw = CODE_TO_CCW_CORRECTION.get(before, 0)
                else:
                    rgb = load_rgb(img_path)
                    pred = model.predict(rgb)
                    row["predicted_factor"] = pred.factor
                    row["confidence"] = f"{pred.confidence:.3f}"

                    if pred.factor == 0:
                        row["action"] = "ok_upright"
                        counts["ok_upright"] += 1
                    elif pred.confidence < args.min_confidence:
                        row["action"] = "skip_low_confidence"
                        counts["skipped_low_conf"] += 1
                    elif FACTOR_TO_CODE[pred.factor] not in actionable_codes:
                        # e.g. unreliable 180-degree calls when --codes is 6,8
                        row["orientation_code"] = FACTOR_TO_CODE[pred.factor]
                        row["action"] = "skip_excluded_code"
                        counts["skipped_excluded"] += 1
                    else:
                        code = FACTOR_TO_CODE[pred.factor]
                        row["orientation_code"] = code
                        counts["flagged"] += 1
                        display_ccw = (4 - pred.factor) % 4
                        changed = True

                        if previews_dir:
                            _write_preview(img_path, rgb, pred.factor, previews_dir)
                        if show_enabled:
                            show_enabled = _show_corrected(img_path, rgb, pred.factor, args.show_seconds)

                        if args.apply:
                            write_finfo_orientation(fpath, code)
                            row["action"] = "applied"
                            counts["applied"] += 1
                        else:
                            row["action"] = "would_apply"

                writer.writerow(row)

                if thumbs_dir is not None:
                    if rgb is None:
                        rgb = load_rgb(img_path)
                    thumb_name = f"{total:05d}_{_safe_stem(img_path)}.jpg"
                    _save_thumb(rgb, display_ccw, thumbs_dir / thumb_name)
                    html_entries.append({
                        "thumb": f"{thumbs_dir.name}/{thumb_name}",
                        "name": img_path.name,
                        "action": row["action"],
                        "code": row["orientation_code"],
                        "conf": row["confidence"],
                        "changed": changed,
                    })

            except Exception as exc:
                row["action"] = f"error: {exc}"
                counts["errors"] += 1
                writer.writerow(row)

    if args.show:
        try:
            import cv2
            cv2.destroyAllWindows()
        except Exception:
            pass

    if html_path:
        _write_html(html_path, html_entries, counts, root)

    print(f"\nScanned {total} images. Report: {report_path}")
    if html_path:
        print(f"HTML report: {html_path}")
    for k, v in counts.items():
        print(f"  {k:24s} {v}")
    if not args.apply and counts["flagged"]:
        print(f"\nDry run. Re-run with --apply to write {counts['flagged']} .finfo orientations.")
    return 0


def report_from_csv(args: argparse.Namespace) -> int:
    """Reuse a previously-written CSV, filtered to the given orientation codes,
    WITHOUT re-running the model. With --html it renders a corrected-thumbnail
    report; with --apply it writes those orientation codes into the .finfo
    sidecars (resolved the same way the screensaver does). Both can be combined.
    """
    csv_path = Path(args.from_csv)
    if not csv_path.exists():
        sys.exit(f"CSV not found: {csv_path}")

    codes = _parse_codes(args.codes)
    make_html = bool(args.html)
    need_resolver = args.apply or args.mark_attempted
    resolver = FinfoResolver.from_registry() if need_resolver else None

    thumbs_dir = None
    if make_html:
        html_path = Path(args.html)
        thumbs_dir = html_path.with_name(html_path.stem + "_thumbs")
        thumbs_dir.mkdir(parents=True, exist_ok=True)

    with csv_path.open(newline="", encoding="utf-8") as fh:
        rows = list(csv.DictReader(fh))

    selected = [r for r in rows
                if (r.get("orientation_code") or "").strip().isdigit()
                and int(r["orientation_code"]) in codes]

    actions = []
    if args.apply:
        actions.append(f"writing Orientation for codes {sorted(codes)}")
    if args.mark_attempted:
        actions.append("marking all rows as attempted")
    if make_html:
        actions.append(f"building thumbnails for codes {sorted(codes)}")
    print(f"{len(rows)} rows in CSV, {len(selected)} match codes {sorted(codes)} "
          f"({'; '.join(actions) or 'dry run'}; no model).")

    entries, errors, applied, marked = [], 0, 0, 0

    # HTML pass: per-thumbnail loop over the code-filtered subset (the only
    # cells worth showing in the report).
    if make_html:
        for i, r in enumerate(selected, 1):
            if i % 100 == 0:
                print(f"  thumbs: ...{i}/{len(selected)}", file=sys.stderr)
            code = int(r["orientation_code"])
            path = Path(r["path"])
            try:
                rgb = load_rgb(path)
                thumb_name = f"{i:05d}_{_safe_stem(path)}.jpg"
                _save_thumb(rgb, CODE_TO_CCW_CORRECTION.get(code, 0), thumbs_dir / thumb_name)
                entries.append({
                    "thumb": f"{thumbs_dir.name}/{thumb_name}",
                    "name": path.name,
                    "action": r.get("action", ""),
                    "code": code,
                    "conf": r.get("confidence", ""),
                    "changed": True,
                })
            except Exception as exc:
                errors += 1
                print(f"  thumb skip {path}: {exc}", file=sys.stderr)

    # Write pass: ONE loop over every non-error row. Each row is touched at
    # most once, regardless of whether it gets Orientation, the attempted flag,
    # or both. Skips rows the model never evaluated (action starts with "error").
    if need_resolver:
        selected_paths = {r["path"] for r in selected}
        for i, r in enumerate(rows, 1):
            if i % 500 == 0:
                print(f"  writing: ...{i}/{len(rows)}", file=sys.stderr)
            action = r.get("action") or ""
            if action.startswith("error"):
                continue

            path = Path(r["path"])
            code_str = (r.get("orientation_code") or "").strip()
            code = int(code_str) if code_str.isdigit() else None
            do_orientation = args.apply and code is not None and code in codes
            do_attempted = args.mark_attempted or do_orientation

            if not (do_orientation or do_attempted):
                continue

            try:
                write_finfo_fields(
                    resolver.path_for(path),
                    orientation=(code if do_orientation else None),
                    attempted=(True if do_attempted else None),
                )
                if do_orientation:
                    applied += 1
                if do_attempted and not do_orientation:
                    marked += 1
                elif do_attempted and do_orientation:
                    marked += 1  # also marked
            except Exception as exc:
                errors += 1
                print(f"  write skip {path}: {exc}", file=sys.stderr)

    counts = {"flagged": len(selected), "applied": applied, "ok_upright": 0,
              "skipped_low_conf": 0, "skipped_excluded": 0,
              "skipped_already_set": 0, "errors": errors}
    if make_html:
        _write_html(html_path, entries, counts, f"{csv_path.name} - codes {sorted(codes)}")
        print(f"HTML report: {html_path}")
    print(f"\nDone. Orientation written: {applied}, attempted-flag set: {marked}, errors: {errors}")
    return 0


def _corrected(rgb: np.ndarray, factor: int) -> np.ndarray:
    """Return the image rotated upright (undo the predicted CCW rotation)."""
    return np.ascontiguousarray(np.rot90(rgb, k=(4 - factor) % 4))


def _write_preview(src: Path, rgb: np.ndarray, factor: int, out_dir: Path) -> None:
    """Save a thumbnail of how the photo will look once corrected."""
    thumb = Image.fromarray(_corrected(rgb, factor))
    thumb.thumbnail((640, 640))
    thumb.save(out_dir / f"{src.stem}__f{factor}.jpg", quality=85)


def _safe_stem(p: Path) -> str:
    """ASCII-safe filename stem for thumbnails (Cyrillic etc. -> '_')."""
    return re.sub(r"[^0-9A-Za-z._-]+", "_", p.stem)[:60] or "img"


def _save_thumb(rgb: np.ndarray, ccw: int, dest: Path, max_px: int = 320) -> None:
    """Save a thumbnail rotated by `ccw` 90-degree CCW turns (the display fix)."""
    img = Image.fromarray(np.ascontiguousarray(np.rot90(rgb, ccw)))
    img.thumbnail((max_px, max_px))
    img.save(dest, quality=80)


def _write_html(html_path: Path, entries: list, counts: dict, root: Path) -> None:
    """Write a 4-column thumbnail grid; cells whose orientation changed are
    outlined in red."""
    import html as _html

    changed_n = sum(1 for e in entries if e["changed"])
    cells = []
    for e in entries:
        cls = "cell changed" if e["changed"] else "cell"
        if e["changed"]:
            meta = f"&#8635; code {e['code']} &middot; {e['conf']}"
        elif e["action"] == "ok_upright":
            meta = "upright"
        elif e["action"].startswith("skip_low"):
            meta = f"low conf {e['conf']}"
        elif e["action"] == "skip_already_set":
            meta = "already set"
        else:
            meta = _html.escape(e["action"])
        cells.append(
            f'<figure class="{cls}">'
            f'<img loading="lazy" src="{_html.escape(e["thumb"])}" alt="">'
            f'<figcaption>{_html.escape(e["name"])}<span class="meta">{meta}</span></figcaption>'
            f"</figure>"
        )

    doc = (
        "<!doctype html>\n<html lang=\"ru\"><head><meta charset=\"utf-8\">\n"
        "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n"
        "<title>Orientation report</title>\n<style>\n"
        " body{font-family:system-ui,Arial,sans-serif;margin:16px;background:#111;color:#eee}\n"
        " h1{font-size:18px;margin:0 0 4px}\n .sum{color:#aaa;margin-bottom:16px;font-size:13px}\n"
        " .grid{display:grid;grid-template-columns:repeat(4,1fr);gap:12px}\n"
        " .cell{background:#1c1c1c;border:2px solid transparent;border-radius:6px;padding:6px}\n"
        " .cell.changed{border-color:#e23b3b;box-shadow:0 0 0 1px #e23b3b}\n"
        " .cell img{width:100%;height:220px;object-fit:contain;background:#000;border-radius:3px;display:block}\n"
        " figcaption{font-size:12px;margin-top:4px;word-break:break-all}\n"
        " .meta{display:block;color:#9bd;margin-top:2px}\n .cell.changed .meta{color:#f88}\n"
        "</style></head><body>\n<h1>Orientation report</h1>\n"
        f'<div class="sum">{_html.escape(str(root))} &mdash; {len(entries)} photos, '
        f'<b style="color:#f88">{changed_n} with changed orientation (red border)</b>. '
        f"flagged={counts.get('flagged', 0)} applied={counts.get('applied', 0)} "
        f"upright={counts.get('ok_upright', 0)} low-conf={counts.get('skipped_low_conf', 0)} "
        f"excluded-code={counts.get('skipped_excluded', 0)} "
        f"already-set={counts.get('skipped_already_set', 0)} errors={counts.get('errors', 0)}</div>\n"
        '<div class="grid">\n' + "\n".join(cells) + "\n</div></body></html>\n"
    )
    html_path.write_text(doc, encoding="utf-8")


_SHOW_WINDOW = "orientation fix (q to stop showing)"


def _show_corrected(src: Path, rgb: np.ndarray, factor: int, seconds: float) -> bool:
    """Pop up the corrected image via OpenCV for `seconds`. Returns False if the
    user pressed 'q' (stop showing further), True otherwise."""
    try:
        import cv2
    except ImportError:
        print("  (--show needs opencv-python: pip install opencv-python)", file=sys.stderr)
        return False

    img = _corrected(rgb, factor)
    h, w = img.shape[:2]
    scale = min(1.0, 900.0 / max(h, w))
    if scale < 1.0:
        img = cv2.resize(img, (int(w * scale), int(h * scale)))
    bgr = cv2.cvtColor(img, cv2.COLOR_RGB2BGR)
    cv2.imshow(_SHOW_WINDOW, bgr)
    cv2.setWindowTitle(_SHOW_WINDOW, f"factor {factor} -> code {FACTOR_TO_CODE[factor]}  |  {src.name}")
    key = cv2.waitKey(max(1, int(seconds * 1000))) & 0xFF
    return key != ord("q")


# --------------------------------------------------------------------------- #
def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(
        description="Detect sideways photos and record the correct orientation "
                    "in their .finfo sidecars (originals untouched).",
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    p.add_argument("path", nargs="?", help="Image file or folder (recursed).")
    p.add_argument("--apply", action="store_true",
                   help="Write Orientation into .finfo (default: dry run).")
    p.add_argument("--self-test", metavar="IMAGE", dest="selftest",
                   help="Verify the mapping + calibrate the model on an upright "
                        "sample, then exit.")
    p.add_argument("--report", default="orientation_report.csv",
                   help="CSV report path (default: orientation_report.csv).")
    p.add_argument("--previews", metavar="DIR",
                   help="Also write corrected-preview thumbnails to DIR.")
    p.add_argument("--html", metavar="FILE",
                   help="Write an HTML report (4-column thumbnail grid; photos "
                        "whose orientation changed are outlined in red). "
                        "Thumbnails go in a sibling <FILE>_thumbs/ folder.")
    p.add_argument("--min-confidence", type=float, default=0.5,
                   help="Only act when the model's confidence >= this (default 0.5).")
    p.add_argument("--show", action="store_true",
                   help="Pop up each corrected (rotated) photo via OpenCV so you "
                        "can watch. Press 'q' in the window to stop showing.")
    p.add_argument("--show-seconds", type=float, default=2.0,
                   help="How long to show each photo with --show (default 2.0).")
    p.add_argument("--overwrite", action="store_true",
                   help="Recompute even files whose .finfo already has an "
                        "Orientation (default: skip them).")
    p.add_argument("--codes", default=DEFAULT_ACTIONABLE_CODES,
                   help="Comma-separated EXIF orientation codes to act on / "
                        f"filter by (default '{DEFAULT_ACTIONABLE_CODES}'; the "
                        "model is unreliable on 180-degree=3). Use '3' with "
                        "--from-csv to inspect only the 180-degree calls.")
    p.add_argument("--from-csv", metavar="FILE", dest="from_csv",
                   help="Rebuild an HTML report from a previous CSV, filtered by "
                        "--codes, WITHOUT re-running the model. Pair with --html.")
    p.add_argument("--mark-attempted", action="store_true", dest="mark_attempted",
                   help="With --from-csv: write OrientationDetectionAttempted=true "
                        "into EVERY non-error row's .finfo so subsequent "
                        "OrientationTagger / live-screensaver runs skip them.")
    return p


def main() -> int:
    # The Windows console is often cp1252, but photo paths here are routinely
    # Cyrillic; force UTF-8 so printing them never crashes.
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8")
        except Exception:
            pass

    args = build_parser().parse_args()

    if args.selftest:
        return self_test(Path(args.selftest))

    if args.from_csv:
        return report_from_csv(args)

    if not args.path:
        build_parser().print_help()
        return 2

    return run(args)


if __name__ == "__main__":
    raise SystemExit(main())
