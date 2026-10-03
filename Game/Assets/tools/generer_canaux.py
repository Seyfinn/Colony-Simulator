"""T-003 : canaux du système de terrain natif, 32 PNG RGBA 32 × 32."""
from pathlib import Path
import math
import struct
import sys
sys.dont_write_bytecode = True
from generer_rivieres import png, EAU, PROFOND, CLAIR, ECUME, VIDE

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'terrain'
LEVEE = (166, 137, 93, 255)
TERRE = (133, 103, 71, 255)
PAROI = (83, 71, 51, 255)
FOND = (113, 87, 62, 255)
FISSURE = (92, 73, 55, 255)


def distance(x, y, mask):
    dx, dy = abs(x-15.5), abs(y-15.5)
    d = math.hypot(dx, dy)
    if (mask & 1 and y < 16) or (mask & 4 and y >= 16): d = min(d, dx)
    if (mask & 2 and x >= 16) or (mask & 8 and x < 16): d = min(d, dy)
    return d


def canal(mask, wet):
    pixels = []
    for y in range(32):
        for x in range(32):
            d = distance(x, y, mask)
            c = VIDE
            if d < 8.5: c = TERRE
            if 7.5 <= d < 8.5: c = LEVEE
            if 5.5 <= d < 6.5: c = PAROI
            if d < 5.5:
                if wet:
                    c = CLAIR if d >= 4 else EAU
                    if d < 2.5: c = PROFOND
                    if y % 11 == 4 and 11 <= x <= 19: c = CLAIR
                    if 4.5 <= d < 5.5 and (x+y) % 11 < 3: c = ECUME
                else:
                    c = FOND
                    if (x*3+y*5) % 29 == 0: c = TERRE
                    if y % 13 == 7 and 13 <= x <= 17: c = FISSURE
            pixels.append(c)
    return pixels


def main():
    tiles = {}
    for wet in (False, True):
        for mask in range(16):
            name = f'canal_{"wet" if wet else "dry"}_{mask}'
            tile = tiles[name] = canal(mask, wet)
            path = OUT / (name + '.png')
            png(path, tile)
            data = path.read_bytes()
            assert struct.unpack('>IIBB', data[16:26]) == (32, 32, 8, 6)
            for bit, index in ((1, 15), (2, 511), (4, 1007), (8, 480)):
                assert bool(tile[index][3]) == bool(mask & bit), (name, bit)
            assert tile[0] == VIDE and tile[1023] == VIDE
    # Raccords sans changement de largeur, y compris entre le fossé sec et rempli.
    for state in ('dry', 'wet'):
        for mask in range(16):
            t = tiles[f'canal_{state}_{mask}']
            if mask & 2:
                assert [t[y*32+31][3] for y in range(32)] == [tiles[f'canal_{state}_8'][y*32][3] for y in range(32)]
            if mask & 4:
                assert [t[31*32+x][3] for x in range(32)] == [tiles[f'canal_{state}_1'][x][3] for x in range(32)]
    # Planche ×4, rangées sèches puis en eau, masques 0 à 15 dans chaque groupe.
    ordered = list(tiles.values())
    preview = []
    for y in range(144):
        for x in range(288):
            index, lx, ly = (y//36)*8+x//36, x%36-2, y%36-2
            c = (48, 64, 59, 255)
            if 0 <= lx < 32 and 0 <= ly < 32:
                c = ordered[index][ly*32+lx]
                if c[3] == 0: c = (119, 151, 94, 255)
            preview.append(c)
    enlarged = [preview[(y//4)*288+x//4] for y in range(576) for x in range(1152)]
    png(ROOT/'validation/t003_catalogue.png', enlarged, 1152, 576)
    print('32 canaux RGBA 32 × 32 : formats, ouvertures et raccords vérifiés.')


if __name__ == '__main__':
    main()
