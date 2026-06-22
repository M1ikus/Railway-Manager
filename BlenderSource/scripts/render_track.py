"""render_track.py — podgląd fragmentu toru: podkłady (append Sleeper.blend) + 2 szyny S49.
Pokazuje wpasowanie węzła K na stopce szyny. Szyny biegną wzdłuż X (poprzek długiej osi podkładu)."""
import bpy, bmesh, os, math, mathutils

PROJECT = r"D:\Gry\RM-0.2"
sleeper_blend = os.path.join(PROJECT, "BlenderSource", "Trackwork", "Sleeper.blend")
out = os.path.join(PROJECT, "BlenderSource", "Trackwork", "Track_preview.png")

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
RAIL_LEN = 1.7   # wzdłuż X, pokrywa 3 podkłady

bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)

# ── append Sleeper z .blend + 2 duplikaty (fragment toru, spacing 0.6) ──
with bpy.data.libraries.load(sleeper_blend) as (src, dst):
    dst.objects = [nm for nm in src.objects if nm == "Sleeper"]
linked = [o for o in dst.objects if o]
sleeper = linked[0]
bpy.context.collection.objects.link(sleeper)
sleeper.location = (0.0, 0.0, 0.0)
for dx in (-0.6, 0.6):
    d = sleeper.copy()
    d.location = (dx, 0.0, 0.0)
    bpy.context.collection.objects.link(d)


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
    zmask.inputs["From Min"].default_value = 0.3285  # tylko sam czubek główki (~3 mm pasek)
    zmask.inputs["From Max"].default_value = 0.331
    lk.new(sep.outputs["Z"], zmask.inputs["Value"])
    col = n.new("ShaderNodeMixRGB")
    lk.new(zmask.outputs["Result"], col.inputs[0])
    col.inputs[1].default_value = (0.190, 0.070, 0.025, 1)   # rdza
    col.inputs[2].default_value = (0.800, 0.820, 0.850, 1)   # jasny srebrny wyślizg
    lk.new(col.outputs[0], bsdf.inputs["Base Color"])
    lk.new(zmask.outputs["Result"], bsdf.inputs["Metallic"])
    rr = n.new("ShaderNodeMapRange")
    rr.inputs["To Min"].default_value = 0.85
    rr.inputs["To Max"].default_value = 0.07   # lustrzany highlight
    lk.new(zmask.outputs["Result"], rr.inputs["Value"])
    lk.new(rr.outputs["Result"], bsdf.inputs["Roughness"])
    return m


steel = steel_mat()


# ── 2 szyny wzdłuż X, przekrój w YZ, na Y=±RAIL, stopka z=FOOT_Z ──
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


# ── podsypka: tłuczeń granit/bazalt (Object coords w metrach, ziarno ~0.04m) ──
def ballast_material():
    """v3: paleta reference (cool łupek + warm rdza) + połysk per-cell + displacement.
    Object coords (metry) — ziarno ~0.045 m niezależnie od rozmiaru nasypu."""
    m = bpy.data.materials.new("Ballast")
    m.use_nodes = True
    nt = m.node_tree
    n, lk = nt.nodes, nt.links
    out = n.get("Material Output")
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
    cr.elements[0].color = (0.25, 0.23, 0.21, 1)     # jasnoszary
    cr.elements[1].position = 0.25
    cr.elements[1].color = (0.092, 0.102, 0.116, 1)  # niebiesko-szary
    e2 = cr.elements.new(0.45)
    e2.color = (0.150, 0.075, 0.037, 1)              # brązowo-rdzawy
    e3 = cr.elements.new(0.68)
    e3.color = (0.039, 0.044, 0.052, 1)              # ciemny łupek
    e4 = cr.elements.new(0.88)
    e4.color = (0.220, 0.107, 0.052, 1)              # rdzawy akcent
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
    # ODWRÓCONE distance-to-edge (0.5 - dist): środek komórki = szczyt WYPUKŁY, krawędź = spoina
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
    lk.new(disp.outputs["Displacement"], out.inputs["Displacement"])
    m.displacement_method = 'DISPLACEMENT'
    return m


# nasyp 3D (przekrój trapezowy): dół szeroki 3,5 m, korona 2,9 m na z=0,13 (wchodzi na
# boki podkładów, ale poniżej ich wierzchu 0,16), skarpy boczne. Extrude wzdłuż X (tor demo).
XSEC = [(-1.75, 0.0), (-1.45, 0.105), (1.45, 0.105), (1.75, 0.0)]   # korona 0,105 (podkład 0,16 wystaje ~0,055)
XL = 1.15
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
# adaptive subdivision dla displacement kamieni na nasypie
_bmod = ballast.modifiers.new("Subdiv", 'SUBSURF')
_bmod.subdivision_type = 'SIMPLE'
_bmod.levels = 2
_bmod.render_levels = 2
ballast.cycles.use_adaptive_subdivision = True
ballast.cycles.dicing_rate = 1.0

# ── kamera / światło / render ──
cam_data = bpy.data.cameras.new("Cam")
cam = bpy.data.objects.new("Cam", cam_data)
bpy.context.collection.objects.link(cam)
cam.location = (1.55, -1.45, 0.70)   # widać podsypkę między podkładami + wypukłość + skarpę boczną
look = mathutils.Vector((-0.1, 0.50, 0.03))
cam.rotation_euler = (look - cam.location).to_track_quat('-Z', 'Y').to_euler()
cam_data.lens = 40
bpy.context.scene.camera = cam

sun_d = bpy.data.lights.new("Sun", 'SUN')
sun_d.energy = 4.5
sun = bpy.data.objects.new("Sun", sun_d)
bpy.context.collection.objects.link(sun)
sun.rotation_euler = (math.radians(52), math.radians(12), math.radians(40))

world = bpy.context.scene.world or bpy.data.worlds.new("W")
bpy.context.scene.world = world
world.use_nodes = True
_bg = world.node_tree.nodes.get("Background")
if _bg:
    _bg.inputs["Strength"].default_value = 1.1
    _bg.inputs["Color"].default_value = (0.55, 0.60, 0.68, 1)

sc = bpy.context.scene
sc.render.engine = 'CYCLES'
sc.cycles.feature_set = 'EXPERIMENTAL'   # adaptive subdivision (displacement nasypu)
sc.cycles.device = 'CPU'
sc.cycles.samples = 56
sc.render.resolution_x = 1200
sc.render.resolution_y = 780
sc.render.filepath = out
bpy.ops.render.render(write_still=True)
print("RENDERED:", out)
