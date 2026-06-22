"""
gen_ballast.py — proceduralny shader tłucznia (podsypka) dla Railway Manager. v2.

Research-driven (workflow whrk4vww4: PKP PLK Id-110). Miks granit/bazalt/melafir łamany,
frakcja 31,5-50 mm. v2 (feedback user): kamienie NIEROWNE (displacement, nie tylko bump) +
kolor zróżnicowany per-kamień (paleta 5 odcieni szaro-brąz + losowa jasność), realny zapylony
ton zamiast binarnego szary/czarny.

To PROOF WYGLĄDU. W grze: tekstura tile-able (bake displacement→normal, GenerateBallast).
UŻYCIE: blender --background --factory-startup --python BlenderSource/scripts/gen_ballast.py
Wyjście: BlenderSource/Trackwork/Ballast_preview.png (+ Ballast.blend).
"""

import bpy, os, math

PROJECT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))) \
    if "__file__" in globals() else r"D:\Gry\RM-0.2"
OUT_DIR = os.path.join(PROJECT, "BlenderSource", "Trackwork")
OUT_PNG = os.path.join(OUT_DIR, "Ballast_preview.png")
OUT_BLEND = os.path.join(OUT_DIR, "Ballast.blend")

VORONOI_SCALE = 45.0   # ~45 ziaren/oś na tile 2 m = ziarno ~44 mm (frakcja 31,5-50)

bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
for m in list(bpy.data.materials):
    bpy.data.materials.remove(m)


def ballast_material():
    m = bpy.data.materials.new("Ballast")
    m.use_nodes = True
    nt = m.node_tree
    n, lk = nt.nodes, nt.links
    out = n.get("Material Output")
    bsdf = n.get("Principled BSDF")
    bsdf.inputs["Roughness"].default_value = 0.88
    bsdf.inputs["Metallic"].default_value = 0.0
    tc = n.new("ShaderNodeTexCoord")

    # WARP -> kanciaste komórki ("łamane")
    nw = n.new("ShaderNodeTexNoise")
    nw.inputs["Scale"].default_value = 16.0
    nw.inputs["Detail"].default_value = 2.0
    lk.new(tc.outputs["Generated"], nw.inputs["Vector"])
    mul = n.new("ShaderNodeVectorMath")
    mul.operation = 'MULTIPLY'
    mul.inputs[1].default_value = (0.18, 0.18, 0.18)
    lk.new(nw.outputs["Color"], mul.inputs[0])
    add = n.new("ShaderNodeVectorMath")
    add.operation = 'ADD'
    lk.new(tc.outputs["Generated"], add.inputs[0])
    lk.new(mul.outputs["Vector"], add.inputs[1])

    ve = n.new("ShaderNodeTexVoronoi")   # spoiny + relief
    ve.feature = 'DISTANCE_TO_EDGE'
    ve.inputs["Scale"].default_value = VORONOI_SCALE
    lk.new(add.outputs["Vector"], ve.inputs["Vector"])
    vc = n.new("ShaderNodeTexVoronoi")   # per-cell ID (kolor)
    vc.feature = 'F1'
    vc.inputs["Scale"].default_value = VORONOI_SCALE
    lk.new(add.outputs["Vector"], vc.inputs["Vector"])

    # per-cell odcień z PALETY 5 kolorów (szaro-brąz realny tłuczeń, NIE czarno-biały)
    sepc = n.new("ShaderNodeSeparateColor")
    lk.new(vc.outputs["Color"], sepc.inputs["Color"])
    pal = n.new("ShaderNodeValToRGB")
    cr = pal.color_ramp
    cr.interpolation = 'CONSTANT'
    cr.elements[0].position = 0.0
    cr.elements[0].color = (0.25, 0.23, 0.21, 1)     # jasnoszary granit #8A8580
    cr.elements[1].position = 0.25
    cr.elements[1].color = (0.092, 0.102, 0.116, 1)  # chłodny niebiesko-szary #565A60
    el2 = cr.elements.new(0.45)
    el2.color = (0.150, 0.075, 0.037, 1)             # wyraźny brązowo-rdzawy #6E4E36
    el3 = cr.elements.new(0.68)
    el3.color = (0.039, 0.044, 0.052, 1)             # ciemny łupek niebieski #383B40
    el4 = cr.elements.new(0.88)
    el4.color = (0.220, 0.107, 0.052, 1)             # jasny rdzawy akcent #845F42
    lk.new(sepc.outputs["Red"], pal.inputs["Fac"])

    # losowa JASNOŚĆ per-kamień (drugi kanał Voronoi Color) — kamienie różnej jasności
    sepg = n.new("ShaderNodeSeparateColor")
    lk.new(vc.outputs["Color"], sepg.inputs["Color"])
    bval = n.new("ShaderNodeMapRange")
    bval.inputs["To Min"].default_value = 0.65
    bval.inputs["To Max"].default_value = 1.15
    lk.new(sepg.outputs["Green"], bval.inputs["Value"])
    hsv = n.new("ShaderNodeHueSaturation")
    lk.new(pal.outputs["Color"], hsv.inputs["Color"])
    lk.new(bval.outputs["Result"], hsv.inputs["Value"])

    # pył szaro-brązowy w spoinach
    edge = n.new("ShaderNodeValToRGB")
    edge.color_ramp.elements[0].position = 0.0
    edge.color_ramp.elements[0].color = (1, 1, 1, 1)
    edge.color_ramp.elements[1].position = 0.10
    edge.color_ramp.elements[1].color = (0, 0, 0, 1)
    lk.new(ve.outputs["Distance"], edge.inputs["Fac"])
    dust = n.new("ShaderNodeMixRGB")
    lk.new(edge.outputs["Color"], dust.inputs[0])
    lk.new(hsv.outputs["Color"], dust.inputs[1])
    dust.inputs[2].default_value = (0.090, 0.072, 0.055, 1)  # pył w spoinach
    lk.new(dust.outputs[0], bsdf.inputs["Base Color"])

    # per-cell POŁYSK: część kamieni błyszcząca (łupek/wilgoć), reszta matowa
    sepb = n.new("ShaderNodeSeparateColor")
    lk.new(vc.outputs["Color"], sepb.inputs["Color"])
    rgh = n.new("ShaderNodeMapRange")
    rgh.inputs["To Min"].default_value = 0.48
    rgh.inputs["To Max"].default_value = 0.92
    lk.new(sepb.outputs["Blue"], rgh.inputs["Value"])
    lk.new(rgh.outputs["Result"], bsdf.inputs["Roughness"])

    # DISPLACEMENT: w tym setupie distance-to-edge = MAŁE w środku komórki / DUŻE na krawędzi
    # (środki się zapadały). ODWRACAMY (0.5 - distance): środek = szczyt WYPUKŁY, krawędź = spoina.
    inv = n.new("ShaderNodeMath")
    inv.operation = 'SUBTRACT'
    inv.inputs[0].default_value = 0.5
    lk.new(ve.outputs["Distance"], inv.inputs[1])
    clamp = n.new("ShaderNodeMath")
    clamp.operation = 'MAXIMUM'
    lk.new(inv.outputs[0], clamp.inputs[0])
    clamp.inputs[1].default_value = 0.0
    nmf = n.new("ShaderNodeTexNoise")
    nmf.inputs["Scale"].default_value = 120.0
    nmf.inputs["Detail"].default_value = 6.0
    lk.new(tc.outputs["Generated"], nmf.inputs["Vector"])
    addh = n.new("ShaderNodeMath")
    addh.operation = 'MULTIPLY_ADD'
    lk.new(nmf.outputs["Fac"], addh.inputs[0])
    addh.inputs[1].default_value = 0.04
    lk.new(clamp.outputs[0], addh.inputs[2])
    disp = n.new("ShaderNodeDisplacement")
    disp.inputs["Midlevel"].default_value = 0.0
    disp.inputs["Scale"].default_value = 0.08
    lk.new(addh.outputs[0], disp.inputs["Height"])
    lk.new(disp.outputs["Displacement"], out.inputs["Displacement"])

    m.displacement_method = 'BOTH'
    m.diffuse_color = (0.22, 0.21, 0.19, 1)
    return m


# plane + adaptive subdivision (Cycles microdisplacement) dla prawdziwych nierównych kamieni
bpy.ops.mesh.primitive_plane_add(size=2.0, location=(0, 0, 0))
plane = bpy.context.active_object
mod = plane.modifiers.new("Subdiv", 'SUBSURF')
mod.subdivision_type = 'SIMPLE'
mod.levels = 2
mod.render_levels = 2
plane.cycles.use_adaptive_subdivision = True
plane.cycles.dicing_rate = 1.0
plane.data.materials.append(ballast_material())

# kamera 3/4 perspektywa close (widać relief kamieni)
cam_data = bpy.data.cameras.new("Cam")
cam = bpy.data.objects.new("Cam", cam_data)
bpy.context.collection.objects.link(cam)
cam.location = (1.05, -1.05, 0.92)
import mathutils
look = mathutils.Vector((0, 0, 0.02))
cam.rotation_euler = (look - cam.location).to_track_quat('-Z', 'Y').to_euler()
cam_data.lens = 48
bpy.context.scene.camera = cam

sun_d = bpy.data.lights.new("Sun", 'SUN')
sun_d.energy = 4.8
sun = bpy.data.objects.new("Sun", sun_d)
bpy.context.collection.objects.link(sun)
sun.rotation_euler = (math.radians(52), math.radians(18), math.radians(20))
world = bpy.context.scene.world or bpy.data.worlds.new("W")
bpy.context.scene.world = world
world.use_nodes = True
_bg = world.node_tree.nodes.get("Background")
if _bg:
    _bg.inputs["Strength"].default_value = 1.3
    _bg.inputs["Color"].default_value = (0.62, 0.66, 0.72, 1)

sc = bpy.context.scene
sc.render.engine = 'CYCLES'
sc.cycles.feature_set = 'EXPERIMENTAL'   # adaptive subdivision
sc.cycles.device = 'CPU'
sc.cycles.samples = 64
sc.render.resolution_x = 1000
sc.render.resolution_y = 750
sc.render.filepath = OUT_PNG

os.makedirs(OUT_DIR, exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=OUT_BLEND)
bpy.ops.render.render(write_still=True)
print(f"[gen_ballast] v2 displacement + paleta 5-kolor | Voronoi {VORONOI_SCALE}")
print(f"[gen_ballast] RENDERED: {OUT_PNG}")
