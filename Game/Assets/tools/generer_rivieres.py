"""T-001 : export du système de terrain natif en PNG RGBA, sans dépendance.

Palette assortie à TerrainPainter ; pixels entiers, alpha binaire, aucun lissage.
Relancer depuis la racine : python Game/Assets/tools/generer_rivieres.py
"""
from pathlib import Path
import math
import struct
import zlib

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "terrain"
EAU = (79, 141, 145, 255)
PROFOND = (61, 114, 136, 255)
CLAIR = (123, 177, 161, 255)
ECUME = (193, 216, 194, 255)
TERRE = (99, 108, 78, 255)
MOUSSE = (119, 139, 94, 255)
ROCHE = (153, 165, 152, 255)
OMBRE = (83, 104, 99, 255)
VIDE = (0, 0, 0, 0)


def png(path, pixels, width=32, height=32):
    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data))
    raw = b''.join(b'\0' + bytes(v for p in pixels[y*width:(y+1)*width] for v in p) for y in range(height))
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 6, 0, 0, 0))
                     + chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b''))


def distance(x, y, mask):
    dx, dy = abs(x - 15.5), abs(y - 15.5)
    d = math.hypot(dx, dy)
    if (mask & 1 and y < 16) or (mask & 4 and y >= 16): d = min(d, dx)
    if (mask & 2 and x >= 16) or (mask & 8 and x < 16): d = min(d, dy)
    return d


def river(mask):
    pixels = []
    for y in range(32):
        for x in range(32):
            d = distance(x, y, mask)
            # Bords stables à 20 pixels de large, centre irrégulier mais discret.
            width = 10.5 + (0.5 if 4 < x < 27 and 4 < y < 27 and (x+y)//5 % 3 == 0 else 0)
            c = VIDE
            if d < width + 2: c = TERRE
            if width + 0.5 <= d < width + 1.5 and (x+y) % 5 < 3: c = MOUSSE
            if d < width: c = CLAIR
            if d < width - 2: c = EAU
            if d < 4 and (x//8 + y//7) % 4 != 0: c = PROFOND
            # Petits traits discontinus, pas de bruit ni de flou.
            if d < width - 3 and y % 11 == 4 and 5 <= x % 13 <= 8: c = CLAIR
            if width - 1 <= d < width and (x+y) % 9 < 3: c = ECUME
            pixels.append(c)
    return pixels


def rotate(pixels, direction):
    turns = {4: 0, 8: 1, 1: 2, 2: 3}[direction]
    for _ in range(turns):
        pixels = [pixels[(31-x)*32+y] for y in range(32) for x in range(32)]
    return pixels


def accent(kind):
    p = [VIDE] * 1024
    for y in range(32):
        for x in range(32):
            c = VIDE
            if kind == 'end':
                # Source rocheuse en demi-cercle ; ouverture vers le sud.
                r = math.hypot((x-15.5), (y-12)*1.25)
                if 7 < r < 12 and y < 19:
                    c = ROCHE if (x+y)//4 % 3 else OMBRE
                    if y < 8: c = (180, 187, 164, 255)
                if 10 <= x <= 21 and 11 <= y <= 17:
                    c = OMBRE if y < 14 else PROFOND
                if 13 <= x <= 18 and y in (15, 18): c = ECUME
            else:
                # Seuil et nappe d'eau sur la moitié aval, écume au pied.
                if 6 <= x <= 25 and 17 <= y <= 25:
                    c = OMBRE if y == 17 else (91, 151, 157, 255)
                    if x % 5 in (0, 1) and y > 18: c = CLAIR
                    if x % 7 == 2 and 19 <= y <= 23: c = ECUME
                if 7 <= x <= 24 and y in (25, 26):
                    c = ECUME if (x+y) % 4 else CLAIR
            p[y*32+x] = c
    return p


def lake(mask):
    pixels = []
    for y in range(32):
        for x in range(32):
            edges = []
            if mask & 1: edges.append(y)
            if mask & 2: edges.append(31-x)
            if mask & 4: edges.append(31-y)
            if mask & 8: edges.append(x)
            d = min(edges, default=32)
            c = VIDE
            if d == 0: c = MOUSSE
            if d == 1: c = TERRE
            if d == 2: c = OMBRE
            if d == 3: c = CLAIR
            if d == 4 and (x+y) % 7 < 4: c = ECUME
            pixels.append(c)
    return pixels


def main():
    tiles = {}
    for mask in range(16):
        tiles[f'river_{mask}'] = river(mask)
        tiles[f'lake_edge_{mask}'] = lake(mask)
    for direction in (1, 2, 4, 8):
        for kind in ('end', 'fall'):
            tiles[f'river_{kind}_{direction}'] = rotate(accent(kind), direction)
    for name, pixels in tiles.items():
        png(OUT / (name + '.png'), pixels)
    # Contrôle du contrat : chaque sortie est un PNG RGBA 8 bits de 32 × 32.
    for name in tiles:
        data = (OUT / (name + '.png')).read_bytes()
        assert struct.unpack('>IIBB', data[16:26]) == (32, 32, 8, 6), name
    # Les 16 masques ouvrent exactement les bords annoncés, sans sortie parasite.
    for mask in range(16):
        tile = tiles[f'river_{mask}']
        for bit, index in ((1, 15), (2, 15*32+31), (4, 31*32+15), (8, 15*32)):
            assert bool(tile[index][3]) == bool(mask & bit), (mask, bit)
    print('40 tuiles RGBA 32 × 32 vérifiées ; raccords des 16 masques conformes.')


if __name__ == '__main__':
    main()
