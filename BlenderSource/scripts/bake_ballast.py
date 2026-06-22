"""
bake_ballast.py — wypala shader tłucznia (Ballast.blend) do tekstur tile-able PNG dla Unity.

Mapy: Albedo (granit/bazalt paleta reference), Normal (relief kamieni z displacement->bump),
MetallicSmoothness (metallic 0 = kamień, A=smoothness z per-cell roughness — część kamieni błyszczy).
UV plane 0-1 = tile 2m (powtarzany w grze na nasypie).

UŻYCIE: blender --background --factory-startup --python BlenderSource/scripts/bake_ballast.py
Wyjście: Assets/Textures/Trackwork/Ballast_Albedo|Normal|MetallicSmoothness.png
"""

import bpy, os
import numpy as np

PROJECT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))) \
    if "__file__" in globals() else r"D:\Gry\RM-0.2"
BLEND = os.path.join(PROJECT, "BlenderSource", "Trackwork", "Ballast.blend")
TEX = os.path.join(PROJECT, "Assets", "Textures", "Trackwork")
os.makedirs(TEX, exist_ok=True)
RES = 1024

bpy.ops.wm.open_mainfile(filepath=BLEND)
obj = next(o for o in bpy.data.objects if o.type == 'MESH')
bpy.ops.object.select_all(action='DESELECT')
obj.select_set(True)
bpy.context.view_layer.objects.active = obj
# bake na stałym mesh (wyłącz adaptive render-time displacement)
if hasattr(obj, "cycles"):
    obj.cycles.use_adaptive_subdivision = False

sc = bpy.context.scene
sc.render.engine = 'CYCLES'
sc.cycles.device = 'CPU'
sc.cycles.samples = 24
sc.render.bake.margin = 8

mat = obj.material_slots[0].material
nt = mat.node_tree
bsdf = nt.nodes.get("Principled BSDF")

# Relief kamieni: weź Height z węzła Displacement i podłącz przez Bump do BSDF Normal
# (bake NORMAL łapie bump, nie material-displacement) — daje normal map kamieni na płaski tile.
disp = next((n for n in nt.nodes if n.type == 'DISPLACEMENT'), None)
if disp and disp.inputs["Height"].links:
    height_src = disp.inputs["Height"].links[0].from_socket
    bump = nt.nodes.new('ShaderNodeBump')
    bump.inputs["Strength"].default_value = 0.6
    bump.inputs["Distance"].default_value = 0.02
    nt.links.new(height_src, bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])


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
    print(f"[bake_ballast] {name}.png")


alb = bake_img("Ballast_Albedo", 'DIFFUSE', 'sRGB', pass_filter={'COLOR'})
save(alb, "Ballast_Albedo")

nrm = bake_img("Ballast_Normal", 'NORMAL', 'Non-Color')
save(nrm, "Ballast_Normal")

rgh = bake_img("_ballast_rough", 'ROUGHNESS', 'Non-Color')
# metallic = 0 (kamień), smoothness = 1 - roughness
rp = np.array(rgh.pixels[:]).reshape(-1, 4)
out = np.zeros_like(rp)
out[:, 0] = 0.0
out[:, 1] = 0.0
out[:, 2] = 0.0
out[:, 3] = 1.0 - rp[:, 0]
packed = bpy.data.images.new("Ballast_MetallicSmoothness", RES, RES, alpha=True)
packed.colorspace_settings.name = 'Non-Color'
packed.pixels = out.flatten().tolist()
save(packed, "Ballast_MetallicSmoothness")

print("[bake_ballast] DONE ->", TEX)
