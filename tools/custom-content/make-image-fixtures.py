"""Reference outputs of HD2Runtime's own icon and archive tools, for ModBuilder's C# port (CustomIconCompiler, ResourceArchive).

    py -3 -B tools/custom-content/make-image-fixtures.py --sdk ../HD2Runtime/sdk

Writes HD2RuntimeGUI.Tests/Fixtures/custom-content/: a few small PNGs in every colour type the decoder handles, and
image-reference.json with the SHA-256 of what the SDK makes of each (decoded pixels, prepared masks, the BC1 mip chain,
the texture header) and of the GUI material and one complete resource archive. The tests compile the same PNGs in C#
and compare. Nothing here is shipped with ModBuilder.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import struct
import sys
import zlib


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def chunk(kind: bytes, body: bytes) -> bytes:
    return struct.pack('>I', len(body)) + kind + body + struct.pack('>I', zlib.crc32(kind + body))


def png(width, height, depth, colour, rows, palette=None, trns=None, filters=None) -> bytes:
    """A PNG from raw scanlines (each without its filter byte). filters cycles the row filter types (each scanline is
    encoded with its filter), so every filter is exercised."""
    bpp = max(1, ({0: 1, 2: 3, 3: 1, 4: 2, 6: 4}[colour] * depth) // 8)
    out, previous = bytearray(), bytes(len(rows[0]))
    for y, line in enumerate(rows):
        kind = (filters or [0])[y % len(filters or [0])]
        enc = bytearray(len(line))
        for i in range(len(line)):
            a = line[i - bpp] if i >= bpp else 0
            b = previous[i]
            c = previous[i - bpp] if i >= bpp else 0
            if kind == 0:
                p = 0
            elif kind == 1:
                p = a
            elif kind == 2:
                p = b
            elif kind == 3:
                p = (a + b) // 2
            else:
                q = a + b - c
                pa, pb, pc = abs(q - a), abs(q - b), abs(q - c)
                p = a if pa <= pb and pa <= pc else b if pb <= pc else c
            enc[i] = (line[i] - p) & 255
        out += bytes([kind]) + enc
        previous = line
    data = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, depth, colour, 0, 0, 0))
    if palette:
        data += chunk(b'PLTE', palette)
    if trns:
        data += chunk(b'tRNS', trns)
    return data + chunk(b'IDAT', zlib.compress(bytes(out), 9)) + chunk(b'IEND', b'')


def emblem(x, y):
    """A deterministic test icon: a red ring and a white chevron on black (each 0 or 1)."""
    dx, dy = x - 127.5, y - 127.5
    r = (dx * dx + dy * dy) ** 0.5
    red = 1 if 96 <= r <= 116 else 0
    white = 1 if abs(dx) < 70 and abs(dx) * 0.9 + 40 <= dy + 70 <= abs(dx) * 0.9 + 72 else 0
    return red, white


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument('--sdk', type=Path, required=True)
    parser.add_argument('--out', type=Path,
        default=Path(__file__).resolve().parents[2] / 'HD2RuntimeGUI.Tests' / 'Fixtures' / 'custom-content')
    args = parser.parse_args()
    sys.path.insert(0, str(args.sdk.resolve()))
    from tools import hd2_image as I
    from tools import hd2_archive as A
    args.out.mkdir(parents=True, exist_ok=True)

    images = {}
    # 1. A mask (opaque, R and G art on black): used as given.
    mask = bytearray()
    for y in range(256):
        for x in range(256):
            red, white = emblem(x, y)
            mask += bytes((red * 255, white * 255, 0, 255))
    images['mask'] = I.write_png(256, 256, bytes(mask))
    # 2. Red-and-white art on a transparent background, with soft edges: converted to masks.
    art = bytearray()
    for y in range(256):
        for x in range(256):
            red, white = emblem(x, y)
            edge = ((x * 7 + y * 13) % 5) * 40
            if white:
                art += bytes((255, 255, 238, 255))
            elif red:
                art += bytes((220, 100, 85, 255 - edge))
            else:
                art += bytes((0, 0, 0, (x + y) % 3 * 20))
    images['art'] = I.write_png(256, 256, bytes(art))
    # 3. Every row filter, RGB 8-bit (no alpha).
    rows = []
    for y in range(256):
        line = bytearray()
        for x in range(256):
            line += bytes(((x * 3 + y) & 255, (x ^ y) & 255, (y * 5) & 255))
        rows.append(bytes(line))
    images['rgb_filters'] = png(256, 256, 8, 2, rows, filters=[0, 1, 2, 3, 4])
    # 4. Grey 8-bit with alpha, 16-bit RGBA, a 4-bit palette with tRNS and 2-bit grey (decoder coverage; small sizes).
    rows = [b''.join(bytes((x * 16, 255 - y * 8)) for x in range(16)) for y in range(16)]
    images['grey_alpha'] = png(16, 16, 8, 4, rows, filters=[4, 3])
    rows = [b''.join(struct.pack('>HHHH', x * 8000, y * 8000, 1234, 65535 - x * 100) for x in range(8)) for y in range(8)]
    images['rgba16'] = png(8, 8, 16, 6, rows, filters=[1, 2])
    palette = b''.join(bytes((i * 16, 255 - i * 16, (i * 37) & 255)) for i in range(16))
    rows = [bytes((((2 * x) % 16) << 4) | ((2 * x + 1 + y) % 16) for x in range(8)) for y in range(16)]
    images['palette4'] = png(16, 16, 4, 3, rows, palette=palette, trns=bytes([0, 128, 255]), filters=[0, 2])
    rows = [bytes((((x * 4 + y) & 3) << 6) | (((x * 4 + y + 1) & 3) << 4) | (((x * 4 + y + 2) & 3) << 2) | ((x * 4 + y + 3) & 3)
        for x in range(4)) for y in range(16)]
    images['grey2'] = png(16, 16, 2, 0, rows)

    reference = {'images': {}}
    for name, data in images.items():
        (args.out / (name + '.png')).write_bytes(data)
        width, height, rgba = I.read_png(data)
        item = {'width': width, 'height': height, 'rgba': sha(rgba)}
        if (width, height) == (256, 256):
            for mode in ('auto', 'raw'):
                prepared, label = I.prepare_icon(data, mode)
                main, gpu = I.icon_texture(data, mode)
                item[mode] = {'label': label, 'prepared': sha(prepared), 'main': sha(main), 'gpu': sha(gpu), 'gpuLength': len(gpu)}
        reference['images'][name] = item
    resource = 'mods/tests/custom_content'
    texture_name = I.image_name(resource, 'mask')
    name_hash = A.resource_hash(texture_name)
    reference['material'] = {'name': texture_name, 'hash': str(name_hash), 'sha256': sha(I.icon_material(name_hash))}
    lua = b'-- test addon\nreturn true\n'
    record = b'return {}\n'
    resources = {
        (A.LUA_TYPE, A.resource_hash(resource)): (A.lua_resource(lua), b''),
        (A.LUA_TYPE, A.resource_hash(resource + '/hd2runtime_images')): (A.lua_resource(record), b''),
    }
    for image in ('mask', 'art'):
        key = A.resource_hash(I.image_name(resource, image))
        main, gpu = I.icon_texture(images[image], 'auto')
        resources[(I.TEXTURE_TYPE, key)] = (main, gpu)
        resources[(I.MATERIAL_TYPE, key)] = (I.icon_material(key), b'')
    archive, gpu = A.make_resource_archive(resources)
    reference['archive'] = {'resource': resource, 'lua': lua.decode(), 'record': record.decode(), 'images': ['mask', 'art'],
        'sha256': sha(archive), 'gpuSha256': sha(gpu), 'length': len(archive), 'gpuLength': len(gpu)}
    version = json.loads((args.sdk / 'metadata.json').read_text(encoding='utf-8'))['runtime_version']
    reference['source'] = 'HD2Runtime ' + version + ' sdk/tools (hd2_image.py, hd2_archive.py)'
    (args.out / 'image-reference.json').write_text(json.dumps(reference, indent=1) + '\n', encoding='utf-8')
    print('wrote', len(images), 'images and image-reference.json to', args.out)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
