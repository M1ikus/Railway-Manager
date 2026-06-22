"""render_sleeper.py — szybki podgląd Workbench wygenerowanego podkładu (do oceny kształtu)."""
import bpy, os, math, mathutils

PROJECT = r"D:\Gry\RM-0.2"
blend = os.path.join(PROJECT, "BlenderSource", "Trackwork", "Sleeper.blend")
out = os.path.join(PROJECT, "BlenderSource", "Trackwork", "Sleeper_preview.png")

bpy.ops.wm.open_mainfile(filepath=blend)

# kolory viewport dla Workbench (color_type=MATERIAL używa diffuse_color)
for m in bpy.data.materials:
    bsdf = m.node_tree.nodes.get("Principled BSDF") if m.use_nodes else None
    if bsdf:
        m.diffuse_color = bsdf.inputs["Base Color"].default_value

# kamera 3/4 z góry
cam_data = bpy.data.cameras.new("Cam")
cam = bpy.data.objects.new("Cam", cam_data)
bpy.context.collection.objects.link(cam)
cam.location = (2.4, -2.4, 1.7)
look = mathutils.Vector((0, 0, 0.12))
cam.rotation_euler = (look - cam.location).to_track_quat('-Z', 'Y').to_euler()
cam_data.lens = 60
bpy.context.scene.camera = cam

# światło (sun) — Cycles pokazuje procedural shader (Workbench nie)
sun_d = bpy.data.lights.new("Sun", 'SUN')
sun_d.energy = 4.5
sun = bpy.data.objects.new("Sun", sun_d)
bpy.context.collection.objects.link(sun)
sun.rotation_euler = (__import__('math').radians(50), __import__('math').radians(15), __import__('math').radians(35))

# ambient (world) — rozjaśnia cienie żeby tekstura była czytelna do oceny
world = bpy.context.scene.world or bpy.data.worlds.new("W")
bpy.context.scene.world = world
world.use_nodes = True
_bg = world.node_tree.nodes.get("Background")
if _bg:
    _bg.inputs["Strength"].default_value = 1.2
    _bg.inputs["Color"].default_value = (0.55, 0.60, 0.68, 1)

sc = bpy.context.scene
sc.render.engine = 'CYCLES'
sc.cycles.device = 'CPU'
sc.cycles.samples = 48
sc.render.resolution_x = 1000
sc.render.resolution_y = 640
sc.render.filepath = out
bpy.ops.render.render(write_still=True)
print("RENDERED:", out)
