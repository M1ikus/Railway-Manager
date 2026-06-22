"""match_grass_color.py — dopasuj średni kolor Grass_Albedo.png DOKŁADNIE do ref_grass.png
(per-kanałowy gain w przestrzeni linear). Po seamless. Nie psuje kafla (uniform multiply).
"""
import bpy, numpy as np, os

PROJECT = r"D:\Gry\RM-0.2"
ref_path = os.path.join(PROJECT, "BlenderSource", "Trackwork", "ref_grass.png")
grass_path = os.path.join(PROJECT, "Assets", "Textures", "Trackwork", "Grass_Albedo.png")


def s2l(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def l2s(c):
    c = np.clip(c, 0, None)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - 0.055)


def load(path):
    im = bpy.data.images.load(path)
    try:
        im.colorspace_settings.name = 'Non-Color'
    except Exception as e:
        print("cs:", e)
    W, H = im.size
    a = np.array(im.pixels[:], dtype=np.float64).reshape(H, W, 4)
    return im, a, W, H


def center_lin_mean(a, W, H):
    c = a[int(H * 0.30):int(H * 0.70), int(W * 0.30):int(W * 0.70), :3]
    return s2l(c.reshape(-1, 3)).mean(axis=0)


_, ra, RW, RH = load(ref_path)
tgt = center_lin_mean(ra, RW, RH)
im, ma, MW, MH = load(grass_path)
cur = center_lin_mean(ma, MW, MH)
gain = tgt / np.maximum(cur, 1e-6)
print(f"TARGET lin = {tgt}")
print(f"MINE   lin = {cur}")
print(f"GAIN       = {gain}")

ml = s2l(ma[:, :, :3]) * gain
out = ma.copy()
out[:, :, :3] = l2s(np.clip(ml, 0, 1))
out[:, :, 3] = 1.0
im.pixels = out.flatten()
im.filepath_raw = grass_path
im.file_format = 'PNG'
im.save()
print("COLOR-MATCHED SAVED:", grass_path)
