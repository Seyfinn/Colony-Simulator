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
    for bit, sx, sy in ((16, 1, -1), (32, 1, 1), (64, -1, 1), (128, -1, -1)):
        if mask & bit:
            along = max(0, ((x-15.5)*sx + (y-15.5)*sy)/2)
            d = min(d, math.hypot(x-15.5-along*sx, y-15.5-along*sy))
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


def corner(sx, sy):
    # Quart de berge au point commun des quatre cases, sans coude.
    cx, cy = (31.5 if sx > 0 else -0.5), (31.5 if sy > 0 else -0.5)
    pixels = []
    for y in range(32):
        for x in range(32):
            d = (abs(x-cx) + abs(y-cy))/math.sqrt(2)
            c = VIDE
            if d < 12.5: c = TERRE
            if 11 <= d < 12 and (x+y) % 5 < 3: c = MOUSSE
            if d < 10.5: c = CLAIR
            if d < 8.5: c = EAU
            if d < 4: c = PROFOND
            if 9.5 <= d < 10.5 and (x+y) % 9 < 3: c = ECUME
            pixels.append(c)
    return pixels


def diagonal_accent(kind, sx, sy):
    pixels = []
    for y in range(32):
        for x in range(32):
            along = ((x-15.5)*sx + (y-15.5)*sy)/math.sqrt(2)
            across = ((x-15.5)*sy - (y-15.5)*sx)/math.sqrt(2)
            c = VIDE
            if kind == 'end':
                r = math.hypot(across, (along+4)*1.25)
                if 7 < r < 12 and along < 3:
                    c = ROCHE if (x+y)//4 % 3 else OMBRE
                    if along < -8: c = (180, 187, 164, 255)
                if abs(across) < 5.5 and -4 <= along <= 2: c = PROFOND
                if abs(across) < 2.5 and 0 <= along < 1.5: c = ECUME
            else:
                if abs(across) < 9.5 and 2 <= along < 10:
                    c = OMBRE if along < 3 else CLAIR if int(across) % 5 < 2 else EAU
                if abs(across) < 8.5 and 10 <= along < 12:
                    c = ECUME if (x+y) % 4 else CLAIR
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
    for mask in range(16, 256):
        tiles[f'river_diag_{mask}'] = river(mask)
    for name, sx, sy in (('ne', 1, -1), ('se', 1, 1), ('sw', -1, 1), ('nw', -1, -1)):
        tiles[f'river_corner_{name}'] = corner(sx, sy)
        for kind in ('end', 'fall'):
            tiles[f'river_{kind}_{name}'] = diagonal_accent(kind, sx, sy)
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
    # Les sorties diagonales atteignent leur coin ; aucun raccord par un coude intermédiaire.
    for mask in range(16, 256):
        tile = tiles[f'river_diag_{mask}']
        for bit, index in ((16, 31), (32, 1023), (64, 992), (128, 0)):
            if mask & bit: assert tile[index][3] == 255, (mask, bit)
    # Deux assemblages 2 × 2 : bande d'eau continue de part et d'autre du coin commun.
    # Ce contrôle détecte un débord trop étroit et le retour à un coude sur la case latérale.
    for names, slope in ((('river_diag_32', 'river_corner_sw', 'river_corner_ne', 'river_diag_128'), 1),
                         (('river_corner_se', 'river_diag_64', 'river_diag_16', 'river_corner_nw'), -1)):
        for x in range(15, 49):
            for offset in range(-6, 7):
                y = (x if slope == 1 else 63-x) + offset
                tile = tiles[names[(y//32)*2+x//32]]
                assert tile[(y%32)*32+x%32] in (EAU, PROFOND, CLAIR, ECUME), (names, x, y)
    for name in ('ne', 'se', 'sw', 'nw'):
        assert tiles[f'river_corner_{name}'][16*32+16] == VIDE
    print(f'{len(tiles)} tuiles RGBA 32 × 32 vérifiées ; connexions cardinales et diagonales conformes.')


if __name__ == '__main__':
    main()
