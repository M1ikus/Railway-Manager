"""analyze_ref_grass.py — numeryczna analiza referencji trawy (pixel-level).
Wyciąga: średni/medianowy kolor (sRGB + linear), jasność, saturację, kontrast (percentyle),
winietę (góra vs środek vs dół), dominującą skalę detalu (FFT -> długość fali w px).

UŻYCIE: blender --background --factory-startup --python BlenderSource/scripts/analyze_ref_grass.py
"""
import bpy, numpy as np, os, sys

p = os.path.join(r"D:\Gry\RM-0.2", "BlenderSource", "Trackwork", "ref_grass.png")
if "--" in sys.argv:
    _ex = sys.argv[sys.argv.index("--") + 1:]
    if _ex:
        p = _ex[0]
img = bpy.data.images.load(p)
try:
    img.colorspace_settings.name = 'Non-Color'   # czytaj surowe (sRGB-encoded) wartości display
except Exception as e:
    print("colorspace:", e)
W, H = img.size
a = np.array(img.pixels[:], dtype=np.float64).reshape(H, W, 4)[:, :, :3]
a = a[::-1]   # Blender: wiersze od dołu -> odwróć na orientację obrazu (góra=0)
srgb255 = np.clip(a, 0, 1) * 255.0


def s2l(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def stats(block, name):
    m = block.reshape(-1, 3).mean(axis=0)
    med = np.median(block.reshape(-1, 3), axis=0)
    print(f"[{name}] sRGB mean=({m[0]*255:.0f},{m[1]*255:.0f},{m[2]*255:.0f}) "
          f"median=({med[0]*255:.0f},{med[1]*255:.0f},{med[2]*255:.0f})")
    lin = s2l(block).reshape(-1, 3).mean(axis=0)
    print(f"        linear mean=({lin[0]:.4f},{lin[1]:.4f},{lin[2]:.4f})")
    return m


full = np.clip(a, 0, 1)
print(f"SIZE: {W}x{H}")
stats(full, "CALY")
# centralny crop (reprezentatywny kolor trawy, bez winiety brzegów)
cy0, cy1 = int(H*0.35), int(H*0.75)
cx0, cx1 = int(W*0.30), int(W*0.70)
center = full[cy0:cy1, cx0:cx1]
mc = stats(center, "SRODEK")

# luminancja + kontrast (na linear)
lin_full = s2l(full)
lum = 0.2126*lin_full[:, :, 0] + 0.7152*lin_full[:, :, 1] + 0.0722*lin_full[:, :, 2]
print(f"LUMINANCJA linear: mean={lum.mean():.4f} std={lum.std():.4f} "
      f"p5={np.percentile(lum,5):.4f} p50={np.percentile(lum,50):.4f} p95={np.percentile(lum,95):.4f}")

# saturacja (HSV) na sRGB
mx = full.max(axis=2); mn = full.min(axis=2)
sat = np.where(mx > 1e-6, (mx-mn)/mx, 0)
print(f"SATURACJA(HSV) mean={sat.mean():.3f} p50={np.median(sat):.3f}")

# winieta: dół vs środek vs góra (luminancja)
band = H // 6
print(f"WINIETA lum: gora={lum[:band].mean():.4f} srodek={lum[H//2-band//2:H//2+band//2].mean():.4f} dol={lum[-band:].mean():.4f}")

# dominująca skala detalu (FFT na luminancji centralnego cropu, high-pass przez odjęcie średniej)
c = lum[cy0:cy1, cx0:cx1]
c = c - c.mean()
F = np.abs(np.fft.fftshift(np.fft.fft2(c)))
h, w = c.shape
yy, xx = np.indices((h, w))
r = np.sqrt((yy-h/2)**2 + (xx-w/2)**2).astype(int)
rad = np.bincount(r.ravel(), F.ravel()) / np.maximum(np.bincount(r.ravel()), 1)
rad[0:3] = 0   # usuń DC/bardzo niskie
peak = int(np.argmax(rad[:min(h, w)//2]))
if peak > 0:
    wavelength = min(h, w) / peak
    print(f"DETAL (FFT): peak radius={peak} -> dominująca długość fali ~{wavelength:.1f} px "
          f"(crop {w}x{h}); czyli smuga/detal ~{wavelength:.0f}px szer.")
print("DONE")
