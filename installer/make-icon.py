#!/usr/bin/env python3
"""Generate assets/Clipped.ico from docs/assets/logo.png.

Usage: CLIPPED_WRITE_ICON=1 python installer/make-icon.py

The icon under assets/ is owned by the visuals lane, so this refuses to write
unless CLIPPED_WRITE_ICON=1 is set and the logo has been changed on purpose.
"""

import os
from pathlib import Path

from PIL import Image

REPO = Path(__file__).resolve().parent.parent
SOURCE = REPO / "docs" / "assets" / "logo.png"
TARGET = REPO / "assets" / "Clipped.ico"
SIZES = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]


def main() -> None:
    if os.environ.get("CLIPPED_WRITE_ICON") != "1":
        raise SystemExit(
            "refusing to write assets/Clipped.ico: it is owned by the visuals lane.\n"
            "re-run with CLIPPED_WRITE_ICON=1 once the new logo is approved."
        )
    image = Image.open(SOURCE).convert("RGBA")
    TARGET.parent.mkdir(parents=True, exist_ok=True)
    image.save(TARGET, format="ICO", sizes=SIZES)
    print(f"wrote {TARGET}")


if __name__ == "__main__":
    main()
