"""make_grass_normal.py — wygeneruj Grass_Normal.png z wysokości źdźbeł (luminancja albedo).
Gradient przez np.roll (wrap => seamless jak albedo). Tangent-space normal (OpenGL Y+).
Jeśli relief w grze wygląda odwrotnie (wgłębienia zamiast wypukłości) -> flipGreenChannel w .meta.
"""
import bpy, numpy as np, os

PROJECT = r"D:\Gry\RM-0.2"
alb = os.path.join(PROJECT, "Assets", "Textures", "Trackwork", "Grass_Albedo.png")
out = os.path.join(PROJECT, "Assets", "Textures", "Trackwork", "Grass_Normal.png")
STRENGTH = 11.0

im = bpy.data.images.load(alb)
try:
    im.colorspace_settings.name = 'Non-Color'
except Exception:
    pass
W, H = im.size
a = np.array(im.pixels[:], dtype=np.float64).reshape(H, W, 4)[:, :, :3]
h = 0.299 * a[:, :, 0] + 0.587 * a[:, :, 1] + 0.114 * a[:, :, 2]   # wysokość = jasność (źdźbła jaśniejsze)

sx = (np.roll(h, -1, axis=1) - np.roll(h, 1, axis=1)) * 0.5 * STRENGTH
sy = (np.roll(h, -1, axis=0) - np.roll(h, 1, axis=0)) * 0.5 * STRENGTH
nx = -sx
ny = -sy
nz = np.ones_like(h)
ln = np.sqrt(nx*nx + ny*ny + nz*nz)
nx /= ln; ny /= ln; nz /= ln

rgb = np.empty((H, W, 4), dtype=np.float64)
rgb[:, :, 0] = nx * 0.5 + 0.5
rgb[:, :, 1] = ny * 0.5 + 0.5
rgb[:, :, 2] = nz * 0.5 + 0.5
rgb[:, :, 3] = 1.0

nim = bpy.data.images.new("Grass_Normal", W, H, alpha=True)
try:
    nim.colorspace_settings.name = 'Non-Color'
except Exception:
    pass
nim.pixels = rgb.flatten()
nim.filepath_raw = out
nim.file_format = 'PNG'
nim.save()
print("NORMAL SAVED:", out)
