"""
bake_sleeper.py — wypala proceduralny shader podkładu (Sleeper.blend) do tekstur PNG dla Unity.

Procedural shader Blendera NIE eksportuje się do FBX. Ten skrypt: UV unwrap + Cycles bake
3 map (albedo / normal / roughness) na wspólny atlas (oba materiały wood+metal) → PNG.
W Unity: podłączyć do materiału URP/Lit (Base Map + Normal Map + smoothness).

UŻYCIE: blender --background --factory-startup --python BlenderSource/scripts/bake_sleeper.py
Wyjście: Assets/Textures/Trackwork/Sleeper_Albedo|Normal|Roughness.png
"""

import bpy, os

PROJECT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))) \
    if "__file__" in globals() else r"D:\Gry\RM-0.2"
BLEND = os.path.join(PROJECT, "BlenderSource", "Trackwork", "Sleeper.blend")
TEX = os.path.join(PROJECT, "Assets", "Textures", "Trackwork")
os.makedirs(TEX, exist_ok=True)
RES = 1024

bpy.ops.wm.open_mainfile(filepath=BLEND)
obj = bpy.data.objects["Sleeper"]
bpy.ops.object.select_all(action='DESELECT')
obj.select_set(True)
bpy.context.view_layer.objects.active = obj

# UV już w .blend (zrobione w gen_sleeper.py — to samo co w FBX, spójne)
sc = bpy.context.scene
sc.render.engine = 'CYCLES'
sc.cycles.device = 'CPU'
sc.cycles.samples = 24
sc.render.bake.margin = 8
sc.render.bake.use_selected_to_active = False


def bake_map(name, btype, colorspace, pass_filter=None):
    img = bpy.data.images.new(name, RES, RES)
    img.colorspace_settings.name = colorspace
    added = []
    for slot in obj.material_slots:
        if not slot.material:
            continue
        nt = slot.material.node_tree
        node = nt.nodes.new('ShaderNodeTexImage')
        node.image = img
        nt.nodes.active = node
        added.append((nt, node))
    if pass_filter:
        bpy.ops.object.bake(type=btype, pass_filter=pass_filter)
    else:
        bpy.ops.object.bake(type=btype)
    img.filepath_raw = os.path.join(TEX, name + ".png")
    img.file_format = 'PNG'
    img.save()
    for nt, node in added:
        nt.nodes.remove(node)
    print(f"[bake] {name}.png")


bake_map("Sleeper_Albedo", 'DIFFUSE', 'sRGB', pass_filter={'COLOR'})
bake_map("Sleeper_Normal", 'NORMAL', 'Non-Color')
bake_map("Sleeper_Roughness", 'ROUGHNESS', 'Non-Color')
print("[bake] DONE ->", TEX)
