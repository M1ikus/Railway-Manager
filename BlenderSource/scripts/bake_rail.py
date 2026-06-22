"""
bake_rail.py — wypala shader szyny S49 (Rail.blend) do tekstur PNG dla Unity URP.

Mapy: Albedo (rdza + jasny pasek toczny), Normal, oraz spakowana MetallicSmoothness
(R=metallic, A=smoothness) — bo URP Lit czyta smoothness z alpha Metallic Map.
Pasek toczny: metallic 1 + smoothness ~0.93 (lustrzany); rdza: metallic 0 + smoothness ~0.15.

UŻYCIE: blender --background --factory-startup --python BlenderSource/scripts/bake_rail.py
Wyjście: Assets/Textures/Trackwork/Rail_Albedo|Normal|MetallicSmoothness.png
"""

import bpy, os
import numpy as np

PROJECT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))) \
    if "__file__" in globals() else r"D:\Gry\RM-0.2"
BLEND = os.path.join(PROJECT, "BlenderSource", "Trackwork", "Rail.blend")
TEX = os.path.join(PROJECT, "Assets", "Textures", "Trackwork")
os.makedirs(TEX, exist_ok=True)
RES = 1024

bpy.ops.wm.open_mainfile(filepath=BLEND)
obj = bpy.data.objects["Rail"]
bpy.ops.object.select_all(action='DESELECT')
obj.select_set(True)
bpy.context.view_layer.objects.active = obj

sc = bpy.context.scene
sc.render.engine = 'CYCLES'
sc.cycles.device = 'CPU'
sc.cycles.samples = 24
sc.render.bake.margin = 8
sc.render.bake.use_selected_to_active = False

mat = obj.material_slots[0].material
nt = mat.node_tree
bsdf = nt.nodes.get("Principled BSDF")


def bake_img(name, btype, colorspace, pass_filter=None, alpha=False):
    img = bpy.data.images.new(name, RES, RES, alpha=alpha)
    img.colorspace_settings.name = colorspace
    node = nt.nodes.new('ShaderNodeTexImage')
    node.image = img
    nt.nodes.active = node
    if pass_filter:
        bpy.ops.object.bake(type=btype, pass_filter=pass_filter)
    else:
        bpy.ops.object.bake(type=btype)
    nt.nodes.remove(node)
    return img


def save(img, name):
    img.filepath_raw = os.path.join(TEX, name + ".png")
    img.file_format = 'PNG'
    img.save()
    print(f"[bake_rail] {name}.png")


# 1. Albedo
alb = bake_img("Rail_Albedo", 'DIFFUSE', 'sRGB', pass_filter={'COLOR'})
save(alb, "Rail_Albedo")

# 2. Normal
nrm = bake_img("Rail_Normal", 'NORMAL', 'Non-Color')
save(nrm, "Rail_Normal")

# 3a. Roughness (do późniejszej inwersji na smoothness)
rgh = bake_img("_rail_rough", 'ROUGHNESS', 'Non-Color')

# 3b. Metallic przez EMIT (podłącz źródło metallic -> Emission, baker emit)
met_in = bsdf.inputs["Metallic"]
src = met_in.links[0].from_socket if met_in.links else None
emis_str = bsdf.inputs["Emission Strength"]
old_str = emis_str.default_value
emis_str.default_value = 1.0
emis_link = None
if src:
    emis_link = nt.links.new(src, bsdf.inputs["Emission Color"])
met = bake_img("_rail_metallic", 'EMIT', 'Non-Color')
emis_str.default_value = old_str
if emis_link:
    nt.links.remove(emis_link)

# 3c. Pack: R=G=B=metallic, A=smoothness(=1-roughness)
mp = np.array(met.pixels[:]).reshape(-1, 4)
rp = np.array(rgh.pixels[:]).reshape(-1, 4)
out = np.zeros_like(mp)
mval = mp[:, 0]
sval = 1.0 - rp[:, 0]
out[:, 0] = mval
out[:, 1] = mval
out[:, 2] = mval
out[:, 3] = sval
packed = bpy.data.images.new("Rail_MetallicSmoothness", RES, RES, alpha=True)
packed.colorspace_settings.name = 'Non-Color'
packed.pixels = out.flatten().tolist()
save(packed, "Rail_MetallicSmoothness")

print("[bake_rail] DONE ->", TEX)
