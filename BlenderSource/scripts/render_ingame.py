"""render_ingame.py — PODGLĄD IN-GAME: płaski grunt z wypaloną teksturą trawy (Grass_Albedo.png)
kafelkowaną + fragment toru, pod kątem zajezdni. To 1:1 reprezentacja gry (grunt = płaski plane
+ tekstura, NIE geometria źdźbeł). Test czy bake trawy trzyma się na płasko.

UŻYCIE: blender --background --factory-startup --python BlenderSource/scripts/render_ingame.py
Wyjście: BlenderSource/Trackwork/Ingame_preview.png
"""
import bpy, bmesh, os, math, mathutils

PROJECT = r"D:\Gry\RM-0.2"
sleeper_blend = os.path.join(PROJECT, "BlenderSource", "Trackwork", "Sleeper.blend")
grass_tex = os.path.join(PROJECT, "Assets", "Textures", "Trackwork", "Grass_Albedo.png")
out = os.path.join(PROJECT, "BlenderSource", "Trackwork", "Ingame_preview.png")

PROFILE = [
    (0.0625, 0.0), (0.0625, 0.0105), (0.045, 0.020), (0.030, 0.0275),
    (0.0115, 0.044), (0.007, 0.058), (0.007, 0.088),
    (0.0105, 0.0965), (0.022, 0.103),
    (0.0335, 0.112), (0.0335, 0.131), (0.0305, 0.139), (0.0245, 0.1445),
    (0.0155, 0.1475), (0.0, 0.149),
    (-0.0155, 0.1475), (-0.0245, 0.1445), (-0.0305, 0.139), (-0.0335, 0.131),
    (-0.0335, 0.112), (-0.022, 0.103), (-0.0105, 0.0965),
    (-0.007, 0.088), (-0.007, 0.058), (-0.0115, 0.044),
    (-0.030, 0.0275), (-0.045, 0.020), (-0.0625, 0.0105), (-0.0625, 0.0),
]
FOOT_Z = 0.182
RAIL = 0.7175
RAIL_LEN = 4.2
SLEEPER_DX = [round(i * 0.6, 3) for i in range(-3, 4)]
GROUND = 20.0          # bok planu (m)
TILE_M = 2.0           # 1 kafel tekstury = 2 m (realna skala źdźbeł)

bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
for m in list(bpy.data.materials):
    bpy.data.materials.remove(m)


# ── grunt: PŁASKI plane + wypalona tekstura trawy (kafelkowana) — jak w grze ──
def grass_textured_mat():
    m = bpy.data.materials.new("Ground_GrassTex")
    m.use_nodes = True
    nt = m.node_tree
    n, lk = nt.nodes, nt.links
    bsdf = n.get("Principled BSDF")
    bsdf.inputs["Roughness"].default_value = 0.92
    bsdf.inputs["Metallic"].default_value = 0.0
    tc = n.new("ShaderNodeTexCoord")
    mp = n.new("ShaderNodeMapping")
    reps = GROUND / TILE_M
    mp.inputs["Scale"].default_value = (reps, reps, 1.0)
    lk.new(tc.outputs["UV"], mp.inputs["Vector"])
    img = n.new("ShaderNodeTexImage")
    img.image = bpy.data.images.load(grass_tex)
    img.extension = 'REPEAT'
    lk.new(mp.outputs["Vector"], img.inputs["Vector"])
    lk.new(img.outputs["Color"], bsdf.inputs["Base Color"])
    # lekki bump z luminancji tekstury (delikatny relief)
    bump = n.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.12
    bump.inputs["Distance"].default_value = 0.004
    lk.new(img.outputs["Color"], bump.inputs["Height"])
    lk.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    return m


def steel_mat():
    m = bpy.data.materials.new("Rail_Steel")
    m.use_nodes = True
    nt = m.node_tree
    n, lk = nt.nodes, nt.links
    bsdf = n.get("Principled BSDF")
    tc = n.new("ShaderNodeTexCoord")
    sep = n.new("ShaderNodeSeparateXYZ")
    lk.new(tc.outputs["Object"], sep.inputs["Vector"])
    zmask = n.new("ShaderNodeMapRange")
    zmask.inputs["From Min"].default_value = 0.3285
    zmask.inputs["From Max"].default_value = 0.331
    lk.new(sep.outputs["Z"], zmask.inputs["Value"])
    col = n.new("ShaderNodeMixRGB")
    lk.new(zmask.outputs["Result"], col.inputs[0])
    col.inputs[1].default_value = (0.190, 0.070, 0.025, 1)
    col.inputs[2].default_value = (0.800, 0.820, 0.850, 1)
    lk.new(col.outputs[0], bsdf.inputs["Base Color"])
    lk.new(zmask.outputs["Result"], bsdf.inputs["Metallic"])
    rr = n.new("ShaderNodeMapRange")
    rr.inputs["To Min"].default_value = 0.85
    rr.inputs["To Max"].default_value = 0.07
    lk.new(zmask.outputs["Result"], rr.inputs["Value"])
    lk.new(rr.outputs["Result"], bsdf.inputs["Roughness"])
    return m


def ballast_material():
    m = bpy.data.materials.new("Ballast")
    m.use_nodes = True
    nt = m.node_tree
    n, lk = nt.nodes, nt.links
    out_node = n.get("Material Output")
    bsdf = n.get("Principled BSDF")
    bsdf.inputs["Metallic"].default_value = 0.0
    tc = n.new("ShaderNodeTexCoord")
    nw = n.new("ShaderNodeTexNoise")
    nw.inputs["Scale"].default_value = 12.0
    nw.inputs["Detail"].default_value = 2.0
    lk.new(tc.outputs["Object"], nw.inputs["Vector"])
    mul = n.new("ShaderNodeVectorMath")
    mul.operation = 'MULTIPLY'
    mul.inputs[1].default_value = (0.04, 0.04, 0.04)
    lk.new(nw.outputs["Color"], mul.inputs[0])
    add = n.new("ShaderNodeVectorMath")
    add.operation = 'ADD'
    lk.new(tc.outputs["Object"], add.inputs[0])
    lk.new(mul.outputs["Vector"], add.inputs[1])
    ve = n.new("ShaderNodeTexVoronoi")
    ve.feature = 'DISTANCE_TO_EDGE'
    ve.inputs["Scale"].default_value = 22.0
    lk.new(add.outputs["Vector"], ve.inputs["Vector"])
    vc = n.new("ShaderNodeTexVoronoi")
    vc.feature = 'F1'
    vc.inputs["Scale"].default_value = 22.0
    lk.new(add.outputs["Vector"], vc.inputs["Vector"])
    sepc = n.new("ShaderNodeSeparateColor")
    lk.new(vc.outputs["Color"], sepc.inputs["Color"])
    pal = n.new("ShaderNodeValToRGB")
    cr = pal.color_ramp
    cr.interpolation = 'CONSTANT'
    cr.elements[0].position = 0.0
    cr.elements[0].color = (0.25, 0.23, 0.21, 1)
    cr.elements[1].position = 0.25
    cr.elements[1].color = (0.092, 0.102, 0.116, 1)
    e2 = cr.elements.new(0.45); e2.color = (0.150, 0.075, 0.037, 1)
    e3 = cr.elements.new(0.68); e3.color = (0.039, 0.044, 0.052, 1)
    e4 = cr.elements.new(0.88); e4.color = (0.220, 0.107, 0.052, 1)
    lk.new(sepc.outputs["Red"], pal.inputs["Fac"])
    sepg = n.new("ShaderNodeSeparateColor")
    lk.new(vc.outputs["Color"], sepg.inputs["Color"])
    bval = n.new("ShaderNodeMapRange")
    bval.inputs["To Min"].default_value = 0.65
    bval.inputs["To Max"].default_value = 1.15
    lk.new(sepg.outputs["Green"], bval.inputs["Value"])
    hsv = n.new("ShaderNodeHueSaturation")
    lk.new(pal.outputs["Color"], hsv.inputs["Color"])
    lk.new(bval.outputs["Result"], hsv.inputs["Value"])
    edge = n.new("ShaderNodeValToRGB")
    edge.color_ramp.elements[0].position = 0.0
    edge.color_ramp.elements[0].color = (1, 1, 1, 1)
    edge.color_ramp.elements[1].position = 0.10
    edge.color_ramp.elements[1].color = (0, 0, 0, 1)
    lk.new(ve.outputs["Distance"], edge.inputs["Fac"])
    dust = n.new("ShaderNodeMixRGB")
    lk.new(edge.outputs["Color"], dust.inputs[0])
    lk.new(hsv.outputs["Color"], dust.inputs[1])
    dust.inputs[2].default_value = (0.090, 0.072, 0.055, 1)
    lk.new(dust.outputs[0], bsdf.inputs["Base Color"])
    sepb = n.new("ShaderNodeSeparateColor")
    lk.new(vc.outputs["Color"], sepb.inputs["Color"])
    rgh = n.new("ShaderNodeMapRange")
    rgh.inputs["To Min"].default_value = 0.48
    rgh.inputs["To Max"].default_value = 0.92
    lk.new(sepb.outputs["Blue"], rgh.inputs["Value"])
    lk.new(rgh.outputs["Result"], bsdf.inputs["Roughness"])
    inv = n.new("ShaderNodeMath")
    inv.operation = 'SUBTRACT'
    inv.inputs[0].default_value = 0.5
    lk.new(ve.outputs["Distance"], inv.inputs[1])
    clamp = n.new("ShaderNodeMath")
    clamp.operation = 'MAXIMUM'
    lk.new(inv.outputs[0], clamp.inputs[0])
    clamp.inputs[1].default_value = 0.0
    nmf = n.new("ShaderNodeTexNoise")
    nmf.inputs["Scale"].default_value = 60.0
    nmf.inputs["Detail"].default_value = 6.0
    lk.new(tc.outputs["Object"], nmf.inputs["Vector"])
    addh = n.new("ShaderNodeMath")
    addh.operation = 'MULTIPLY_ADD'
    lk.new(nmf.outputs["Fac"], addh.inputs[0])
    addh.inputs[1].default_value = 0.04
    lk.new(clamp.outputs[0], addh.inputs[2])
    disp = n.new("ShaderNodeDisplacement")
    disp.inputs["Midlevel"].default_value = 0.0
    disp.inputs["Scale"].default_value = 0.06
    lk.new(addh.outputs[0], disp.inputs["Height"])
    lk.new(disp.outputs["Displacement"], out_node.inputs["Displacement"])
    m.displacement_method = 'DISPLACEMENT'
    return m


grass = grass_textured_mat()
steel = steel_mat()

# grunt
bpy.ops.mesh.primitive_plane_add(size=GROUND, location=(0, 0, 0))
ground = bpy.context.active_object
ground.name = "Ground"
ground.data.materials.append(grass)

# podkłady
with bpy.data.libraries.load(sleeper_blend) as (src, dst):
    dst.objects = [nm for nm in src.objects if nm == "Sleeper"]
sleeper = [o for o in dst.objects if o][0]
bpy.context.collection.objects.link(sleeper)
sleeper.location = (SLEEPER_DX[0], 0.0, 0.0)
for dx in SLEEPER_DX[1:]:
    d = sleeper.copy()
    d.location = (dx, 0.0, 0.0)
    bpy.context.collection.objects.link(d)


def build_rail(side):
    bm = bmesh.new()
    P = [(py + side, pz + FOOT_Z) for (py, pz) in PROFILE]
    l0 = [bm.verts.new((-RAIL_LEN / 2, y, z)) for (y, z) in P]
    l1 = [bm.verts.new((RAIL_LEN / 2, y, z)) for (y, z) in P]
    nP = len(P)
    for i in range(nP):
        j = (i + 1) % nP
        bm.faces.new([l0[i], l0[j], l1[j], l1[i]])
    bm.faces.new(l0)
    bm.faces.new(list(reversed(l1)))
    me = bpy.data.meshes.new(f"Rail_{side}")
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(f"Rail_{side}", me)
    bpy.context.collection.objects.link(ob)
    ob.data.materials.append(steel)
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode='OBJECT')


build_rail(RAIL)
build_rail(-RAIL)

XSEC = [(-1.75, 0.0), (-1.45, 0.105), (1.45, 0.105), (1.75, 0.0)]
XL = RAIL_LEN / 2.0
bm = bmesh.new()
l0 = [bm.verts.new((-XL, y, z)) for (y, z) in XSEC]
l1 = [bm.verts.new((XL, y, z)) for (y, z) in XSEC]
for i in range(len(XSEC) - 1):
    bm.faces.new([l0[i], l0[i + 1], l1[i + 1], l1[i]])
me = bpy.data.meshes.new("Ballast")
bm.to_mesh(me)
bm.free()
ballast = bpy.data.objects.new("Ballast", me)
bpy.context.collection.objects.link(ballast)
ballast.data.materials.append(ballast_material())
ballast.select_set(True)
bpy.context.view_layer.objects.active = ballast
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.normals_make_consistent(inside=False)
bpy.ops.object.mode_set(mode='OBJECT')
_bmod = ballast.modifiers.new("Subdiv", 'SUBSURF')
_bmod.subdivision_type = 'SIMPLE'
_bmod.levels = 2
_bmod.render_levels = 2
ballast.cycles.use_adaptive_subdivision = True
ballast.cycles.dicing_rate = 1.0

# ── kamera: kąt zajezdni (~45°), tor + trawa wokół ──
cam_data = bpy.data.cameras.new("Cam")
cam = bpy.data.objects.new("Cam", cam_data)
bpy.context.collection.objects.link(cam)
cam.location = (3.8, -4.8, 4.2)
look = mathutils.Vector((0.0, 0.2, 0.0))
cam.rotation_euler = (look - cam.location).to_track_quat('-Z', 'Y').to_euler()
cam_data.lens = 38
bpy.context.scene.camera = cam

# ── światło zbliżone do gry (ciepłe słońce + niebo), bez przepału ──
sun_d = bpy.data.lights.new("Sun", 'SUN')
sun_d.energy = 3.2
sun_d.color = (1.0, 0.96, 0.86)
sun = bpy.data.objects.new("Sun", sun_d)
bpy.context.collection.objects.link(sun)
sun.rotation_euler = (math.radians(50), math.radians(12), math.radians(35))

world = bpy.context.scene.world or bpy.data.worlds.new("W")
bpy.context.scene.world = world
world.use_nodes = True
_bg = world.node_tree.nodes.get("Background")
if _bg:
    _bg.inputs["Strength"].default_value = 1.0
    _bg.inputs["Color"].default_value = (0.60, 0.66, 0.74, 1)

sc = bpy.context.scene
sc.render.engine = 'CYCLES'
sc.cycles.feature_set = 'EXPERIMENTAL'
sc.cycles.device = 'CPU'
sc.cycles.samples = 64
sc.render.resolution_x = 1280
sc.render.resolution_y = 800
sc.render.filepath = out
bpy.ops.render.render(write_still=True)
print("RENDERED:", out)
