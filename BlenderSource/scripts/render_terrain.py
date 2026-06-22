"""render_terrain.py — podgląd TERENU (utility/dzika trawa) razem z fragmentem toru.

Grunt zajezdni = tile-able material (procedural). Tu tylko PROOF WYGLĄDU + harmonii
grunt<->tor (rail+sleeper+ballast z istniejących assetów). Docelowo material przejdzie
do gen_terrain.py + bake_terrain.py (Albedo/Normal/MetallicSmoothness, seamless 4D).

Trawa stylizowana CBM (decyzja user 2026-06-21): "utility/dzika" — odsycona zieleń +
suche khaki łaty (macro noise) + rzadkie łysiny ubitej ziemi + pojedyncze ziarna żwiru
na łysinach + kępki (bump). Paleta tonalnie spokrewniona z podsypką (brąz/szarość).

UŻYCIE: blender --background --factory-startup --python BlenderSource/scripts/render_terrain.py
Wyjście: BlenderSource/Trackwork/Terrain_preview.png
"""
import bpy, bmesh, os, math, mathutils

PROJECT = r"D:\Gry\RM-0.2"
sleeper_blend = os.path.join(PROJECT, "BlenderSource", "Trackwork", "Sleeper.blend")
out = os.path.join(PROJECT, "BlenderSource", "Trackwork", "Terrain_preview.png")

# profil S49 (z gen_rail/render_track)
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
RAIL_LEN = 4.2            # dłuższy odcinek toru — widać teren wokół
SLEEPER_DX = [round(i * 0.6, 3) for i in range(-3, 4)]   # 7 podkładów, spacing 0.6 m

bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
for m in list(bpy.data.materials):
    bpy.data.materials.remove(m)


# ════════════════════════════ TRAWA (utility/dzika) ════════════════════════════
def grass_material():
    """CBM-style PŁASKA trawa-dywan (target = screen CBM, NIE 3D źdźbła FS25): kierunkowe,
    drobne ŹDŹBŁA przez anizotropowy (rozciągnięty) noise w 2 splecionych kierunkach +
    delikatna macro-mottling, NISKI kontrast, matowa, subtelny kierunkowy bump. BEZ komórek
    Voronoi/przerw (to dawało mszysty/kępkowy wygląd). Object coords (metry)."""
    m = bpy.data.materials.new("Terrain_Grass")
    m.use_nodes = True
    nt = m.node_tree
    n, lk = nt.nodes, nt.links
    bsdf = n.get("Principled BSDF")
    bsdf.inputs["Metallic"].default_value = 0.0
    tc = n.new("ShaderNodeTexCoord")

    # ── WARP coords (źdźbła organiczne, nie idealne pasma) ──
    wn = n.new("ShaderNodeTexNoise")
    wn.inputs["Scale"].default_value = 5.0
    wn.inputs["Detail"].default_value = 3.0
    lk.new(tc.outputs["Object"], wn.inputs["Vector"])
    wmul = n.new("ShaderNodeVectorMath")
    wmul.operation = 'MULTIPLY'
    wmul.inputs[1].default_value = (0.12, 0.12, 0.12)
    lk.new(wn.outputs["Color"], wmul.inputs[0])
    warp = n.new("ShaderNodeVectorMath")
    warp.operation = 'ADD'
    lk.new(tc.outputs["Object"], warp.inputs[0])
    lk.new(wmul.outputs["Vector"], warp.inputs[1])

    # ── ŹDŹBŁA w-wa 1: Wave pasma (profil TRI) — WYRAŹNE kierunkowe smugi ──
    map1 = n.new("ShaderNodeMapping")
    map1.inputs["Scale"].default_value = (1.0, 0.35, 1.0)
    lk.new(warp.outputs["Vector"], map1.inputs["Vector"])
    wave1 = n.new("ShaderNodeTexWave")
    wave1.wave_type = 'BANDS'
    wave1.bands_direction = 'X'
    wave1.wave_profile = 'TRI'
    wave1.inputs["Scale"].default_value = 12.0
    wave1.inputs["Distortion"].default_value = 9.0
    wave1.inputs["Detail"].default_value = 3.0
    wave1.inputs["Detail Scale"].default_value = 1.4
    lk.new(map1.outputs["Vector"], wave1.inputs["Vector"])

    # ── ŹDŹBŁA w-wa 2: inny kierunek (diagonal) + inna skala (rozbicie jednorodności) ──
    map2 = n.new("ShaderNodeMapping")
    map2.inputs["Rotation"].default_value = (0.0, 0.0, math.radians(35))
    map2.inputs["Scale"].default_value = (1.0, 0.40, 1.0)
    lk.new(warp.outputs["Vector"], map2.inputs["Vector"])
    wave2 = n.new("ShaderNodeTexWave")
    wave2.wave_type = 'BANDS'
    wave2.bands_direction = 'X'
    wave2.wave_profile = 'TRI'
    wave2.inputs["Scale"].default_value = 9.0
    wave2.inputs["Distortion"].default_value = 7.0
    wave2.inputs["Detail"].default_value = 3.0
    lk.new(map2.outputs["Vector"], wave2.inputs["Vector"])

    blades = n.new("ShaderNodeMixRGB")
    blades.inputs[0].default_value = 0.5
    lk.new(wave1.outputs["Fac"], blades.inputs[1])
    lk.new(wave2.outputs["Fac"], blades.inputs[2])

    # kontrast źdźbeł (ciemny cień u podstawy -> jasny oświetlony czubek)
    bramp = n.new("ShaderNodeValToRGB")
    bramp.color_ramp.elements[0].position = 0.30
    bramp.color_ramp.elements[1].position = 0.72
    lk.new(blades.outputs["Color"], bramp.inputs["Fac"])

    # drobny szum (micro-detal koloru + bump)
    fine = n.new("ShaderNodeTexNoise")
    fine.inputs["Scale"].default_value = 45.0
    fine.inputs["Detail"].default_value = 6.0
    lk.new(tc.outputs["Object"], fine.inputs["Vector"])

    # macro mottling (łaty jaśniej/ciemniej)
    macro = n.new("ShaderNodeTexNoise")
    macro.inputs["Scale"].default_value = 0.50
    macro.inputs["Detail"].default_value = 2.0
    lk.new(tc.outputs["Object"], macro.inputs["Vector"])

    # ── fac koloru = źdźbła(0.78) + fine(0.10) + macro(0.12) ──
    fa = n.new("ShaderNodeMath")
    fa.operation = 'MULTIPLY'
    lk.new(bramp.outputs["Color"], fa.inputs[0])
    fa.inputs[1].default_value = 0.78
    fb = n.new("ShaderNodeMath")
    fb.operation = 'MULTIPLY_ADD'
    lk.new(fine.outputs["Fac"], fb.inputs[0])
    fb.inputs[1].default_value = 0.10
    lk.new(fa.outputs["Value"], fb.inputs[2])
    fc = n.new("ShaderNodeMath")
    fc.operation = 'MULTIPLY_ADD'
    lk.new(macro.outputs["Fac"], fc.inputs[0])
    fc.inputs[1].default_value = 0.12
    lk.new(fb.outputs["Value"], fc.inputs[2])

    # ── COLOR: ciemna zieleń (cień) -> jasna żółto-oliwka (czubki źdźbeł) ──
    col = n.new("ShaderNodeMixRGB")
    lk.new(fc.outputs["Value"], col.inputs[0])
    col.inputs[1].default_value = (0.030, 0.052, 0.018, 1)   # cień/podstawa (linear)
    col.inputs[2].default_value = (0.135, 0.150, 0.058, 1)   # czubki, jaśniejsza żółto-oliwka
    lk.new(col.outputs["Color"], bsdf.inputs["Base Color"])

    # ── ROUGHNESS: matowa, lekka wariacja ──
    rgh = n.new("ShaderNodeMapRange")
    rgh.inputs["To Min"].default_value = 0.82
    rgh.inputs["To Max"].default_value = 0.95
    lk.new(blades.outputs["Color"], rgh.inputs["Value"])
    lk.new(rgh.outputs["Result"], bsdf.inputs["Roughness"])

    # ── BUMP: relief źdźbeł (wave) + drobny detal ──
    h1 = n.new("ShaderNodeMath")
    h1.operation = 'MULTIPLY'
    lk.new(bramp.outputs["Color"], h1.inputs[0])
    h1.inputs[1].default_value = 0.7
    h2 = n.new("ShaderNodeMath")
    h2.operation = 'MULTIPLY_ADD'
    lk.new(fine.outputs["Fac"], h2.inputs[0])
    h2.inputs[1].default_value = 0.3
    lk.new(h1.outputs["Value"], h2.inputs[2])
    bump = n.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.38
    bump.inputs["Distance"].default_value = 0.010
    lk.new(h2.outputs["Value"], bump.inputs["Height"])
    lk.new(bump.outputs["Normal"], bsdf.inputs["Normal"])

    m.diffuse_color = (0.07, 0.10, 0.04, 1)
    return m


# ════════════════════════════ SZYNA (reuse render_track) ════════════════════════════
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


# ════════════════════════════ PODSYPKA (reuse render_track v3) ════════════════════════════
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


# ════════════════════════════ SCENA ════════════════════════════
grass = grass_material()
steel = steel_mat()

# --- grunt: plane 16 m z trawą ---
bpy.ops.mesh.primitive_plane_add(size=40.0, location=(0, 0, 0))
ground = bpy.context.active_object
ground.name = "Ground"
ground.data.materials.append(grass)

# --- podkłady: append z .blend + duplikaty wzdłuż X ---
with bpy.data.libraries.load(sleeper_blend) as (src, dst):
    dst.objects = [nm for nm in src.objects if nm == "Sleeper"]
sleeper = [o for o in dst.objects if o][0]
bpy.context.collection.objects.link(sleeper)
sleeper.location = (SLEEPER_DX[0], 0.0, 0.0)
for dx in SLEEPER_DX[1:]:
    d = sleeper.copy()
    d.location = (dx, 0.0, 0.0)
    bpy.context.collection.objects.link(d)


# --- 2 szyny wzdłuż X, przekrój YZ, na Y=±RAIL, stopka z=FOOT_Z ---
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

# --- nasyp podsypki (przekrój trapezowy) wzdłuż X ---
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

# --- kamera (kąt zajezdni ~45°, widać teren wokół toru) ---
cam_data = bpy.data.cameras.new("Cam")
cam = bpy.data.objects.new("Cam", cam_data)
bpy.context.collection.objects.link(cam)
cam.location = (3.0, 0.6, 2.0)   # GRASS-ONLY blisko, płytki kąt — żeby źdźbła czytały się jak CBM screen 1
look = mathutils.Vector((4.0, 2.6, 0.0))
cam.rotation_euler = (look - cam.location).to_track_quat('-Z', 'Y').to_euler()
cam_data.lens = 50
bpy.context.scene.camera = cam

# --- słońce popołudniowe (jak render_track) ---
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
sc.cycles.feature_set = 'EXPERIMENTAL'   # adaptive subdivision (displacement podsypki)
sc.cycles.device = 'CPU'
sc.cycles.samples = 64
sc.render.resolution_x = 1280
sc.render.resolution_y = 800
sc.render.filepath = out
bpy.ops.render.render(write_still=True)
print("RENDERED:", out)
