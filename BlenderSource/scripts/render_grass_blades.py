"""render_grass_blades.py — trawa z PRAWDZIWYCH źdźbeł 3D (particle hair) -> docelowo bake
top-down do tile-able tekstury (jak FS25 detal upieczony w płaską teksturę CBM).

Cel: render trawy jak na referencji (gęsta, drobna, puszysta źdźbłowa, oliwka).
Particle hair: ~120k źdźbeł 5 cm + splay (roughness/rotation), kolor dark base -> light tip,
gleba ciemna między źdźbłami. Kamera płytki kąt jak na screenie.

UŻYCIE: blender --background --factory-startup --python BlenderSource/scripts/render_grass_blades.py
Wyjście: BlenderSource/Trackwork/Grass_preview.png
"""
import bpy, os, math, mathutils

PROJECT = r"D:\Gry\RM-0.2"
out = os.path.join(PROJECT, "BlenderSource", "Trackwork", "Grass_preview.png")

bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
for m in list(bpy.data.materials):
    bpy.data.materials.remove(m)


# ── gleba (widoczna między źdźbłami — ciemna, lekko zielono-brązowa) ──
def soil_mat():
    m = bpy.data.materials.new("Soil")
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (0.018, 0.028, 0.012, 1)
    b.inputs["Roughness"].default_value = 1.0
    b.inputs["Metallic"].default_value = 0.0
    return m


# ── źdźbło: kolor dark base -> light tip (Hair Info Intercept) + per-źdźbło wariacja ──
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
    cr.elements[0].color = (0.075, 0.100, 0.038, 1)   # podstawa (jaśniejsza, cieplejsza)
    cr.elements[1].position = 1.0
    cr.elements[1].color = (0.310, 0.330, 0.130, 1)   # jasny czubek (żółto-oliwka)
    e_mid = cr.elements.new(0.5)
    e_mid.color = (0.165, 0.190, 0.072, 1)            # środek
    lk.new(hi.outputs["Intercept"], ramp.inputs["Fac"])
    # per-źdźbło wariacja jasności (Random)
    rnd = n.new("ShaderNodeMapRange")
    rnd.inputs["To Min"].default_value = 0.80
    rnd.inputs["To Max"].default_value = 1.20
    lk.new(hi.outputs["Random"], rnd.inputs["Value"])
    # macro mottling: łaty jaśniej/ciemniej wg pozycji XY (jak referencja) — niski kontrast
    tc2 = n.new("ShaderNodeTexCoord")
    mac = n.new("ShaderNodeTexNoise")
    mac.inputs["Scale"].default_value = 0.45
    mac.inputs["Detail"].default_value = 2.0
    lk.new(tc2.outputs["Object"], mac.inputs["Vector"])
    macv = n.new("ShaderNodeMapRange")
    macv.inputs["To Min"].default_value = 0.72
    macv.inputs["To Max"].default_value = 1.25
    lk.new(mac.outputs["Fac"], macv.inputs["Value"])
    mulv = n.new("ShaderNodeMath")
    mulv.operation = 'MULTIPLY'
    lk.new(rnd.outputs["Result"], mulv.inputs[0])
    lk.new(macv.outputs["Result"], mulv.inputs[1])
    hsv = n.new("ShaderNodeHueSaturation")
    hsv.inputs["Saturation"].default_value = 1.30   # cieplejsza, bardziej nasycona żółto-oliwka
    lk.new(ramp.outputs["Color"], hsv.inputs["Color"])
    lk.new(mulv.outputs["Value"], hsv.inputs["Value"])
    lk.new(hsv.outputs["Color"], bsdf.inputs["Base Color"])
    return m


soil = soil_mat()
grass = grass_hair_mat()

# ── emitter plane + 2 sloty (0=gleba, 1=trawa) ──
bpy.ops.mesh.primitive_plane_add(size=3.0, location=(0, 0, 0))
plane = bpy.context.active_object
plane.name = "GrassPatch"
plane.data.materials.append(soil)    # slot 0 (powierzchnia)
plane.data.materials.append(grass)   # slot 1 (źdźbła)

# ── particle hair = źdźbła ──
plane.modifiers.new("Grass", 'PARTICLE_SYSTEM')
psys = plane.particle_systems[-1]
s = psys.settings
s.type = 'HAIR'
s.count = 440000
s.hair_length = 0.032
s.hair_step = 3
s.material = 2                 # 1-based slot index -> slot 1 = GrassHair
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


# splay/waviness (żeby źdźbła nie stały jak szczotka) — nazwy pól zabezpieczone
_try("roughness_1", 0.30)
_try("roughness_1_size", 0.6)
_try("roughness_endpoint", 0.18)
_try("roughness_end_shape", 2.0)

# ── kamera: płytki kąt jak na referencji ──
cam_data = bpy.data.cameras.new("Cam")
cam = bpy.data.objects.new("Cam", cam_data)
bpy.context.collection.objects.link(cam)
cam.location = (0.0, -0.50, 1.30)   # ~60° — widać kierunkowe źdźbła (jak referencja), nie tylko czubki
look = mathutils.Vector((0.0, 0.25, 0.0))
cam.rotation_euler = (look - cam.location).to_track_quat('-Z', 'Y').to_euler()
cam_data.lens = 50
bpy.context.scene.camera = cam

# ── słońce popołudniowe + lekkie niebo ──
sun_d = bpy.data.lights.new("Sun", 'SUN')
sun_d.energy = 5.5
sun_d.color = (1.0, 0.94, 0.80)   # ciepłe popołudniowe słońce
sun_d.angle = math.radians(2.0)
sun = bpy.data.objects.new("Sun", sun_d)
bpy.context.collection.objects.link(sun)
sun.rotation_euler = (math.radians(48), math.radians(10), math.radians(35))

world = bpy.context.scene.world or bpy.data.worlds.new("W")
bpy.context.scene.world = world
world.use_nodes = True
_bg = world.node_tree.nodes.get("Background")
if _bg:
    _bg.inputs["Strength"].default_value = 3.4
    _bg.inputs["Color"].default_value = (0.74, 0.74, 0.68, 1)   # neutralniej/cieplej (mniej chłodnego washu)

sc = bpy.context.scene
sc.render.engine = 'CYCLES'
sc.cycles.device = 'CPU'
sc.cycles.samples = 64
sc.view_settings.exposure = 0.55   # rozjaśnienie (dense grass self-shadow eats light)
sc.render.resolution_x = 1280
sc.render.resolution_y = 760
sc.render.filepath = out
bpy.ops.render.render(write_still=True)
print("RENDERED:", out)
