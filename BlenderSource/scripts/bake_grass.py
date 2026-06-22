"""bake_grass.py — wypal trawę (particle hair) TOP-DOWN ortho do tile-able tekstury.

To "bake" detalu źdźbeł 3D w płaską teksturę (most FS25 detal -> CBM płaska tekstura).
Render ortho prosto z góry na środkowy fragment większego pola (czysty crop, bez krawędzi).
v1: Albedo. (Normal/seamless = kolejny krok.)

UŻYCIE: blender --background --factory-startup --python BlenderSource/scripts/bake_grass.py
Wyjście: Assets/Textures/Trackwork/Grass_Albedo.png  (+ podgląd Trackwork/Grass_tile_preview.png)
"""
import bpy, os, math

PROJECT = r"D:\Gry\RM-0.2"
out_albedo = os.path.join(PROJECT, "Assets", "Textures", "Trackwork", "Grass_Albedo.png")
TILE_M = 2.0          # bok kafla w metrach (ortho widzi 2x2 m)
RES = 1024            # px kafla

bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
for m in list(bpy.data.materials):
    bpy.data.materials.remove(m)


def soil_mat():
    m = bpy.data.materials.new("Soil")
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (0.070, 0.090, 0.045, 1)   # jaśniejsza gleba (przerwy nie czarne)
    b.inputs["Roughness"].default_value = 1.0
    b.inputs["Metallic"].default_value = 0.0
    return m


def grass_hair_mat():
    m = bpy.data.materials.new("GrassHair")
    m.use_nodes = True
    nt = m.node_tree
    n, lk = nt.nodes, nt.links
    bsdf = n.get("Principled BSDF")
    bsdf.inputs["Roughness"].default_value = 0.9
    bsdf.inputs["Metallic"].default_value = 0.0
    hi = n.new("ShaderNodeHairInfo")
    ramp = n.new("ShaderNodeValToRGB")
    cr = ramp.color_ramp
    cr.elements[0].position = 0.0
    cr.elements[0].color = (0.045, 0.065, 0.026, 1)   # stłumione, ciemniejsze (Unity dołoży światło)
    cr.elements[1].position = 1.0
    cr.elements[1].color = (0.175, 0.200, 0.082, 1)
    e_mid = cr.elements.new(0.5)
    e_mid.color = (0.095, 0.120, 0.048, 1)
    lk.new(hi.outputs["Intercept"], ramp.inputs["Fac"])
    rnd = n.new("ShaderNodeMapRange")
    rnd.inputs["To Min"].default_value = 0.80
    rnd.inputs["To Max"].default_value = 1.20
    lk.new(hi.outputs["Random"], rnd.inputs["Value"])
    tc2 = n.new("ShaderNodeTexCoord")
    mac = n.new("ShaderNodeTexNoise")
    mac.inputs["Scale"].default_value = 0.45
    mac.inputs["Detail"].default_value = 2.0
    lk.new(tc2.outputs["Object"], mac.inputs["Vector"])
    macv = n.new("ShaderNodeMapRange")
    macv.inputs["To Min"].default_value = 0.96   # prawie zero macro w teksturze (inaczej siatka per-kafel)
    macv.inputs["To Max"].default_value = 1.04
    lk.new(mac.outputs["Fac"], macv.inputs["Value"])
    mulv = n.new("ShaderNodeMath")
    mulv.operation = 'MULTIPLY'
    lk.new(rnd.outputs["Result"], mulv.inputs[0])
    lk.new(macv.outputs["Result"], mulv.inputs[1])
    hsv = n.new("ShaderNodeHueSaturation")
    hsv.inputs["Saturation"].default_value = 1.05
    lk.new(ramp.outputs["Color"], hsv.inputs["Color"])
    lk.new(mulv.outputs["Value"], hsv.inputs["Value"])
    lk.new(hsv.outputs["Color"], bsdf.inputs["Base Color"])
    return m


soil = soil_mat()
grass = grass_hair_mat()

# pole 4x4 m (ortho weźmie czysty środek 2x2 m, bez krawędzi)
bpy.ops.mesh.primitive_plane_add(size=4.0, location=(0, 0, 0))
plane = bpy.context.active_object
plane.data.materials.append(soil)
plane.data.materials.append(grass)

plane.modifiers.new("Grass", 'PARTICLE_SYSTEM')
psys = plane.particle_systems[-1]
s = psys.settings
s.type = 'HAIR'
s.count = 800000              # ~50k/m^2 jak w renderze (440k/9m^2)
s.hair_length = 0.032
s.hair_step = 3
s.material = 2
s.use_advanced_hair = True
s.length_random = 0.45
s.use_rotations = True
s.rotation_factor_random = 1.0
s.phase_factor_random = 1.0


def _try(attr, val):
    try:
        setattr(s, attr, val)
    except Exception as e:
        print(f"[grass] skip {attr}: {e}")


_try("roughness_1", 0.30)
_try("roughness_1_size", 0.6)
_try("roughness_endpoint", 0.18)
_try("roughness_end_shape", 2.0)

# ── kamera ORTHO prosto z góry (tile mapuje się 1:1 na płaski grunt) ──
cam_data = bpy.data.cameras.new("Cam")
cam_data.type = 'ORTHO'
cam_data.ortho_scale = 1.95
cam = bpy.data.objects.new("Cam", cam_data)
bpy.context.collection.objects.link(cam)
cam.location = (0.0, -1.09, 3.0)        # ortho lekko pochylona (20°) — łapie OŚWIETLONE boki źdźbeł (jasno), wciąż tileable-ish
cam.rotation_euler = (math.radians(20), 0.0, 0.0)
bpy.context.scene.camera = cam

# ── światło EVEN, ciepłe, raczej z góry (albedo z miękkim cieniem źdźbeł, bez ostrego kierunku) ──
sun_d = bpy.data.lights.new("Sun", 'SUN')
sun_d.energy = 2.6
sun_d.color = (1.0, 0.96, 0.88)
sun_d.angle = math.radians(8.0)        # miękki cień
sun = bpy.data.objects.new("Sun", sun_d)
bpy.context.collection.objects.link(sun)
sun.rotation_euler = (math.radians(72), 0.0, math.radians(20))   # prawie z góry = równo

world = bpy.context.scene.world or bpy.data.worlds.new("W")
bpy.context.scene.world = world
world.use_nodes = True
_bg = world.node_tree.nodes.get("Background")
if _bg:
    _bg.inputs["Strength"].default_value = 2.0
    _bg.inputs["Color"].default_value = (0.72, 0.74, 0.70, 1)

sc = bpy.context.scene
sc.render.engine = 'CYCLES'
sc.cycles.device = 'CPU'
sc.cycles.samples = 96
sc.view_settings.exposure = 0.1
sc.render.resolution_x = RES
sc.render.resolution_y = RES
sc.render.image_settings.file_format = 'PNG'
sc.render.filepath = out_albedo

os.makedirs(os.path.dirname(out_albedo), exist_ok=True)
bpy.ops.render.render(write_still=True)
print("BAKED ALBEDO:", out_albedo)
