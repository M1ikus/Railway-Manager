"""render_grass_flat.py — wypalona tekstura trawy (Grass_Albedo.png) na PŁASKIM PLANIE,
z bliska jak referencja CBM. To 1:1 reprezentacja gry: flat plane + materiał (shader) z teksturą,
NIE geometria 3D. Tekstura wypalona z blade-geometry (offline), ale w grze = płaska.

UŻYCIE: blender --background --factory-startup --python BlenderSource/scripts/render_grass_flat.py
Wyjście: BlenderSource/Trackwork/GrassFlat_preview.png
"""
import bpy, os, math, mathutils

PROJECT = r"D:\Gry\RM-0.2"
grass_tex = os.path.join(PROJECT, "Assets", "Textures", "Trackwork", "Grass_Albedo.png")
out = os.path.join(PROJECT, "BlenderSource", "Trackwork", "GrassFlat_preview.png")

bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
for m in list(bpy.data.materials):
    bpy.data.materials.remove(m)

PLANE = 4.0
TILE_M = 2.0

m = bpy.data.materials.new("GrassFlat")
m.use_nodes = True
nt = m.node_tree
n, lk = nt.nodes, nt.links
bsdf = n.get("Principled BSDF")
bsdf.inputs["Roughness"].default_value = 0.92
bsdf.inputs["Metallic"].default_value = 0.0
tc = n.new("ShaderNodeTexCoord")
mp = n.new("ShaderNodeMapping")
reps = PLANE / TILE_M
mp.inputs["Scale"].default_value = (reps, reps, 1.0)
lk.new(tc.outputs["UV"], mp.inputs["Vector"])
img = n.new("ShaderNodeTexImage")
img.image = bpy.data.images.load(grass_tex)
img.extension = 'REPEAT'
lk.new(mp.outputs["Vector"], img.inputs["Vector"])
# duże miękkie łaty (macro, niezależne od kafla) — jaśniej/ciemniej jak referencja
macn = n.new("ShaderNodeTexNoise")
macn.inputs["Scale"].default_value = 1.4
macn.inputs["Detail"].default_value = 2.0
lk.new(tc.outputs["Object"], macn.inputs["Vector"])
macv = n.new("ShaderNodeMapRange")
macv.inputs["To Min"].default_value = 0.84
macv.inputs["To Max"].default_value = 1.14
lk.new(macn.outputs["Fac"], macv.inputs["Value"])
mulc = n.new("ShaderNodeMixRGB")
mulc.blend_type = 'MULTIPLY'
mulc.inputs[0].default_value = 1.0
lk.new(img.outputs["Color"], mulc.inputs[1])
lk.new(macv.outputs["Result"], mulc.inputs[2])
lk.new(mulc.outputs["Color"], bsdf.inputs["Base Color"])
bump = n.new("ShaderNodeBump")
bump.inputs["Strength"].default_value = 0.12
bump.inputs["Distance"].default_value = 0.003
lk.new(img.outputs["Color"], bump.inputs["Height"])
lk.new(bump.outputs["Normal"], bsdf.inputs["Normal"])

bpy.ops.mesh.primitive_plane_add(size=PLANE, location=(0, 0, 0))
ground = bpy.context.active_object
ground.data.materials.append(m)

cam_data = bpy.data.cameras.new("Cam")
cam = bpy.data.objects.new("Cam", cam_data)
bpy.context.collection.objects.link(cam)
cam.location = (0.0, -1.4, 1.5)
look = mathutils.Vector((0.0, 0.4, 0.0))
cam.rotation_euler = (look - cam.location).to_track_quat('-Z', 'Y').to_euler()
cam_data.lens = 45
bpy.context.scene.camera = cam

sun_d = bpy.data.lights.new("Sun", 'SUN')
sun_d.energy = 3.2
sun_d.color = (1.0, 0.96, 0.86)
sun = bpy.data.objects.new("Sun", sun_d)
bpy.context.collection.objects.link(sun)
sun.rotation_euler = (math.radians(52), math.radians(12), math.radians(32))

world = bpy.context.scene.world or bpy.data.worlds.new("W")
bpy.context.scene.world = world
world.use_nodes = True
_bg = world.node_tree.nodes.get("Background")
if _bg:
    _bg.inputs["Strength"].default_value = 1.0
    _bg.inputs["Color"].default_value = (0.62, 0.67, 0.74, 1)

sc = bpy.context.scene
sc.render.engine = 'CYCLES'
sc.cycles.device = 'CPU'
sc.cycles.samples = 64
sc.render.resolution_x = 1280
sc.render.resolution_y = 760
sc.render.filepath = out
bpy.ops.render.render(write_still=True)
print("RENDERED:", out)
