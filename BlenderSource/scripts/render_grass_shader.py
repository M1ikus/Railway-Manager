"""render_grass_shader.py — TRAWA JAKO SHADER (płaski plan): osobne ŹDŹBŁA w różnych
kierunkach (Voronoi-komórki + per-komórka losowy obrót + maska kształtu źdźbła), GĘSTO
(6 warstw), z BLISKA jak referencja CBM. Czysto płaski shader -> bake trywialny + seamless.

UŻYCIE: blender --background --factory-startup --python BlenderSource/scripts/render_grass_shader.py
Wyjście: BlenderSource/Trackwork/GrassShader_preview.png
"""
import bpy, os, math, mathutils

PROJECT = r"D:\Gry\RM-0.2"
out = os.path.join(PROJECT, "BlenderSource", "Trackwork", "GrassShader_preview.png")

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
    off = n.new("ShaderNodeVectorMath")
    off.operation = 'ADD'
    off.inputs[1].default_value = (offx, offy, 0.0)
    lk.new(scaled_sock, off.inputs[0])
    vor = n.new("ShaderNodeTexVoronoi")
    vor.feature = 'F1'
    vor.inputs["Scale"].default_value = 1.0
    lk.new(off.outputs["Vector"], vor.inputs["Vector"])
    loc = n.new("ShaderNodeVectorMath")
    loc.operation = 'SUBTRACT'
    lk.new(off.outputs["Vector"], loc.inputs[0])
    lk.new(vor.outputs["Position"], loc.inputs[1])
    sepc = n.new("ShaderNodeSeparateColor")
    lk.new(vor.outputs["Color"], sepc.inputs["Color"])
    ang = n.new("ShaderNodeMath")
    ang.operation = 'MULTIPLY'
    ang.inputs[1].default_value = 3.14159
    lk.new(sepc.outputs["Red"], ang.inputs[0])
    rot = n.new("ShaderNodeVectorRotate")
    rot.rotation_type = 'Z_AXIS'
    lk.new(loc.outputs["Vector"], rot.inputs["Vector"])
    lk.new(ang.outputs["Value"], rot.inputs["Angle"])
    seprot = n.new("ShaderNodeSeparateXYZ")
    lk.new(rot.outputs["Vector"], seprot.inputs["Vector"])
    ax = n.new("ShaderNodeMath")
    ax.operation = 'ABSOLUTE'
    lk.new(seprot.outputs["X"], ax.inputs[0])
    ay = n.new("ShaderNodeMath")
    ay.operation = 'ABSOLUTE'
    lk.new(seprot.outputs["Y"], ay.inputs[0])
    mx = _mr(n, ax.outputs[0], 0.0, width, 1.0, 0.0, lk)
    my = _mr(n, ay.outputs[0], 0.0, length, 1.0, 0.0, lk)
    blade = n.new("ShaderNodeMath")
    blade.operation = 'MULTIPLY'
    lk.new(mx, blade.inputs[0])
    lk.new(my, blade.inputs[1])
    return blade.outputs[0], sepc.outputs["Green"]


def grass_shader():
    m = bpy.data.materials.new("Grass_Shader")
    m.use_nodes = True
    nt = m.node_tree
    n, lk = nt.nodes, nt.links
    bsdf = n.get("Principled BSDF")
    bsdf.inputs["Metallic"].default_value = 0.0
    bsdf.inputs["Roughness"].default_value = 0.9
    tc = n.new("ShaderNodeTexCoord")

    scaled = n.new("ShaderNodeVectorMath")
    scaled.operation = 'MULTIPLY'
    scaled.inputs[1].default_value = (24.0, 24.0, 24.0)   # komórka ~4 cm
    lk.new(tc.outputs["Object"], scaled.inputs[0])

    offsets = [(0.0, 0.0), (17.3, 9.1), (5.7, 23.4), (31.2, 13.8), (11.9, 37.5), (27.1, 29.8)]
    masks = []
    greens = []
    for (ox, oy) in offsets:
        b, g = blade_mask(n, lk, scaled.outputs["Vector"], ox, oy, 0.24, 0.58)
        masks.append(b)
        greens.append(g)

    def _max(a, b):
        mm = n.new("ShaderNodeMath")
        mm.operation = 'MAXIMUM'
        lk.new(a, mm.inputs[0])
        lk.new(b, mm.inputs[1])
        return mm.outputs[0]

    bmax = masks[0]
    for b in masks[1:]:
        bmax = _max(bmax, b)

    # zieleń: wariacja per-obszar (żółto-oliwka)
    gvar = n.new("ShaderNodeTexNoise")
    gvar.inputs["Scale"].default_value = 7.0
    gvar.inputs["Detail"].default_value = 3.0
    lk.new(tc.outputs["Object"], gvar.inputs["Vector"])
    gcol = n.new("ShaderNodeValToRGB")
    gcr = gcol.color_ramp
    gcr.elements[0].position = 0.28
    gcr.elements[0].color = (0.150, 0.180, 0.068, 1)
    gcr.elements[1].position = 0.74
    gcr.elements[1].color = (0.300, 0.315, 0.125, 1)
    lk.new(gvar.outputs["Fac"], gcol.inputs["Fac"])

    macro = n.new("ShaderNodeTexNoise")
    macro.inputs["Scale"].default_value = 0.6
    macro.inputs["Detail"].default_value = 2.0
    lk.new(tc.outputs["Object"], macro.inputs["Vector"])
    macv = _mr(n, macro.outputs["Fac"], 0.0, 1.0, 0.86, 1.12, lk)

    base = n.new("ShaderNodeMixRGB")
    lk.new(bmax, base.inputs[0])
    base.inputs[1].default_value = (0.085, 0.105, 0.045, 1)   # gleba/cień (jasna — gęsta trawa)
    lk.new(gcol.outputs["Color"], base.inputs[2])
    hsv = n.new("ShaderNodeHueSaturation")
    hsv.inputs["Saturation"].default_value = 1.15
    lk.new(macv, hsv.inputs["Value"])
    lk.new(base.outputs["Color"], hsv.inputs["Color"])
    lk.new(hsv.outputs["Color"], bsdf.inputs["Base Color"])

    bump = n.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.22
    bump.inputs["Distance"].default_value = 0.004
    lk.new(bmax, bump.inputs["Height"])
    lk.new(bump.outputs["Normal"], bsdf.inputs["Normal"])

    m.diffuse_color = (0.10, 0.13, 0.05, 1)
    return m


bpy.ops.mesh.primitive_plane_add(size=8.0, location=(0, 0, 0))
ground = bpy.context.active_object
ground.data.materials.append(grass_shader())

# kamera Z BLISKA jak referencja (widać pojedyncze źdźbła, fill frame)
cam_data = bpy.data.cameras.new("Cam")
cam = bpy.data.objects.new("Cam", cam_data)
bpy.context.collection.objects.link(cam)
cam.location = (0.0, -1.05, 1.15)
look = mathutils.Vector((0.0, 0.30, 0.0))
cam.rotation_euler = (look - cam.location).to_track_quat('-Z', 'Y').to_euler()
cam_data.lens = 45
bpy.context.scene.camera = cam

sun_d = bpy.data.lights.new("Sun", 'SUN')
sun_d.energy = 4.0
sun_d.color = (1.0, 0.96, 0.84)
sun = bpy.data.objects.new("Sun", sun_d)
bpy.context.collection.objects.link(sun)
sun.rotation_euler = (math.radians(55), math.radians(10), math.radians(30))

world = bpy.context.scene.world or bpy.data.worlds.new("W")
bpy.context.scene.world = world
world.use_nodes = True
_bg = world.node_tree.nodes.get("Background")
if _bg:
    _bg.inputs["Strength"].default_value = 1.4
    _bg.inputs["Color"].default_value = (0.66, 0.70, 0.74, 1)

sc = bpy.context.scene
sc.render.engine = 'CYCLES'
sc.cycles.device = 'CPU'
sc.cycles.samples = 80
sc.view_settings.exposure = 0.45
sc.render.resolution_x = 1280
sc.render.resolution_y = 760
sc.render.filepath = out
bpy.ops.render.render(write_still=True)
print("RENDERED:", out)
