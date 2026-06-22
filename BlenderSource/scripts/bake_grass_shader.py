"""bake_grass_shader.py — bake PŁASKICH ŹDŹBEŁ (shader per-komórka, obrócone w różnych
kierunkach = "rozdeptana trawa") top-down do tile-able tekstury Grass_Albedo.png.

Voronoi-komórki + per-komórka losowy obrót + maska kształtu źdźbła (wąskie, wydłużone),
6 warstw na gęste, nakładające się SMUGI w różnych kierunkach. Stłumiony kolor (Unity dołoży
światło). Płaski shader -> top-down ortho = czysty kafel; potem seamless heal.

UŻYCIE: blender --background --factory-startup --python BlenderSource/scripts/bake_grass_shader.py
Wyjście: Assets/Textures/Trackwork/Grass_Albedo.png
"""
import bpy, os, math

PROJECT = r"D:\Gry\RM-0.2"
out = os.path.join(PROJECT, "Assets", "Textures", "Trackwork", "Grass_Albedo.png")
RES = 2048   # wyższa rozdzielczość = ostrość bliżej podsypki

bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
for m in list(bpy.data.materials):
    bpy.data.materials.remove(m)


def _mr(n, val_sock, fmin, fmax, tmin, tmax, lk):
    mr = n.new("ShaderNodeMapRange")
    mr.inputs["From Min"].default_value = fmin
    mr.inputs["From Max"].default_value = fmax
    mr.inputs["To Min"].default_value = tmin
    mr.inputs["To Max"].default_value = tmax
    lk.new(val_sock, mr.inputs["Value"])
    return mr.outputs["Result"]


def blade_mask(n, lk, scaled_sock, offx, offy, width, length):
    off = n.new("ShaderNodeVectorMath"); off.operation = 'ADD'
    off.inputs[1].default_value = (offx, offy, 0.0)
    lk.new(scaled_sock, off.inputs[0])
    vor = n.new("ShaderNodeTexVoronoi"); vor.feature = 'F1'
    vor.inputs["Scale"].default_value = 1.0
    lk.new(off.outputs["Vector"], vor.inputs["Vector"])
    loc = n.new("ShaderNodeVectorMath"); loc.operation = 'SUBTRACT'
    lk.new(off.outputs["Vector"], loc.inputs[0]); lk.new(vor.outputs["Position"], loc.inputs[1])
    sepc = n.new("ShaderNodeSeparateColor"); lk.new(vor.outputs["Color"], sepc.inputs["Color"])
    ang = n.new("ShaderNodeMath"); ang.operation = 'MULTIPLY'; ang.inputs[1].default_value = 3.14159
    lk.new(sepc.outputs["Red"], ang.inputs[0])
    rot = n.new("ShaderNodeVectorRotate"); rot.rotation_type = 'Z_AXIS'
    lk.new(loc.outputs["Vector"], rot.inputs["Vector"]); lk.new(ang.outputs["Value"], rot.inputs["Angle"])
    seprot = n.new("ShaderNodeSeparateXYZ"); lk.new(rot.outputs["Vector"], seprot.inputs["Vector"])
    axn = n.new("ShaderNodeMath"); axn.operation = 'ABSOLUTE'; lk.new(seprot.outputs["X"], axn.inputs[0])
    ayn = n.new("ShaderNodeMath"); ayn.operation = 'ABSOLUTE'; lk.new(seprot.outputs["Y"], ayn.inputs[0])
    mx = _mr(n, axn.outputs[0], 0.0, width, 1.0, 0.0, lk)
    my = _mr(n, ayn.outputs[0], 0.0, length, 1.0, 0.0, lk)
    blade = n.new("ShaderNodeMath"); blade.operation = 'MULTIPLY'
    lk.new(mx, blade.inputs[0]); lk.new(my, blade.inputs[1])
    return blade.outputs[0]


def grass_shader():
    m = bpy.data.materials.new("Grass_Shader")
    m.use_nodes = True
    nt = m.node_tree; n, lk = nt.nodes, nt.links
    bsdf = n.get("Principled BSDF")
    bsdf.inputs["Metallic"].default_value = 0.0
    bsdf.inputs["Roughness"].default_value = 0.9
    tc = n.new("ShaderNodeTexCoord")
    scaled = n.new("ShaderNodeVectorMath"); scaled.operation = 'MULTIPLY'
    scaled.inputs[1].default_value = (24.0, 24.0, 24.0)   # drobniejsze + gęstsze (jak referencja); relief z normalki ratuje czytelność
    lk.new(tc.outputs["Object"], scaled.inputs[0])

    offsets = [(0, 0), (17.3, 9.1), (5.7, 23.4), (31.2, 13.8), (11.9, 37.5), (27.1, 29.8)]
    masks = [blade_mask(n, lk, scaled.outputs["Vector"], ox, oy, 0.22, 0.66) for (ox, oy) in offsets]   # 6 warstw = gęsto

    def _max(a, b):
        mm = n.new("ShaderNodeMath"); mm.operation = 'MAXIMUM'
        lk.new(a, mm.inputs[0]); lk.new(b, mm.inputs[1]); return mm.outputs[0]
    bmax = masks[0]
    for b in masks[1:]:
        bmax = _max(bmax, b)
    bmax = _mr(n, bmax, 0.10, 0.48, 0.0, 1.0, lk)   # ostrzejsze krawędzie źdźbeł (mniej miękko, bliżej tłucznia)

    # zieleń źdźbeł: wariacja per-obszar (STŁUMIONA — Unity dołoży światło)
    gvar = n.new("ShaderNodeTexNoise"); gvar.inputs["Scale"].default_value = 4.0
    gvar.inputs["Detail"].default_value = 3.0
    lk.new(tc.outputs["Object"], gvar.inputs["Vector"])
    gcol = n.new("ShaderNodeValToRGB")
    gcr = gcol.color_ramp
    gcr.elements[0].position = 0.28
    gcr.elements[0].color = (0.110, 0.140, 0.055, 1)   # dopasowane do referencji (muted oliwka)
    gcr.elements[1].position = 0.74
    gcr.elements[1].color = (0.265, 0.300, 0.120, 1)   # jaśniejsze czubki = większy kontrast smug
    lk.new(gvar.outputs["Fac"], gcol.inputs["Fac"])

    base = n.new("ShaderNodeMixRGB")
    lk.new(bmax, base.inputs[0])
    base.inputs[1].default_value = (0.042, 0.055, 0.022, 1)   # ciemniejsza gleba = mocniejszy kontrast smug
    lk.new(gcol.outputs["Color"], base.inputs[2])
    lk.new(base.outputs["Color"], bsdf.inputs["Base Color"])
    # bump z maski źdźbeł -> źdźbła rzucają mikro-cień przy bocznym świetle = czytelne SMUGI
    bump = n.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.45
    bump.inputs["Distance"].default_value = 0.006
    lk.new(bmax, bump.inputs["Height"])
    lk.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    return m


bpy.ops.mesh.primitive_plane_add(size=4.0, location=(0, 0, 0))
plane = bpy.context.active_object
plane.data.materials.append(grass_shader())

# ORTHO prosto z góry (płaski shader -> czysty kafel 2x2 m)
cam_data = bpy.data.cameras.new("Cam"); cam_data.type = 'ORTHO'; cam_data.ortho_scale = 2.0
cam = bpy.data.objects.new("Cam", cam_data)
bpy.context.collection.objects.link(cam)
cam.location = (0.0, 0.0, 3.0)
cam.rotation_euler = (0.0, 0.0, 0.0)
bpy.context.scene.camera = cam

# światło EVEN (płaski albedo, bez kierunku — Unity zrobi shading)
world = bpy.context.scene.world or bpy.data.worlds.new("W")
bpy.context.scene.world = world
world.use_nodes = True
_bg = world.node_tree.nodes.get("Background")
if _bg:
    _bg.inputs["Strength"].default_value = 1.1
    _bg.inputs["Color"].default_value = (0.95, 0.97, 0.92, 1)
sun_d = bpy.data.lights.new("Sun", 'SUN'); sun_d.energy = 2.6
sun_d.color = (1.0, 0.97, 0.88)
sun = bpy.data.objects.new("Sun", sun_d)
bpy.context.collection.objects.link(sun)
sun.rotation_euler = (math.radians(48), 0.0, math.radians(25))   # boczne -> mikro-cień źdźbeł = SMUGI

sc = bpy.context.scene
sc.render.engine = 'CYCLES'
sc.cycles.device = 'CPU'
sc.cycles.samples = 64
sc.view_settings.exposure = 0.1
sc.render.resolution_x = RES
sc.render.resolution_y = RES
sc.render.image_settings.file_format = 'PNG'
sc.render.filepath = out
os.makedirs(os.path.dirname(out), exist_ok=True)
bpy.ops.render.render(write_still=True)
print("BAKED SHADER GRASS:", out)
