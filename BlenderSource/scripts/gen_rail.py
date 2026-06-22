"""
gen_rail.py — proceduralny generator szyny S49 (49E1) dla Railway Manager.

Profil zweryfikowany research-driven (workflow w86pesz8j, datasheet ArcelorMittal,
PN-EN 13674-1, weryfikacja adwersarialna HIGH). 19 wierzchołków, profil uproszczony
(fazy zamiast łuków normowych — szyna instancjonowana na tysiącach metrów).

WG KODU GRY (GenerateRailPrefabs): railPrefab instancjonowany co 1 m wzdłuż toru,
LookRotation(tangent), offset ±0.7175 (gauge 1.435). pos.Y=0 BEZ podnoszenia → mesh
musi mieć stopkę na 0.182 m (górna powierzchnia podkładki żebrowej węzła K). Oś długości
wzdłuż Blender Y (spójnie ze Sleeper.fbx). Jeden mesh = jedna szyna (gra robi 2× L/P).

UŻYCIE (headless): blender --background --factory-startup --python BlenderSource/scripts/gen_rail.py
Wyjście: BlenderSource/Trackwork/Rail.fbx (+ .blend).
"""

import bpy, bmesh, os

# ── profil S49 (x=poprzecznie, z=wysokość), metry, spód stopki z=0 (research) ──
# Profil S49 odwzorowany z rysunku ArcelorMittal (łuki przybliżone punktami, mm/1000).
# Symetryczny wzgl. x=0. Stopka 125 / szyjka 14 / główka 67 / H=149.
PROFILE = [
    # prawa STOPKA (skrzydło skos 1:7.81 + krawędź R1.5/R3)
    (0.0625, 0.0), (0.0625, 0.0105), (0.045, 0.020), (0.030, 0.0275),
    # fillet R120/R80 -> SZYJKA (talia 14)
    (0.0115, 0.044), (0.007, 0.058), (0.007, 0.088),
    # fillet R7 -> DÓŁ GŁÓWKI (skos 1:3 + R2)
    (0.0105, 0.0965), (0.022, 0.103),
    # bok GŁÓWKI (max 67) + R13 + R80 róg górny
    (0.0335, 0.112), (0.0335, 0.131), (0.0305, 0.139), (0.0245, 0.1445),
    # wierzch R300 (crown 46.8) -> szczyt
    (0.0155, 0.1475), (0.0, 0.149),
    # lewa strona (mirror)
    (-0.0155, 0.1475), (-0.0245, 0.1445), (-0.0305, 0.139), (-0.0335, 0.131),
    (-0.0335, 0.112), (-0.022, 0.103), (-0.0105, 0.0965),
    (-0.007, 0.088), (-0.007, 0.058), (-0.0115, 0.044),
    (-0.030, 0.0275), (-0.045, 0.020), (-0.0625, 0.0105), (-0.0625, 0.0),
]
FOOT_Z = 0.182    # stopka siada na podkładce żebrowej węzła K (korekta wg kodu — gra nie podnosi)
SEG_LEN = 1.0     # segment 1 m (krok instancjonowania w grze)

PROJECT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))) \
    if "__file__" in globals() else r"D:\Gry\RM-0.2"
OUT_DIR = os.path.join(PROJECT, "BlenderSource", "Trackwork")
OUT_FBX = os.path.join(OUT_DIR, "Rail.fbx")
OUT_BLEND = os.path.join(OUT_DIR, "Rail.blend")

bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
for m in list(bpy.data.materials):
    bpy.data.materials.remove(m)


def rail_material():
    """Stal: główka WYTARTA do połysku (metallic, niski roughness) / szyjka+stopka RDZA (mat).
    Maska po lokalnej wysokości Z (TexCoord Object — niezależna od rotacji instancji)."""
    m = bpy.data.materials.new("Rail_Steel")
    m.use_nodes = True
    nt = m.node_tree
    n, lk = nt.nodes, nt.links
    bsdf = n.get("Principled BSDF")
    tc = n.new("ShaderNodeTexCoord")
    sep = n.new("ShaderNodeSeparateXYZ")
    lk.new(tc.outputs["Object"], sep.inputs["Vector"])
    # maska główki: top szyny (z ~0.30..0.328 po offset) -> 1=polished, 0=rdza
    # WĄSKI pasek toczny na samym wierzchu główki (z ~0.327-0.331 po offset 0.182)
    zmask = n.new("ShaderNodeMapRange")
    zmask.inputs["From Min"].default_value = 0.3285  # tylko sam czubek główki (~3 mm pasek)
    zmask.inputs["From Max"].default_value = 0.331
    lk.new(sep.outputs["Z"], zmask.inputs["Value"])
    # kolor: RDZA (cała szyna) <-> wyślizgany JASNY srebrny pasek (lustrzany, styk z kołem)
    col = n.new("ShaderNodeMixRGB")
    lk.new(zmask.outputs["Result"], col.inputs[0])
    col.inputs[1].default_value = (0.190, 0.070, 0.025, 1)   # rdza #7A4B2B
    col.inputs[2].default_value = (0.800, 0.820, 0.850, 1)   # jasny srebrny wyślizg
    lk.new(col.outputs[0], bsdf.inputs["Base Color"])
    # metallic: rdza 0.0 (utleniona) -> pasek toczny 1.0 (goła stal)
    lk.new(zmask.outputs["Result"], bsdf.inputs["Metallic"])
    # roughness: rdza matowa 0.85 -> wyślizgany pasek 0.07 (LUSTRZANY, ostry highlight)
    rr = n.new("ShaderNodeMapRange")
    rr.inputs["To Min"].default_value = 0.85
    rr.inputs["To Max"].default_value = 0.07
    lk.new(zmask.outputs["Result"], rr.inputs["Value"])
    lk.new(rr.outputs["Result"], bsdf.inputs["Roughness"])
    m.diffuse_color = (0.25, 0.15, 0.10, 1)
    return m


steel = rail_material()

# ── geometria: profil (XZ) extrudowany wzdłuż Y o 1 m, centered ──
bm = bmesh.new()
P = [(x, z + FOOT_Z) for (x, z) in PROFILE]
loop0 = [bm.verts.new((x, -SEG_LEN / 2, z)) for (x, z) in P]
loop1 = [bm.verts.new((x, SEG_LEN / 2, z)) for (x, z) in P]
nP = len(P)
for i in range(nP):                                   # boczne quady
    j = (i + 1) % nP
    bm.faces.new([loop0[i], loop0[j], loop1[j], loop1[i]])
bm.faces.new(loop0)                                   # cap front
bm.faces.new(list(reversed(loop1)))                   # cap back

mesh = bpy.data.meshes.new("Rail")
bm.to_mesh(mesh)
bm.free()
obj = bpy.data.objects.new("Rail", mesh)
bpy.context.collection.objects.link(obj)
obj.data.materials.append(steel)

bpy.context.view_layer.objects.active = obj
obj.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.normals_make_consistent(inside=True)   # spójnie z sleeper (kompensacja bake_space_transform)
bpy.ops.uv.smart_project(angle_limit=1.15, island_margin=0.02)  # UV do FBX (bake tekstur)
bpy.ops.object.mode_set(mode='OBJECT')
obj.location = (0.0, 0.0, 0.0)

tri_est = len(mesh.polygons) * 2
print(f"[gen_rail] S49 | mesh: {len(mesh.vertices)} verts, {len(mesh.polygons)} faces (~{tri_est} tris)")
print(f"[gen_rail] H=0.149, stopka=0.125, główka=0.067; stopka @ z={FOOT_Z}, seg={SEG_LEN}m")

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
print(f"[gen_rail] EXPORTED: {OUT_FBX}")

# ── podgląd close-up główki ──
import math, mathutils
cam_data = bpy.data.cameras.new("Cam")
cam = bpy.data.objects.new("Cam", cam_data)
bpy.context.collection.objects.link(cam)
cam.location = (0.34, -0.62, 0.50)   # wzdłuż szyny z boku-góry: pasek toczny biegnie wzdłuż wierzchu
look = mathutils.Vector((0.0, 0.30, 0.30))
cam.rotation_euler = (look - cam.location).to_track_quat('-Z', 'Y').to_euler()
cam_data.lens = 45
bpy.context.scene.camera = cam
sun_d = bpy.data.lights.new("Sun", 'SUN')
sun_d.energy = 4.0
sun = bpy.data.objects.new("Sun", sun_d)
bpy.context.collection.objects.link(sun)
sun.rotation_euler = (math.radians(50), math.radians(22), math.radians(28))
world = bpy.context.scene.world or bpy.data.worlds.new("W")
bpy.context.scene.world = world
world.use_nodes = True
_bg = world.node_tree.nodes.get("Background")
if _bg:
    _bg.inputs["Strength"].default_value = 1.0
    _bg.inputs["Color"].default_value = (0.6, 0.64, 0.7, 1)
sc = bpy.context.scene
sc.render.engine = 'CYCLES'
sc.cycles.device = 'CPU'
sc.cycles.samples = 48
sc.render.resolution_x = 1000
sc.render.resolution_y = 600
sc.render.filepath = os.path.join(OUT_DIR, "Rail_preview.png")
bpy.ops.render.render(write_still=True)
print("[gen_rail] RENDERED preview: Rail_preview.png")
