#!/usr/bin/env python3
"""Generate resources/icon.png (256x256) without any image library.

The store listings require a square 256 px icon; the mod has no art of its own, so this draws a
simple monogram: a copper "M" on a dark slate tile, with the same accent colour the HUD uses.

Usage: python3 tooling/make-icon.py
"""
import os
import struct
import zlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "resources", "icon.png")
SIZE = 256
SUPERSAMPLE = 2


def blend(base, top, alpha):
    return tuple(int(round(b * (1 - alpha) + t * alpha)) for b, t in zip(base, top))


def segment_distance(px, py, ax, ay, bx, by):
    vx, vy = bx - ax, by - ay
    wx, wy = px - ax, py - ay
    length = vx * vx + vy * vy
    t = 0.0 if length == 0 else max(0.0, min(1.0, (wx * vx + wy * vy) / length))
    dx, dy = px - (ax + t * vx), py - (ay + t * vy)
    return (dx * dx + dy * dy) ** 0.5


def draw():
    """Returns RGBA rows for the icon."""
    strokes = [
        # x0, y0, x1, y1, half-width, colour
        (84, 186, 84, 74, 15, (214, 133, 85)),
        (84, 74, 128, 138, 15, (198, 116, 70)),
        (128, 138, 172, 74, 15, (198, 116, 70)),
        (172, 74, 172, 186, 15, (176, 96, 56)),
        (66, 208, 190, 208, 7, (224, 163, 58)),
    ]

    rows = []
    for y in range(SIZE):
        row = bytearray()
        for x in range(SIZE):
            # supersample for smooth edges
            acc = [0.0, 0.0, 0.0, 0.0]
            samples = SUPERSAMPLE * SUPERSAMPLE
            for sy in range(SUPERSAMPLE):
                for sx in range(SUPERSAMPLE):
                    px = x + (sx + 0.5) / SUPERSAMPLE
                    py = y + (sy + 0.5) / SUPERSAMPLE

                    # background: vertical gradient inside a 6 px inset tile
                    inset = 6
                    if px < inset or py < inset or px > SIZE - inset or py > SIZE - inset:
                        pixel = (0, 0, 0, 0)
                    else:
                        shade = int(28 + 12 * (py / SIZE))
                        pixel = (shade - 2, shade + 2, shade + 8, 255)

                    for ax, ay, bx, by, half, colour in strokes:
                        if segment_distance(px, py, ax, ay, bx, by) <= half:
                            pixel = colour + (255,)
                    for c in range(4):
                        acc[c] += pixel[c]
            row.extend(int(round(v / samples)) for v in acc)
        rows.append(bytes(row))
    return rows


def write_png(path, rows):
    raw = b"".join(b"\x00" + row for row in rows)

    def chunk(tag, payload):
        body = tag + payload
        return struct.pack(">I", len(payload)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

    header = struct.pack(">IIBBBBB", SIZE, SIZE, 8, 6, 0, 0, 0)
    data = (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", header)
            + chunk(b"IDAT", zlib.compress(raw, 9))
            + chunk(b"IEND", b""))
    with open(path, "wb") as handle:
        handle.write(data)


def main():
    write_png(OUT, draw())
    print(f"wrote {OUT} ({os.path.getsize(OUT)} bytes, {SIZE}x{SIZE})")


if __name__ == "__main__":
    main()
