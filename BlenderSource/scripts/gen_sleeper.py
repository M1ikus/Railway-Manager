"""
gen_sleeper.py — proceduralny generator podkładu kolejowego (Sleeper) dla Railway Manager.

Wariant: DREWNIANY typ IIB + przytwierdzenie K (decyzja user 2026-05-31).
Dane z workflow-research (PL/PKP, weryfikacja adwersarialna). Wymiary bryły: HIGH
(real handlowy IIB = 2600x260x160 mm). Wymiary węzła K: MEDIUM (stylized przybliżenia
z masy Pm-49 + standardów — research openQuestion #4). Korekty z weryfikacji uwzględnione:
typ IIB (nie '1E1'), podkładka Pm-49, łapki haczykowe K (śrubowe, nie sprężyste SB).

UŻYCIE (headless):
    blender --background --factory-startup --python BlenderSource/scripts/gen_sleeper.py
UWAGA: czyści scenę — uruchamiaj headless / w nowym pliku.
Wyjście: BlenderSource/Trackwork/Sleeper.fbx (+ .blend do podglądu).
"""

import bpy, bmesh, os

# ════════════════════════════════════════════════════════════════════
#  PARAMETRY — drewno IIB + węzeł K (research 2026-05-31)
#  Bryła: real handlowy IIB 2600x260x160 mm (HIGH). Gauge/spacing z kodu gry.
# ════════════════════════════════════════════════════════════════════
L = 2.6        # długość podkładu (poprzeczna do toru) — Blender Y -> Unity Z
W = 0.26       # szerokość (wzdłuż toru)               — Blender X -> Unity X
H = 0.16       # wysokość                              — Blender Z -> Unity Y
GAUGE = 1.435
RAIL = GAUGE / 2.0     # ±0.7175 — oś szyny wzdłuż Y
FOOT = 0.125   # szerokość stopki szyny (wzdłuż Y)

# Węzeł K (na JEDNĄ szynę; ×2 na podkład). Z=H to górna powierzchnia drewna.
BP_X, BP_Y, BP_Z = 0.16, 0.30, 0.022      # podkładka żebrowa Pm-49 (płyta stalowa)
RIB_X, RIB_Y, RIB_Z = 0.16, 0.025, 0.035  # żebra (grzbiety po bokach stopki)
CLIP_X, CLIP_Y, CLIP_Z = 0.05, 0.045, 0.030  # łapka haczykowa
BOLT = 0.035   # łeb śruby stopowej (kwadratowy), wys 0.025
RIB_OFF = FOOT / 2 + 0.018   # odsunięcie żebra/łapki od osi szyny
BOLT_OX, BOLT_OY = 0.05, 0.11 # offset łbów śrub na rogach podkładki

PROJECT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))) \
    if "__file__" in globals() else r"D:\Gry\RM-0.2"
OUT_DIR = os.path.join(PROJECT, "BlenderSource", "Trackwork")
OUT_FBX = os.path.join(OUT_DIR, "Sleeper.fbx")
OUT_BLEND = os.path.join(OUT_DIR, "Sleeper.blend")

# ── czyść scenę ──
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
for m in list(bpy.data.materials):
    bpy.data.materials.remove(m)


# ── materiał: drewno kreozotowe (procedural stylized — Wave słoje + Noise bump) ──
def wood_material():
    """Drewno kreozotowe v2: słoje = pierścienie wokół osi długości (Y) z pithem na
    pół-wysokości -> słoje wzdłuż długości na top/bokach + pierścienie na czołach.
    + plamy kreozotu (Voronoi) + podłużne spękania (anizotropowy noise) + roughness var."""
    m = bpy.data.materials.new("Sleeper_Wood")
    m.use_nodes = True
    nt = m.node_tree
    n, lk = nt.nodes, nt.links
    bsdf = n.get("Principled BSDF")
    bsdf.inputs["Metallic"].default_value = 0.0
    tc = n.new("ShaderNodeTexCoord")

    # maska CZÓŁ: |Y| blisko końca podkładu (L/2 = 1.3) -> 0..1
    sep = n.new("ShaderNodeSeparateXYZ")
    lk.new(tc.outputs["Object"], sep.inputs["Vector"])
    yabs = n.new("ShaderNodeMath")
    yabs.operation = 'ABSOLUTE'
    lk.new(sep.outputs["Y"], yabs.inputs[0])
    endmask = n.new("ShaderNodeMapRange")
    endmask.inputs["From Min"].default_value = 1.08
    endmask.inputs["From Max"].default_value = 1.28
    lk.new(yabs.outputs[0], endmask.inputs["Value"])

    # SŁOJE: pierścienie wokół osi Y, pith na pół-wysokości (Z=0.08), mniej regularne
    wmap = n.new("ShaderNodeMapping")
    wmap.inputs["Location"].default_value = (0.0, 0.0, -0.08)
    lk.new(tc.outputs["Object"], wmap.inputs["Vector"])
    wave = n.new("ShaderNodeTexWave")
    wave.wave_type = 'RINGS'
    wave.rings_direction = 'Y'
    wave.inputs["Scale"].default_value = 5.0
    wave.inputs["Distortion"].default_value = 2.6
    wave.inputs["Detail"].default_value = 3.0
    lk.new(wmap.outputs["Vector"], wave.inputs["Vector"])
    ramp = n.new("ShaderNodeValToRGB")
    cr = ramp.color_ramp
    cr.elements[0].position = 0.15
    cr.elements[0].color = (0.075, 0.045, 0.024, 1)
    cr.elements[1].position = 0.85
    cr.elements[1].color = (0.175, 0.100, 0.058, 1)
    lk.new(wave.outputs["Color"], ramp.inputs["Fac"])

    # PLAMY KREOZOTU: rzadkie CIEMNE plamy (odwrócona maska — blisko punktów Voronoi)
    vor = n.new("ShaderNodeTexVoronoi")
    vor.inputs["Scale"].default_value = 5.0
    lk.new(tc.outputs["Object"], vor.inputs["Vector"])
    vramp = n.new("ShaderNodeValToRGB")
    vcr = vramp.color_ramp
    vcr.elements[0].position = 0.0
    vcr.elements[0].color = (1, 1, 1, 1)   # blisko punktu = plama
    vcr.elements[1].position = 0.12
    vcr.elements[1].color = (0, 0, 0, 1)   # dalej = brak plamy
    lk.new(vor.outputs["Distance"], vramp.inputs["Fac"])
    mix = n.new("ShaderNodeMixRGB")
    lk.new(vramp.outputs["Color"], mix.inputs[0])
    lk.new(ramp.outputs["Color"], mix.inputs[1])
    mix.inputs[2].default_value = (0.022, 0.013, 0.007, 1)  # ciemna tłusta plama kreozotu
    # CZOŁA: przyciemnienie przekroju (rdzeń)
    mix2 = n.new("ShaderNodeMixRGB")
    lk.new(endmask.outputs["Result"], mix2.inputs[0])
    lk.new(mix.outputs[0], mix2.inputs[1])
    mix2.inputs[2].default_value = (0.045, 0.028, 0.016, 1)  # ciemniejszy przekrój czoła
    lk.new(mix2.outputs[0], bsdf.inputs["Base Color"])

    # PODŁUŻNE SPĘKANIA: anizotropowy noise (wzdłuż Y) + gęstsze na czołach -> Bump
    cmap = n.new("ShaderNodeMapping")
    cmap.inputs["Scale"].default_value = (1.0, 0.12, 1.0)
    lk.new(tc.outputs["Object"], cmap.inputs["Vector"])
    noise = n.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 28.0
    noise.inputs["Detail"].default_value = 6.0
    lk.new(cmap.outputs["Vector"], noise.inputs["Vector"])
    crk = n.new("ShaderNodeValToRGB")
    ccr = crk.color_ramp
    ccr.elements[0].position = 0.45
    ccr.elements[1].position = 0.62
    lk.new(noise.outputs["Fac"], crk.inputs["Fac"])
    # height = crack + endmask*0.5 (wzmocnione spękania na czołach)
    addh = n.new("ShaderNodeMath")
    addh.operation = 'MULTIPLY_ADD'
    lk.new(endmask.outputs["Result"], addh.inputs[0])
    addh.inputs[1].default_value = 0.5
    lk.new(crk.outputs["Color"], addh.inputs[2])
    bump = n.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.50
    bump.inputs["Distance"].default_value = 0.02
    lk.new(addh.outputs[0], bump.inputs["Height"])
    lk.new(bump.outputs["Normal"], bsdf.inputs["Normal"])

    # ROUGHNESS: plamy kreozotu = niżej (tłusty sheen), drewno = matowe
    rmix = n.new("ShaderNodeMixRGB")
    lk.new(vramp.outputs["Color"], rmix.inputs[0])
    rmix.inputs[1].default_value = (0.85, 0.85, 0.85, 1)  # matowe drewno
    rmix.inputs[2].default_value = (0.55, 0.55, 0.55, 1)  # tłusta plama
    lk.new(rmix.outputs[0], bsdf.inputs["Roughness"])

    m.diffuse_color = (0.08, 0.05, 0.03, 1)  # fallback Workbench
    return m


# ── materiał: metal mocowań (żeliwo/stal, lekka rdza przez noise) ──
def metal_material():
    m = bpy.data.materials.new("Metal_Fitting")
    m.use_nodes = True
    nt = m.node_tree
    n, lk = nt.nodes, nt.links
    bsdf = n.get("Principled BSDF")
    bsdf.inputs["Metallic"].default_value = 0.55
    bsdf.inputs["Roughness"].default_value = 0.60
    tc = n.new("ShaderNodeTexCoord")
    noise = n.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 14.0
    noise.inputs["Detail"].default_value = 4.0
    lk.new(tc.outputs["Object"], noise.inputs["Vector"])
    ramp = n.new("ShaderNodeValToRGB")
    cr = ramp.color_ramp
    cr.elements[0].position = 0.35
    cr.elements[0].color = (0.024, 0.024, 0.022, 1)   # #2A2A28 ciemny metal
    cr.elements[1].position = 0.75
    cr.elements[1].color = (0.110, 0.045, 0.018, 1)   # #6E4326 rdza ~linear
    lk.new(noise.outputs["Fac"], ramp.inputs["Fac"])
    lk.new(ramp.outputs["Color"], bsdf.inputs["Base Color"])
    m.diffuse_color = (0.07, 0.05, 0.04, 1)
    return m


wood = wood_material()
metal = metal_material()

# ── geometria: jeden mesh, 2 sloty (wood=0, metal=1) ──
bm = bmesh.new()


def box(cx, cy, zlo, sx, sy, sz, mat_index):
    xs = (cx - sx / 2, cx + sx / 2)
    ys = (cy - sy / 2, cy + sy / 2)
    zs = (zlo, zlo + sz)
    pts = [(x, y, z) for x in xs for y in ys for z in zs]
    v = [bm.verts.new(p) for p in pts]
    faces = [(0, 1, 3, 2), (4, 5, 7, 6), (0, 1, 5, 4), (2, 3, 7, 6), (0, 2, 6, 4), (1, 3, 7, 5)]
    for f in faces:
        face = bm.faces.new([v[i] for i in f])
        face.material_index = mat_index


# belka drewna: dół z=0 (pivot bottom-center)
box(0.0, 0.0, 0.0, W, L, H, 0)

# węzeł K na obu szynach
for side in (RAIL, -RAIL):
    top = H
    box(0.0, side, top, BP_X, BP_Y, BP_Z, 1)                       # podkładka żebrowa Pm-49
    rib_z = top + BP_Z
    box(0.0, side - RIB_OFF, rib_z, RIB_X, RIB_Y, RIB_Z, 1)        # żebro wewn.
    box(0.0, side + RIB_OFF, rib_z, RIB_X, RIB_Y, RIB_Z, 1)        # żebro zewn.
    box(0.0, side - FOOT / 2, rib_z, CLIP_X, CLIP_Y, CLIP_Z, 1)    # łapka haczykowa 1
    box(0.0, side + FOOT / 2, rib_z, CLIP_X, CLIP_Y, CLIP_Z, 1)    # łapka haczykowa 2
    for ox in (-BOLT_OX, BOLT_OX):                                  # 4 łby śrub na rogach płyty
        for oy in (-BOLT_OY, BOLT_OY):
            box(ox, side + oy, rib_z, BOLT, BOLT, 0.025, 1)

mesh = bpy.data.meshes.new("Sleeper")
bm.to_mesh(mesh)
bm.free()

obj = bpy.data.objects.new("Sleeper", mesh)
bpy.context.collection.objects.link(obj)
obj.data.materials.append(wood)
obj.data.materials.append(metal)

bpy.context.view_layer.objects.active = obj
obj.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.normals_make_consistent(inside=True)   # kompensuje flip z bake_space_transform (RH->LH)
bpy.ops.uv.smart_project(angle_limit=1.15, island_margin=0.02)  # UV do FBX (potrzebne dla bake tekstur)
bpy.ops.object.mode_set(mode='OBJECT')
obj.location = (0.0, 0.0, 0.0)

tri_est = len(mesh.polygons) * 2
print(f"[gen_sleeper] DREWNO IIB + węzeł K | mesh: {len(mesh.vertices)} verts, "
      f"{len(mesh.polygons)} quads (~{tri_est} tris)")
print(f"[gen_sleeper] dims: {W} x {L} x {H} m, gauge {GAUGE}, rail @ +/-{RAIL}")

# ── eksport ──
os.makedirs(OUT_DIR, exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=OUT_BLEND)
bpy.ops.object.select_all(action='DESELECT')
obj.select_set(True)
bpy.ops.export_scene.fbx(
    filepath=OUT_FBX, use_selection=True,
    apply_scale_options='FBX_SCALE_ALL',
    axis_forward='-Z', axis_up='Y',
    bake_space_transform=True,   # wypala konwersję Z-up->Y-up (poprawna orientacja w Unity)
    object_types={'MESH'}, mesh_smooth_type='FACE',
)
print(f"[gen_sleeper] EXPORTED: {OUT_FBX}")
