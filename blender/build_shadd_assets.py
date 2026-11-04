"""Shadd — procedural asset builder for Blender 3.6+/4.x.

Run:  blender --background --python blender/build_shadd_assets.py -- --radius 4 --out ./export
Builds the hex board, a peg, a scoring piece and a band, then exports each to FBX
(Unity-ready: Y-up, -Z forward, scale 1 = 1 peg spacing).
"""
import argparse, math, os, sys
import bpy, bmesh

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ap = argparse.ArgumentParser()
ap.add_argument("--radius", type=int, default=4)
ap.add_argument("--out", default=os.path.join(os.getcwd(), "export"))
args = ap.parse_args(argv)
os.makedirs(args.out, exist_ok=True)

S = 1.0  # peg spacing


def reset():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()
    for block in (bpy.data.meshes, bpy.data.materials):
        for b in list(block):
            block.remove(b)


def material(name, rgb, rough=0.5):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*rgb, 1)
    bsdf.inputs["Roughness"].default_value = rough
    return m


def axial(q, r):  # Blender is Z-up: board lies in XY
    return ((q + r / 2) * S, r * S * math.sqrt(3) / 2)


def build_board(R):
    rad = R * S + 0.75
    bm = bmesh.new()
    verts = [bm.verts.new((math.cos(i * math.pi / 3) * rad, math.sin(i * math.pi / 3) * rad, 0)) for i in range(6)]
    face = bm.faces.new(verts)
    ext = bmesh.ops.extrude_face_region(bm, geom=[face])
    bmesh.ops.translate(bm, vec=(0, 0, -0.3), verts=[v for v in ext["geom"] if isinstance(v, bmesh.types.BMVert)])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    # Y-shaped sockets: inset + shallow dent at every triangle centroid
    me = bpy.data.meshes.new("Board")
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new("Board", me)
    bpy.context.collection.objects.link(ob)
    bev = ob.modifiers.new("Bevel", "BEVEL")
    bev.width, bev.segments = 0.1, 4
    ob.data.materials.append(material("BoardMat", (0.012, 0.013, 0.015), 0.62))
    # peg sockets as a boolean-free decal grid (instanced small cylinders, joined)
    for q in range(-R, R + 1):
        for r in range(-R, R + 1):
            if abs(q + r) > R:
                continue
            x, y = axial(q, r)
            bpy.ops.mesh.primitive_cylinder_add(vertices=14, radius=0.095, depth=0.02, location=(x, y, 0.005))
            bpy.context.object.name = f"Socket_{q}_{r}"
            bpy.context.object.parent = ob
    return ob


def build_peg():
    bpy.ops.mesh.primitive_cone_add(vertices=14, radius1=0.095, radius2=0.075, depth=0.3, location=(0, 0, 0.15))
    peg = bpy.context.object
    peg.name = "Peg"
    bpy.ops.mesh.primitive_uv_sphere_add(segments=14, ring_count=8, radius=0.075, location=(0, 0, 0.3))
    cap = bpy.context.object
    cap.select_set(True)
    peg.select_set(True)
    bpy.context.view_layer.objects.active = peg
    bpy.ops.object.join()
    bpy.ops.object.shade_smooth()
    peg.data.materials.append(material("PegMat", (0.025, 0.028, 0.034), 0.45))
    return peg


def build_piece():
    # triangular frustum "tooth" — vertex 0 points +Y (Unity +Z after export)
    bpy.ops.mesh.primitive_cone_add(vertices=3, radius1=0.27, radius2=0.14, depth=0.3, location=(0, 0, 0.15))
    p = bpy.context.object
    p.name = "Piece"
    p.rotation_euler[2] = math.pi / 2
    bpy.ops.object.transform_apply(rotation=True)
    b = p.modifiers.new("Bevel", "BEVEL")
    b.width, b.segments = 0.025, 3
    p.data.materials.append(material("PieceMat", (0.8, 0.8, 0.8), 0.38))  # tinted per player in Unity
    return p


def build_band(length=3 * S, r=0.12, thick=0.022):
    pts = []
    for i in range(17):
        a = math.pi / 2 + i / 16 * math.pi
        pts.append((math.cos(a) * r, math.sin(a) * r, 0))
    for i in range(17):
        a = -math.pi / 2 + i / 16 * math.pi
        pts.append((length + math.cos(a) * r, math.sin(a) * r, 0))
    cu = bpy.data.curves.new("BandCurve", "CURVE")
    cu.dimensions, cu.bevel_depth, cu.bevel_resolution = "3D", thick, 2
    sp = cu.splines.new("POLY")
    sp.points.add(len(pts) - 1)
    for i, (x, y, z) in enumerate(pts):
        sp.points[i].co = (x, y, z, 1)
    sp.use_cyclic_u = True
    ob = bpy.data.objects.new("Band", cu)
    bpy.context.collection.objects.link(ob)
    ob.data.materials.append(material("BandMat", (0.95, 0.92, 0.86), 0.55))
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    bpy.ops.object.convert(target="MESH")
    return ob


def export(ob, name):
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    for c in ob.children:
        c.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.export_scene.fbx(filepath=os.path.join(args.out, name + ".fbx"), use_selection=True,
                             apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
                             mesh_smooth_type="FACE", use_mesh_modifiers=True)


reset()
board = build_board(args.radius)
export(board, f"Board_R{args.radius}")
for builder, name in ((build_peg, "Peg"), (build_piece, "Piece"), (build_band, "Band_L3")):
    ob = builder()
    ob.location.x += 20  # keep away from board while exporting
    export(ob, name)
print("Shadd assets exported to", args.out)
