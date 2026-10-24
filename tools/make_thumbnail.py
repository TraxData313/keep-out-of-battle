r"""Builds the square Steam Workshop / mod thumbnail (1024x1024, under 1 MB) as docs\thumbnail.jpg.

    python tools\make_thumbnail.py

1. Cuts two panels out of the committed screenshots (3440x1440):
   - the field behind: docs\screenshots\order-of-battle.jpg, a square around the army and the hero pool;
   - the popup in front: docs\screenshots\keep-out-popup.jpg, the "Keep out of battle" dialog with two heroes ticked.
   Panels go to tools\build\ (gitignored).
2. Renders tools\preview_thumbnail.html with headless Edge (or Chrome) to a 1024x1024 PNG.
3. Saves docs\thumbnail.jpg, stepping JPEG quality down until it fits under 1 MB.

Same house style as Better Skirmisher Separation's thumbnails. Needs Pillow and Edge or Chrome.
"""
from __future__ import annotations

import io
import shutil
import subprocess
import time
from pathlib import Path

from PIL import Image

REPO = Path(__file__).resolve().parent.parent
SHOTS = REPO / "docs" / "screenshots"
WORK = REPO / "tools" / "build"
OUT = REPO / "docs" / "thumbnail.jpg"
STEAM_LIMIT = 1024 * 1024

FIELD_SHOT, FIELD_BOX = "order-of-battle.jpg", (1000, 0, 2440, 1440)      # square: army, hero pool, the button
POPUP_SHOT, POPUP_BOX = "keep-out-popup.jpg", (1370, 270, 2080, 1166)    # the dialog, title to Done (= .popup aspect)
POPUP_SIZE = (494, 623)

BROWSERS = [
    r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
    r"C:\Program Files\Google\Chrome\Application\chrome.exe",
]


def cut(shot: str, box: tuple[int, int, int, int], size: tuple[int, int], name: str) -> Path:
    img = Image.open(SHOTS / shot).convert("RGB").crop(box).resize(size, Image.LANCZOS)
    out = WORK / name
    img.save(out, quality=94, subsampling=0)
    return out


def wait_for_file(path: Path, timeout: float = 60.0) -> None:
    """Edge can return before its headless screenshot is on disk; wait until it exists and stops growing."""
    deadline, last = time.monotonic() + timeout, -1
    while time.monotonic() < deadline:
        size = path.stat().st_size if path.exists() else -1
        if size > 0 and size == last:
            return
        last = size
        time.sleep(0.5)
    raise SystemExit(f"The browser did not write {path}.")


def main() -> None:
    WORK.mkdir(parents=True, exist_ok=True)
    field = cut(FIELD_SHOT, FIELD_BOX, (1024, 1024), "field.jpg")
    popup = cut(POPUP_SHOT, POPUP_BOX, (POPUP_SIZE[0] * 2, POPUP_SIZE[1] * 2), "popup.jpg")

    html = (REPO / "tools" / "preview_thumbnail.html").read_text(encoding="utf-8")
    html = html.replace("FIELD_IMG", field.as_uri()).replace("POPUP_IMG", popup.as_uri())
    page = WORK / "thumbnail.html"
    page.write_text(html, encoding="utf-8")

    browser = next((b for b in BROWSERS if Path(b).exists()), None) or shutil.which("msedge") or shutil.which("chrome")
    if not browser:
        raise SystemExit("Edge or Chrome is needed to render the thumbnail.")
    png = WORK / "thumbnail.png"
    png.unlink(missing_ok=True)  # a stale render must never be mistaken for this one
    subprocess.run([browser, "--headless=new", "--disable-gpu", "--hide-scrollbars", "--force-device-scale-factor=1",
                    "--allow-file-access-from-files", "--window-size=1024,1024", f"--screenshot={png}",
                    page.as_uri()], check=True, capture_output=True, timeout=120)
    wait_for_file(png)
    img = Image.open(png).convert("RGB").crop((0, 0, 1024, 1024))

    for quality in range(95, 60, -3):
        buf = io.BytesIO()
        img.save(buf, "JPEG", quality=quality, optimize=True, progressive=True)
        if buf.tell() <= STEAM_LIMIT:
            OUT.write_bytes(buf.getvalue())
            print(f"{OUT} - {buf.tell() // 1024} KB, quality {quality}")
            return
    raise SystemExit("Could not get the thumbnail under 1 MB.")


if __name__ == "__main__":
    main()
