#!/usr/bin/env python3
"""Import a square PNG as the store icon: decode, box-downsample to 256x256, re-encode.

No image library is available on this machine, so this does the PNG work itself. It is only used
when new art arrives; `tooling/make-icon.py` remains the drawn fallback.

Usage: python3 tooling/import-icon.py <source.png> [--size 256]
"""
import os
import struct
import sys
import zlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "resources", "icon.png")


def read_png(path):
    data = open(path, "rb").read()
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise SystemExit(f"{path} is not a PNG")
    index = 8
    idat = b""
    width = height = depth = colour = None
    while index < len(data):
        length = struct.unpack(">I", data[index:index + 4])[0]
        tag = data[index + 4:index + 8]
        payload = data[index + 8:index + 8 + length]
        if tag == b"IHDR":
            width, height, depth, colour, _comp, _filt, interlace = struct.unpack(">IIBBBBB", payload)
            if interlace:
                raise SystemExit("interlaced PNG is not supported")
        elif tag == b"IDAT":
            idat += payload
        elif tag == b"IEND":
            break
        index += 12 + length

    if depth != 8 or colour not in (0, 2, 4, 6):
        raise SystemExit(f"unsupported PNG: depth {depth}, colour type {colour}")
    channels = {0: 1, 2: 3, 4: 2, 6: 4}[colour]

    raw = zlib.decompress(idat)
    stride = width * channels
    rows = []
    previous = bytearray(stride)
    position = 0
    for _ in range(height):
        filter_type = raw[position]
        position += 1
        line = bytearray(raw[position:position + stride])
        position += stride
        if filter_type == 1:
            for x in range(channels, stride):
                line[x] = (line[x] + line[x - channels]) & 0xFF
        elif filter_type == 2:
            for x in range(stride):
                line[x] = (line[x] + previous[x]) & 0xFF
        elif filter_type == 3:
            for x in range(stride):
                left = line[x - channels] if x >= channels else 0
                line[x] = (line[x] + ((left + previous[x]) >> 1)) & 0xFF
        elif filter_type == 4:
            for x in range(stride):
                left = line[x - channels] if x >= channels else 0
                up = previous[x]
                upleft = previous[x - channels] if x >= channels else 0
                estimate = left + up - upleft
                dl, du, dul = abs(estimate - left), abs(estimate - up), abs(estimate - upleft)
                if dl <= du and dl <= dul:
                    nearest = left
                elif du <= dul:
                    nearest = up
                else:
                    nearest = upleft
                line[x] = (line[x] + nearest) & 0xFF
        elif filter_type != 0:
            raise SystemExit(f"bad PNG filter {filter_type}")
        rows.append(line)
        previous = line
    return width, height, channels, rows


def to_rgba(channels, line):
    """Expand a scanline to RGBA bytes."""
    if channels == 4:
        return line
    if channels == 3:
        out = bytearray()
        for x in range(0, len(line), 3):
            out += line[x:x + 3] + b"\xff"
        return out
    if channels == 2:  # grey + alpha
        out = bytearray()
        for x in range(0, len(line), 2):
            grey = line[x]
            out += bytes((grey, grey, grey, line[x + 1]))
        return out
    out = bytearray()
    for x in range(len(line)):
        grey = line[x]
        out += bytes((grey, grey, grey, 255))
    return out


def resize(width, height, channels, rows, size):
    """Box-average down to size x size; refuses to upscale."""
    if width < size or height < size:
        raise SystemExit(f"source is {width}x{height}, needs to be at least {size}x{size}")
    if width % size or height % size:
        raise SystemExit(f"{width}x{height} does not divide evenly into {size}x{size}")

    stride = width * channels
    rgba_rows = [to_rgba(channels, row) for row in rows]
    span_x, span_y = width // size, height // size
    block = span_x * span_y

    out = []
    for by in range(size):
        line = bytearray()
        for bx in range(size):
            totals = [0, 0, 0, 0]
            for y in range(by * span_y, (by + 1) * span_y):
                row = rgba_rows[y]
                base = bx * span_x * 4
                for x in range(span_x):
                    offset = base + x * 4
                    totals[0] += row[offset]
                    totals[1] += row[offset + 1]
                    totals[2] += row[offset + 2]
                    totals[3] += row[offset + 3]
            line += bytes(int(round(total / block)) for total in totals)
        out.append(line)
    return out if stride else out


def write_png(path, size, rows):
    raw = b"".join(b"\x00" + bytes(row) for row in rows)

    def chunk(tag, payload):
        body = tag + payload
        return struct.pack(">I", len(payload)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

    header = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    data = (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", header)
            + chunk(b"IDAT", zlib.compress(raw, 9))
            + chunk(b"IEND", b""))
    with open(path, "wb") as handle:
        handle.write(data)


def main():
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    source = sys.argv[1]
    size = 256
    if "--size" in sys.argv:
        size = int(sys.argv[sys.argv.index("--size") + 1])

    width, height, channels, rows = read_png(source)
    if width != height:
        raise SystemExit(f"icon art must be square, got {width}x{height}")

    out_rows = resize(width, height, channels, rows, size)
    write_png(OUT, size, out_rows)
    print(f"{source} ({width}x{height}) -> {OUT} ({size}x{size}, {os.path.getsize(OUT)} bytes)")


if __name__ == "__main__":
    main()
