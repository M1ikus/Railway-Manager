"""calc_basecolor.py — policz dokładny _BaseColor, by kolor trawy w grze = referencja.
in_game = tekstura * _BaseColor_cur * swiatlo  (tekstura ~= ref, bo color-matched)
chcemy in_game = ref  =>  _BaseColor_new = _BaseColor_cur * (ref / in_game)  per-kanał.
"""
import bpy, numpy as np, os

PROJECT = r"D:\Gry\RM-0.2"
ref_path = os.path.join(PROJECT, "BlenderSource", "Trackwork", "ref_grass.png")
ingame_path = os.path.join(PROJECT, "BlenderSource", "Trackwork", "ingame_grass.png")
CUR_BASECOLOR = 0.5   # aktualne _BaseColor w LushGrass_Light.mat


def s2l(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def center_lin(path):
    im = bpy.data.images.load(path)
    try:
        im.colorspace_settings.name = 'Non-Color'
    except Exception:
        pass
    W, H = im.size
    a = np.array(im.pixels[:], dtype=np.float64).reshape(H, W, 4)[:, :, :3]
    c = a[int(H * 0.30):int(H * 0.70), int(W * 0.30):int(W * 0.70)]
    return s2l(c.reshape(-1, 3)).mean(axis=0), (W, H)


ref, rs = center_lin(ref_path)
ing, gs = center_lin(ingame_path)
print(f"REF    lin = ({ref[0]:.4f},{ref[1]:.4f},{ref[2]:.4f})  size {rs}")
print(f"INGAME lin = ({ing[0]:.4f},{ing[1]:.4f},{ing[2]:.4f})  size {gs}")
ratio = ref / np.maximum(ing, 1e-6)
print(f"RATIO ref/ingame = ({ratio[0]:.3f},{ratio[1]:.3f},{ratio[2]:.3f})  (ingame jasniejszy o ~{1/ratio.mean():.2f}x)")
new_base = np.clip(CUR_BASECOLOR * ratio, 0.0, 2.0)
print(f"NOWY _BaseColor = ({new_base[0]:.3f}, {new_base[1]:.3f}, {new_base[2]:.3f})")
