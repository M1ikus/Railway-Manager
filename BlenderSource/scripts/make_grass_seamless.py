"""make_grass_seamless.py — uczyń Grass_Albedo.png tileable (seamless) metodą offset+feather.

Roll o pół obrazu (stary szew ląduje w środku) + feather na krawędziach do wersji rolled
(której krawędzie są ciągłe). Dla jednorodnej trawy ghost w pierścieniu jest praktycznie
niewidoczny, a krawędzie kafla są seamless. Nadpisuje plik.

UŻYCIE: blender --background --factory-startup --python BlenderSource/scripts/make_grass_seamless.py
"""
import bpy, numpy as np, os

p = os.path.join(r"D:\Gry\RM-0.2", "Assets", "Textures", "Trackwork", "Grass_Albedo.png")
img = bpy.data.images.load(p)
W, H = img.size
arr = np.array(img.pixels[:], dtype=np.float32).reshape(H, W, 4)
rgb = arr[:, :, :3]

roll = np.roll(rgb, (H // 2, W // 2), axis=(0, 1))
fx, fy = W // 8, H // 8
xx = np.minimum(np.arange(W), W - 1 - np.arange(W))
yy = np.minimum(np.arange(H), H - 1 - np.arange(H))
mx = np.clip(xx / fx, 0.0, 1.0)
my = np.clip(yy / fy, 0.0, 1.0)
m = np.minimum(mx[None, :], my[:, None])[..., None]   # 1 w środku, 0 na krawędziach

out = rgb * m + roll * (1.0 - m)
arr[:, :, :3] = out
arr[:, :, 3] = 1.0

img.pixels = arr.flatten()
img.filepath_raw = p
img.file_format = 'PNG'
img.save()
print("SEAMLESS SAVED:", p)
