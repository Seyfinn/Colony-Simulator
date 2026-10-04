"""Vérifie les noms, dimensions et formats des livraisons T-002/004/005/007/008 et T-023/024/025."""
from pathlib import Path
import struct

root = Path(__file__).resolve().parents[1]
expected = {"icons/coins.png": (16, 16), "world/river_segment.png": (16, 8),
            "world/map_background.png": (1024, 640)}
for good in ("food", "grain", "wood", "stone", "ironore", "charcoal", "iron", "tools", "flour", "bread", "beer"):
    expected[f"icons/{good}.png"] = (16, 16)
for building in ("hut", "kiln", "bloomery", "forge", "mill", "oven", "cask", "cask_brewing", "cask_ready"):
    expected[f"buildings/{building}.png"] = (64, 80)
for building in ("dam", "dam_side"):
    expected[f"buildings/{building}.png"] = (32, 48)
for side in ("", "side_"):
    for frame in range(3):
        expected[f"buildings/dam_construction_{side}{frame}.png"] = (32, 48)
for frame in range(4):
    expected[f"buildings/mill_wheel_{frame}.png"] = (24, 40)
for people in ("human", "dwarf", "elf", "orc"):
    expected[f"world/colony_{people}.png"] = (32, 32)
    for frame in range(4):
        expected[f"peoples/trader_{people}_{frame}.png"] = (32, 32)
for name, size in expected.items():
    data = (root / name).read_bytes()
    assert data[:8] == b"\x89PNG\r\n\x1a\n", name
    w, h, depth, color = struct.unpack(">IIBB", data[16:26])
    assert (w, h) == size and (depth, color) == (8, 6), (name, w, h, depth, color)
for pattern, count in [("buildings/mill_wheel_{}.png", 4),
                       ("buildings/dam_construction_{}.png", 3),
                       ("buildings/dam_construction_side_{}.png", 3)]:
    assert len({(root / pattern.format(i)).read_bytes() for i in range(count)}) == count, pattern
for people in ("human", "dwarf", "elf", "orc"):
    assert len({(root / f"peoples/trader_{people}_{i}.png").read_bytes() for i in range(4)}) == 4, people
assert len({(root / f"buildings/{name}.png").read_bytes() for name in ("cask", "cask_brewing", "cask_ready")}) == 3, "États du fût"
print(f"{len(expected)} PNG vérifiés : noms du catalogue, dimensions exactes, RGBA 8 bits ; étapes, roue et états du fût distincts.")
