"""Import only the explicitly supplied local Zalo sticker cache; never read chats/accounts.

Requires Pillow. Zalo's horizontal PNG strips do not carry timing: use 100 ms/frame.
Keep GIF timings; decode WebP by its signature (some .webp files actually contain PNG).
"""
import argparse
import hashlib
import json
import shutil
from pathlib import Path
from PIL import Image


def gif_frame(image):
    rgba = image.convert("RGBA")
    # Reserve palette index 255 for transparency. Per-frame palettes preserve artwork.
    palette = rgba.convert("RGB").quantize(colors=255)
    palette.paste(255, mask=rgba.getchannel("A").point(lambda a: 255 if a < 128 else 0))
    palette.info["transparency"] = 255
    return palette


def run(source, destination):
    destination.mkdir(parents=True, exist_ok=True)
    report = {"source": "Zalo PC local sticker cache", "spriteFrameMilliseconds": 100,
              "packNames": "Source IDs retained where display names are unavailable", "packs": [], "errors": []}
    for folder in sorted(source.iterdir(), key=lambda p: int(p.name) if p.name.isdigit() else -1):
        if not folder.is_dir() or not folder.name.isdigit():
            continue
        pack = {"Id": "zalo-" + folder.name, "Name": "Zalo · " + folder.name, "Stickers": []}
        target = destination / pack["Id"]
        target.mkdir(exist_ok=True)
        for item in sorted(folder.iterdir(), key=lambda p: int(p.name) if p.name.isdigit() else -1):
            if not item.is_dir() or not item.name.isdigit():
                continue
            candidates = []
            for f in item.iterdir():
                if not f.is_file() or f.suffix.lower() not in (".png", ".gif", ".webp"):
                    continue
                try:
                    with Image.open(f) as im:
                        frames = getattr(im, "n_frames", 1)
                        strip = im.format == "PNG" and im.width > im.height and im.width % im.height == 0
                        count = im.width // im.height if strip else frames
                        candidates.append((count > 1, count, f.stat().st_mtime_ns, f))
                except Exception as error:
                    report["errors"].append({"asset": str(f.relative_to(source)), "error": str(error)})
            if not candidates:
                continue
            f = max(candidates)[3]
            try:
                with Image.open(f) as im:
                    frames = getattr(im, "n_frames", 1)
                    strip = im.format == "PNG" and im.width > im.height and im.width % im.height == 0
                    count = im.width // im.height if strip else frames
                    if count > 300:
                        raise ValueError("More than 300 animation frames")
                    extension = ".gif" if count > 1 else ".png"
                    temp = target / ("convert" + extension)
                    if im.format == "GIF" and frames > 1:
                        shutil.copyfile(f, temp)
                    elif count > 1:
                        animation, durations = [], []
                        for index in range(count):
                            if strip:
                                frame = im.crop((index * im.height, 0, (index + 1) * im.height, im.height))
                                duration = 100
                            else:
                                im.seek(index)
                                frame = im.copy()
                                duration = max(20, im.info.get("duration", 100))
                            animation.append(gif_frame(frame))
                            durations.append(duration)
                        animation[0].save(temp, save_all=True, append_images=animation[1:], duration=durations,
                                          loop=0, disposal=2, transparency=255, optimize=False)
                    else:
                        im.convert("RGBA").save(temp)
                    data = temp.read_bytes()
                    name = hashlib.sha256(data).hexdigest() + extension
                    temp.replace(target / name)
                    pack["Stickers"].append({"Id": "zalo-" + item.name, "Name": "Sticker " + item.name, "File": name})
            except Exception as error:
                report["errors"].append({"asset": str(f.relative_to(source)), "error": str(error)})
        if pack["Stickers"]:
            (target / "pack.json").write_text(json.dumps(pack, ensure_ascii=False, indent=2), encoding="utf-8")
            report["packs"].append({"id": pack["Id"], "stickers": len(pack["Stickers"]),
                                    "animated": sum(s["File"].endswith(".gif") for s in pack["Stickers"])})
    (destination / "source.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"packs": len(report["packs"]), "stickers": sum(p["stickers"] for p in report["packs"]),
                      "animated": sum(p["animated"] for p in report["packs"]), "errors": report["errors"]}))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("destination", type=Path)
    args = parser.parse_args()
    run(args.source, args.destination)
