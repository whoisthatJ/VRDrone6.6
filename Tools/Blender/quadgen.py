"""
Ushqyr quad part generator (Phase 3, P2). Run headless:

    blender -b --factory-startup -P Tools/Blender/quadgen.py -- <parts.json> <out_dir>

parts.json is written by the Unity exporter (Ushqyr > Quads > Generate Quad Parts) from the garage part data, so
every mesh matches the physics. Each part is modelled in Blender (X right, Y forward, Z up, metres) with bevels,
smooth shading and UVs, then written as <out_dir>/<id>.json: vertices, normals and UVs in Unity axes (x, z, y) plus
one index list per material slot (Carbon, Edge, Metal, Bell, Copper, Prop, PropBlur, Lipo, Plastic, Led, Lens, Pad,
TPU, PCB, Wire, WireRed, Rubber, Plug, Cap) and named sockets. The Unity importer turns them into mesh assets.
Generic construction only: no real brands or product designs.
"""
import bpy, bmesh, json, math, os, sys, random
from mathutils import Vector, Matrix

MM = 0.001


# ---------------------------------------------------------------- scene helpers

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def material(name):
    return bpy.data.materials.get(name) or bpy.data.materials.new(name)


def link(name, bm, slot, bevel=0.0, segments=2, smooth=True, angle=35.0):
    # Outlines come in either winding: make closed shells face outwards.
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    ob.data.materials.append(material(slot))
    if bevel > 0.0:
        md = ob.modifiers.new("bevel", "BEVEL")
        md.width = bevel
        md.segments = segments
        md.limit_method = "ANGLE"
        md.angle_limit = math.radians(40)
        md.harden_normals = True
    if smooth:
        for p in me.polygons:
            p.use_smooth = True
        me.use_auto_smooth = True
        me.auto_smooth_angle = math.radians(angle)
    return ob


def prism(points, z0, h):
    """A vertical prism from a 2D outline (counter-clockwise, metres)."""
    bm = bmesh.new()
    bot = [bm.verts.new((x, y, z0)) for x, y in points]
    top = [bm.verts.new((x, y, z0 + h)) for x, y in points]
    bm.faces.new(list(reversed(bot)))
    bm.faces.new(top)
    n = len(points)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((bot[i], bot[j], top[j], top[i]))
    bm.normal_update()
    return bm


def cylinder(r, h, c=(0, 0, 0), segs=24, r2=None, axis="Z"):
    bm = bmesh.new()
    rot = Matrix.Identity(4)
    if axis == "Y":
        rot = Matrix.Rotation(math.radians(-90), 4, "X")
    elif axis == "X":
        rot = Matrix.Rotation(math.radians(90), 4, "Y")
    m = Matrix.Translation(Vector(c)) @ rot @ Matrix.Translation((0, 0, h / 2))
    bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=r, radius2=r if r2 is None else r2, depth=h, matrix=m)
    return bm


def box(c, s, rz=0.0, rx=0.0):
    bm = bmesh.new()
    m = Matrix.Translation(Vector(c)) @ Matrix.Rotation(rz, 4, "Z") @ Matrix.Rotation(rx, 4, "X") @ Matrix.Diagonal((s[0], s[1], s[2], 1))
    bmesh.ops.create_cube(bm, size=1.0, matrix=m)
    return bm


def tube(r_out, r_in, h, c=(0, 0, 0), segs=40):
    """An open-ended ring (duct wall, motor bell skirt)."""
    bm = bmesh.new()
    outs, ins = [], []
    for k in range(segs):
        a = 2 * math.pi * k / segs
        ca, sa = math.cos(a), math.sin(a)
        outs.append((bm.verts.new((c[0] + ca * r_out, c[1] + sa * r_out, c[2])), bm.verts.new((c[0] + ca * r_out, c[1] + sa * r_out, c[2] + h))))
        ins.append((bm.verts.new((c[0] + ca * r_in, c[1] + sa * r_in, c[2])), bm.verts.new((c[0] + ca * r_in, c[1] + sa * r_in, c[2] + h))))
    for k in range(segs):
        j = (k + 1) % segs
        bm.faces.new((outs[k][0], outs[j][0], outs[j][1], outs[k][1]))
        bm.faces.new((ins[j][0], ins[k][0], ins[k][1], ins[j][1]))
        bm.faces.new((outs[j][1], outs[k][1], ins[k][1], ins[j][1])[::-1])
        bm.faces.new((outs[k][0], outs[j][0], ins[j][0], ins[k][0])[::-1])
    bm.normal_update()
    return bm


def tube_path(points, r, slot, name, segs=8):
    """A wire along a 3D polyline."""
    bm = bmesh.new()
    rings = []
    for i, p in enumerate(points):
        p = Vector(p)
        d = (Vector(points[min(i + 1, len(points) - 1)]) - Vector(points[max(i - 1, 0)])).normalized()
        up = Vector((0, 0, 1)) if abs(d.z) < 0.9 else Vector((1, 0, 0))
        u = d.cross(up).normalized()
        w = d.cross(u).normalized()
        rings.append([bm.verts.new(p + (u * math.cos(2 * math.pi * k / segs) + w * math.sin(2 * math.pi * k / segs)) * r) for k in range(segs)])
    for a, b in zip(rings, rings[1:]):
        for k in range(segs):
            j = (k + 1) % segs
            bm.faces.new((a[k], a[j], b[j], b[k]))
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])
    bm.normal_update()
    return link(name, bm, slot, angle=60)


def cut(target, cutter_bms):
    """Boolean difference of several cutter meshes from target."""
    for i, bm in enumerate(cutter_bms):
        c = link(f"cut{i}", bm, "Carbon", smooth=False)
        md = target.modifiers.new(f"bool{i}", "BOOLEAN")
        md.operation = "DIFFERENCE"
        md.solver = "EXACT"
        md.object = c
        c.hide_render = True
        c.hide_viewport = True
        c["cutter"] = True


def rounded_rect(w, l, r, segs=6, cx=0.0, cy=0.0):
    r = min(r, w / 2 - 1e-5, l / 2 - 1e-5)
    pts = []
    for (sx, sy, a0) in ((1, 1, 0), (-1, 1, 90), (-1, -1, 180), (1, -1, 270)):
        ox, oy = cx + sx * (w / 2 - r), cy + sy * (l / 2 - r)
        for k in range(segs + 1):
            a = math.radians(a0 + 90 * k / segs)
            pts.append((ox + r * math.cos(a), oy + r * math.sin(a)))
    return pts


def capsule_arm(x0, y0, x1, y1, w, pad, segs=14):
    """An arm outline from (x0, y0) to a round motor pad at (x1, y1)."""
    d = Vector((x1 - x0, y1 - y0))
    L = d.length
    d.normalize()
    n = Vector((-d.y, d.x))
    pts = []
    a0 = math.atan2(d.y, d.x)
    root = Vector((x0, y0))
    tip = Vector((x1, y1))
    pts.append(root - n * w / 2)
    # Flare from the arm width out to the pad.
    pts.append(tip - d * pad * 1.6 - n * w / 2)
    for k in range(segs + 1):
        a = a0 - math.pi / 2 - math.radians(35) + (math.pi + math.radians(70)) * k / segs
        pts.append(tip + Vector((math.cos(a), math.sin(a))) * pad)
    pts.append(tip - d * pad * 1.6 + n * w / 2)
    pts.append(root + n * w / 2)
    return [(p.x, p.y) for p in pts]


# ---------------------------------------------------------------- export

def export(part_id, out_dir, sockets=None):
    bpy.context.view_layer.update()
    deps = bpy.context.evaluated_depsgraph_get()
    verts, norms, uvs = [], [], []
    subs = {}
    for ob in list(bpy.context.scene.objects):
        if ob.type != "MESH" or ob.get("cutter"):
            continue
        # Smart UVs on the evaluated mesh (bevels and booleans applied).
        ev = ob.evaluated_get(deps)
        me = bpy.data.meshes.new_from_object(ev, preserve_all_data_layers=True, depsgraph=deps)
        me.transform(ob.matrix_world)
        bm = bmesh.new()
        bm.from_mesh(me)
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
        bm.to_mesh(me)
        bm.free()
        if not me.uv_layers:
            me.uv_layers.new()
        tmp = bpy.data.objects.new("uv_tmp", me)
        bpy.context.scene.collection.objects.link(tmp)
        bpy.context.view_layer.objects.active = tmp
        for o in bpy.context.scene.objects:
            o.select_set(o == tmp)
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.002, scale_to_bounds=False)
        bpy.ops.object.mode_set(mode="OBJECT")
        me.calc_normals_split()
        uv = me.uv_layers.active.data
        for poly in me.polygons:
            slot = me.materials[poly.material_index].name if me.materials else "Plastic"
            idx = subs.setdefault(slot, [])
            for li in poly.loop_indices:
                loop = me.loops[li]
                v = me.vertices[loop.vertex_index].co
                n = loop.normal
                verts += [round(v.x, 6), round(v.z, 6), round(v.y, 6)]
                norms += [round(n.x, 4), round(n.z, 4), round(n.y, 4)]
                uvs += [round(uv[li].uv.x, 5), round(uv[li].uv.y, 5)]
                idx.append(len(verts) // 3 - 1)
        bpy.data.objects.remove(tmp)
    data = {
        "id": part_id,
        "vertices": verts, "normals": norms, "uvs": uvs,
        "submeshes": [{"material": k, "indices": v} for k, v in subs.items()],
        "sockets": [{"name": k, "position": [v[0], v[2], v[1]]} for k, v in (sockets or {}).items()],
    }
    with open(os.path.join(out_dir, part_id + ".json"), "w") as f:
        json.dump(data, f, separators=(",", ":"))
    return len(verts) // 3


# ---------------------------------------------------------------- parts

def frame(p):
    """Bottom plate + arms (or the ducted base), top plate, standoffs, camera side plates, screws, TPU parts."""
    ax, az = p["armX"], p["armZ"]          # Unity x / z of the motors -> Blender x / y
    plate, armW, bodyL, bodyW, stack = p["plate"], p["armW"], p["bodyL"], p["bodyW"], p["stack"]
    geo = p["geometry"]
    motors = [(ax, -az), (ax, az), (-ax, -az), (-ax, az)]
    mount = p["mount"] * MM / 2
    hole = (1.0 if p["mountScrew"] <= 2 else 1.6) * MM
    pad = max(mount * 1.5, armW * 0.62)
    ducted = geo == "Ducted"
    sockets = {}
    for i, (mx, my) in enumerate(motors):
        sockets[f"motor{i}"] = (mx, my, plate)

    if ducted:
        prop_r = p["propR"]
        duct_h = p["ductH"]
        base_pts = rounded_rect(bodyW, bodyL, bodyW * 0.18)
        base = link("base", prism(base_pts, 0, plate), "Carbon", bevel=0.25 * MM)
        for (mx, my) in motors:
            arm = link("arm", prism(capsule_arm(0, 0, mx, my, armW * 1.4, pad * 1.1), 0, plate), "Carbon", bevel=0.25 * MM)
            cut(arm, [cylinder(hole, plate * 3, (mx + dx * mount, my + dy * mount, -plate)) for dx, dy in ((1, 1), (-1, 1), (1, -1), (-1, -1))])
            wall = 1.2 * MM if prop_r < 0.03 else 1.8 * MM
            link("duct", tube(prop_r + 1.5 * MM + wall, prop_r + 1.5 * MM, duct_h, (mx, my, -1 * MM), segs=40), "Plastic", bevel=0.3 * MM, angle=50)
            link("lip", tube(prop_r + 1.5 * MM + wall * 2.2, prop_r + 1.5 * MM + wall * 0.5, wall * 1.2, (mx, my, duct_h - 1 * MM - wall), segs=40), "Plastic", angle=50)
            for k in range(3):
                a = math.radians(30 + 120 * k + (45 if mx > 0 else 225))
                r0, r1 = pad * 0.9, prop_r + 1.6 * MM
                bm = box((mx + math.cos(a) * (r0 + r1) / 2, my + math.sin(a) * (r0 + r1) / 2, plate * 0.5), (r1 - r0, armW * 0.35, plate), rz=a)
                link("strut", bm, "Plastic", bevel=0.2 * MM)
        if p["canopy"]:
            # One-piece whoop: a TPU canopy dome over the AIO board with the camera window at the front.
            bm = bmesh.new()
            bmesh.ops.create_uvsphere(bm, u_segments=24, v_segments=12, radius=1.0,
                                      matrix=Matrix.Translation((0, 0, plate)) @ Matrix.Diagonal((bodyW * 0.45, bodyL * 0.48, stack * 1.1, 1)))
            bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z < plate - 1e-6], context="VERTS")
            dome = link("canopy", bm, "TPU", angle=60)
            sol = dome.modifiers.new("shell", "SOLIDIFY")
            sol.thickness = 0.8 * MM
            cut(dome, [box((0, bodyL * 0.45, plate + stack * 0.55), (8 * MM, 10 * MM, 8 * MM))])
    else:
        body_pts = rounded_rect(bodyW, bodyL, bodyW * 0.12)
        bottom = link("bottom", prism(body_pts, 0, plate), "Carbon", bevel=0.35 * MM)
        cut(bottom, [box((0, 0, plate / 2), (bodyW * 0.35, bodyL * 0.22, plate * 3))])
        for (mx, my) in motors:
            rx = math.copysign(bodyW * 0.3, mx)
            ry = math.copysign(bodyL * 0.3, my)
            if geo == "Deadcat" and my > 0:
                rx = math.copysign(bodyW * 0.5, mx)
            arm = link("arm", prism(capsule_arm(rx, ry, mx, my, armW, pad), 0, plate), "Carbon", bevel=0.35 * MM)
            cutters = [cylinder(hole, plate * 3, (mx + dx * mount, my + dy * mount, -plate)) for dx, dy in ((1, 1), (-1, 1), (1, -1), (-1, -1))]
            cutters.append(cylinder(2.6 * MM, plate * 3, (mx, my, -plate)))
            # Lightening slot along longer arms.
            d = Vector((mx - rx, my - ry))
            if d.length > 0.06 and armW > 9 * MM:
                mid = Vector((rx, ry)) + d * 0.5
                cutters.append(box((mid.x, mid.y, plate / 2), (armW * 0.28, d.length * 0.35, plate * 3), rz=math.atan2(d.y, d.x) - math.pi / 2))
            cut(arm, cutters)
            # Countersunk motor screws under the pad.
            for dx, dy in ((1, 1), (-1, 1), (1, -1), (-1, -1)):
                link("screw", cylinder(hole * 1.9, 0.8 * MM, (mx + dx * mount, my + dy * mount, -0.8 * MM), segs=12, r2=hole * 1.2), "Metal", angle=50)
            # TPU arm guard on the end of the arm.
            a = math.atan2(my, mx)
            bm = box((mx + math.cos(a) * pad * 0.95, my + math.sin(a) * pad * 0.95, plate / 2), (pad * 0.5, pad * 1.4, plate * 1.6), rz=a)
            link("guard", bm, "TPU", bevel=0.8 * MM)

    # Top plate, standoffs, side plates.
    top_z = plate + stack
    top = link("top", prism(rounded_rect(bodyW * 0.92, bodyL * 0.95, bodyW * 0.12), top_z, plate * 0.45), "Carbon", bevel=0.3 * MM)
    cut(top, [box((sx * bodyW * 0.22, 0, top_z), (3 * MM, bodyL * 0.12, plate * 3)) for sx in (-1, 1)])
    so_r = (2.5 if p["mountScrew"] > 2 else 2.0) * MM
    for sx in (-1, 1):
        for sy in (-1, 1):
            c = (sx * bodyW * 0.38, sy * bodyL * 0.42, plate)
            link("standoff", cylinder(so_r, stack, c, segs=12), "Edge", bevel=0.3 * MM, angle=40)
            link("screwtop", cylinder(so_r * 1.1, 1.0 * MM, (c[0], c[1], top_z + plate * 0.45), segs=12, r2=so_r * 0.7), "Metal", angle=50)
    if not p["canopy"]:
        cam_w = p["camW"] * MM
        for sx in (-1, 1):
            pts = rounded_rect(cam_w * 1.1, stack * 0.9, 2 * MM)
            bm = prism(pts, 0, 1.6 * MM)
            bmesh.ops.transform(bm, matrix=Matrix.Translation((sx * (cam_w / 2 + 1.6 * MM) - (1.6 * MM if sx > 0 else 0), bodyL * 0.5 - cam_w * 0.4, plate + stack * 0.5))
                                @ Matrix.Rotation(math.radians(90), 4, "Y") @ Matrix.Rotation(math.radians(90), 4, "Z"), verts=bm.verts)
            link("sideplate", bm, "Carbon", bevel=0.25 * MM)
        # TPU antenna mount at the back.
        link("antmount", box((0, -bodyL * 0.47, top_z + plate * 0.45 + 4 * MM), (bodyW * 0.5, 8 * MM, 8 * MM)), "TPU", bevel=1.2 * MM)
    sockets.update({
        "stack": (0, 0, plate + 1.5 * MM), "camera": (0, bodyL * 0.5 - p["camW"] * MM * 0.4, plate + stack * 0.5),
        "top": (0, 0, top_z + plate * 0.45), "antenna": (0, -bodyL * 0.47, top_z + plate * 0.45 + 8 * MM),
        "rx": (0, -bodyL * 0.35, plate + stack * 0.2),
    })
    return sockets


def motor(p):
    sr, sh = p["statorD"] * MM / 2, p["statorH"] * MM
    br = sr + 2.5 * MM + sr * 0.12
    bh = sh + 4 * MM + sr * 0.3
    mount = p["mount"] * MM / 2
    link("base", cylinder(br * 0.95, 1.5 * MM, (0, 0, 0), segs=32), "Metal", bevel=0.25 * MM)
    for a in (45, 135, 225, 315):
        r = math.radians(a)
        link("tab", box((math.cos(r) * mount * 0.75, math.sin(r) * mount * 0.75, 0.6 * MM), (mount * 1.2, mount * 0.55, 1.2 * MM), rz=r), "Metal", bevel=0.3 * MM)
    link("stator", cylinder(sr * 0.55, sh, (0, 0, 1.5 * MM), segs=24), "Metal")
    teeth = 9 if sr < 5 * MM else 12
    for k in range(teeth):
        a = 2 * math.pi * k / teeth
        link("winding", box((math.cos(a) * sr * 0.75, math.sin(a) * sr * 0.75, 1.5 * MM + sh / 2), (sr * 0.45, sr * 0.36, sh * 0.96), rz=a), "Copper", bevel=0.3 * MM * min(1, sr / (5 * MM)))
    # Bell: skirt + vented top, accent-coloured.
    skirt = link("bell", tube(br, br - max(0.6 * MM, br * 0.07), bh - 1.2 * MM, (0, 0, 2.5 * MM), segs=40), "Bell", bevel=0.25 * MM, angle=45)
    cap = link("cap", cylinder(br, 1.2 * MM, (0, 0, 2.5 * MM + bh - 1.2 * MM), segs=40), "Bell", bevel=0.4 * MM)
    vents = 6 if br > 8 * MM else 4
    cut(cap, [box((math.cos(a) * br * 0.6, math.sin(a) * br * 0.6, 2.5 * MM + bh - 1.2 * MM), (br * 0.45, br * 0.22, 6 * MM), rz=a)
              for a in [2 * math.pi * k / vents for k in range(vents)]])
    cut(skirt, [box((math.cos(a) * br, math.sin(a) * br, 2.5 * MM + bh * 0.15), (br * 0.3, br * 0.3, bh * 0.25), rz=a)
                for a in [2 * math.pi * k / 6 for k in range(6)]] if br > 8 * MM else [])
    shaft_r = (2.5 if sr > 8 * MM else 1.0 if sr > 4 * MM else 0.75) * MM
    top = 2.5 * MM + bh
    link("shaft", cylinder(shaft_r, 7 * MM if sr > 8 * MM else 3 * MM, (0, 0, top - 0.5 * MM), segs=16), "Metal")
    if sr > 8 * MM:
        link("nut", cylinder(shaft_r * 1.9, 4 * MM, (0, 0, top + 3 * MM), segs=6), "Metal", bevel=0.3 * MM, angle=30)
    return {"prop": (0, 0, top + 0.5 * MM)}


def prop(p, out_dir):
    R = p["diameterIn"] * 0.0254 / 2
    pitch = p["pitchIn"] * 0.0254
    blades = p["blades"]
    hub_r = max(2.5 * MM, R * 0.12)
    link("hub", cylinder(hub_r, 6 * MM if R > 0.03 else 3 * MM, (0, 0, 0), segs=24), "Prop", bevel=0.4 * MM)
    stations = 14
    for b in range(blades):
        ang = 2 * math.pi * b / blades
        bm = bmesh.new()
        rings = []
        for i in range(stations + 1):
            t = i / stations
            r = hub_r * 0.8 + (R - hub_r * 0.8) * t
            chord = R * (0.16 + 0.12 * math.sin(math.pi * min(1, t * 1.25))) * (1.0 if blades <= 3 else 0.78) * (1 - 0.65 * t ** 4)
            beta = math.atan2(pitch, 2 * math.pi * max(r, 1e-4))
            thick = chord * (0.12 - 0.08 * t)
            ring = []
            for (u, v) in ((-0.5, 0.0), (-0.25, 0.55), (0.1, 0.62), (0.5, 0.0), (0.1, -0.25), (-0.25, -0.22)):
                x, z = u * chord, v * thick
                # Twist: rotate the section by the blade angle about the radial axis.
                xr = x * math.cos(beta) - z * math.sin(beta)
                zr = x * math.sin(beta) + z * math.cos(beta)
                ring.append(bm.verts.new((xr, r, zr + 2 * MM)))
            rings.append(ring)
        for a, c in zip(rings, rings[1:]):
            for k in range(6):
                j = (k + 1) % 6
                bm.faces.new((a[k], a[j], c[j], c[k]))
        bm.faces.new(list(reversed(rings[0])))
        bm.faces.new(rings[-1])
        bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(ang, 3, "Z"))
        bm.normal_update()
        link("blade", bm, "Prop", angle=50)
    n = export(p["id"], out_dir)
    # The blur disc: a thin two-sided ring the size of the prop.
    reset()
    bm = bmesh.new()
    seg = 64
    for side in (1, -1):
        # Separate vertices per side (a face can't be made twice from the same ones).
        z = 2 * MM + side * 0.05 * MM
        inner = [bm.verts.new((math.cos(2 * math.pi * k / seg) * hub_r, math.sin(2 * math.pi * k / seg) * hub_r, z)) for k in range(seg)]
        outer = [bm.verts.new((math.cos(2 * math.pi * k / seg) * R, math.sin(2 * math.pi * k / seg) * R, z)) for k in range(seg)]
        for k in range(seg):
            j = (k + 1) % seg
            f = (inner[k], inner[j], outer[j], outer[k]) if side > 0 else (outer[k], outer[j], inner[j], inner[k])
            bm.faces.new(f)
    me = bpy.data.meshes.new("disc")
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new("disc", me)
    bpy.context.scene.collection.objects.link(ob)
    ob.data.materials.append(material("PropBlur"))
    export(p["id"] + "-blur", out_dir)
    return n


def battery(p):
    W, L, H = p["w"], p["l"], p["h"]
    if p["liion"]:
        # 2 x 3 cylindrical cells in shrink wrap.
        cr = min(W / 4, H / 2) * 0.98
        for ix in (-1, 1):
            for iz in (-1, 0, 1):
                link("cell", cylinder(cr, L * 0.98, (ix * W / 4, -L * 0.49, iz * H / 3 * 0.66), segs=24, axis="Y"), "Lipo", angle=60)
    else:
        link("pack", box((0, 0, 0), (W, L, H)), "Lipo", bevel=min(W, H) * 0.12, segments=3)
        link("band", box((0, L * 0.25, 0), (W * 1.01, L * 0.12, H * 1.01)), "Plastic", bevel=min(W, H) * 0.1, segments=2)
    # Main leads out of the back to the plug, balance lead beside them.
    plug = p["plug"]
    pw, pl, ph = {"XT60": (16, 16, 8), "XT30": (10, 12, 5), "BT2": (7, 10, 3)}[plug]
    back = -L / 2
    for side, slot in ((-1, "WireRed"), (1, "Wire")):
        x = side * min(W * 0.2, 4 * MM)
        tube_path([(x, back, H * 0.2), (x, back - 10 * MM, H * 0.25), (x * 0.6, back - 22 * MM, H * 0.55), (x * 0.3, back - 30 * MM, H * 0.6)],
                  (1.2 if plug == "XT60" else 0.8 if plug == "XT30" else 0.5) * MM, slot, "lead")
    link("plug", box((0, back - 30 * MM - pl * MM / 2, H * 0.6), (pw * MM, pl * MM, ph * MM)), "Plug", bevel=0.8 * MM)
    if p["cells"] > 1:
        tube_path([(W * 0.35, back, H * 0.3), (W * 0.45, back - 12 * MM, H * 0.2), (W * 0.4, back - 18 * MM, -H * 0.1)], 0.6 * MM, "Wire", "balance")
        link("balplug", box((W * 0.4, back - 21 * MM, -H * 0.1), ((p["cells"] + 1) * 2.5 * MM, 5 * MM, 3 * MM)), "Plastic", bevel=0.4 * MM)
    # Strap round the pack and the top plate, grip pad underneath.
    sw = (10 if L < 0.05 else 18) * MM
    link("strap", tube(1.0, 0.94, sw, (0, 0, -sw / 2), segs=48), "Pad", smooth=True)
    strap = bpy.data.objects["strap"]
    # The ring's axis along the pack (Y); it wraps the pack's width (X) and height (Z).
    strap.rotation_euler = (math.radians(90), 0, 0)
    strap.scale = (W * 0.54 + 1 * MM, H * 0.6 + 2 * MM, 1)
    strap.location = (0, L * 0.05, -H * 0.05)
    link("grip", box((0, 0, -H / 2 - 0.5 * MM), (W * 0.9, L * 0.85, 1 * MM)), "Rubber", bevel=0.3 * MM)
    return {}


def stack(p):
    pattern = p["pattern"] * MM
    board = pattern + 5.5 * MM
    rnd = random.Random(int(p["pattern"] * 10))
    for i, z in enumerate((0, 8 * MM if pattern > 21 * MM else 6 * MM)):
        b = link("board", prism(rounded_rect(board, board, 2 * MM), z, 1.6 * MM), "PCB", bevel=0.2 * MM)
        cut(b, [cylinder(1.6 * MM, 6 * MM, (sx * pattern / 2, sy * pattern / 2, z - 2 * MM), segs=12) for sx in (-1, 1) for sy in (-1, 1)])
        for sx in (-1, 1):
            for sy in (-1, 1):
                link("grommet", cylinder(2.6 * MM, 2.6 * MM, (sx * pattern / 2, sy * pattern / 2, z - 0.5 * MM), segs=12), "Rubber", angle=60)
        # Components: MOSFETs on the ESC, the MCU and passives on the FC.
        parts = 16 if i == 0 else 12
        for k in range(parts):
            w = rnd.uniform(1.5, 5.0) * MM
            l = rnd.uniform(1.5, 5.0) * MM
            x = rnd.uniform(-board / 2 + 4 * MM, board / 2 - 4 * MM)
            y = rnd.uniform(-board / 2 + 4 * MM, board / 2 - 4 * MM)
            link("chip", box((x, y, z + 1.6 * MM + 0.5 * MM), (w, l, 1 * MM)), "Plastic" if k % 3 else "Metal", bevel=0.15 * MM)
        if i == 1:
            link("mcu", box((0, 0, z + 1.6 * MM + 0.6 * MM), (7 * MM, 7 * MM, 1.2 * MM), rz=math.radians(45)), "Plastic", bevel=0.2 * MM)
    # Motor-wire pads with short wires out of each corner.
    for sx in (-1, 1):
        for sy in (-1, 1):
            for w in range(3):
                off = (w - 1) * 1.6 * MM
                tube_path([(sx * board / 2 * 0.9 + off * sy, sy * board / 2 * 0.9 - off * sx, 1.8 * MM), (sx * board * 0.7 + off, sy * board * 0.7 - off, 1.0 * MM)],
                          0.55 * MM, "Wire", "mwire", segs=6)
    return {}


def camera(p):
    s = p["size"] * MM
    hd = p["hd"]
    depth = s * (1.15 if hd else 0.95)
    link("body", box((0, 0, 0), (s, depth, s)), "Plastic", bevel=s * 0.08)
    link("barrel", cylinder(s * 0.36, s * 0.45, (0, depth / 2, 0), segs=24, axis="Y"), "Plastic", bevel=0.4 * MM)
    link("glass", cylinder(s * 0.28, 0.6 * MM, (0, depth / 2 + s * 0.45, 0), segs=24, axis="Y"), "Lens")
    link("guard", tube(s * 0.44, s * 0.37, s * 0.12, (0, 0, 0), segs=24), "TPU")
    g = bpy.data.objects["guard"]
    g.rotation_euler = (math.radians(-90), 0, 0)
    g.location = (0, depth / 2 + s * 0.3, 0)
    for sx in (-1, 1):
        link("csrew", cylinder(1.0 * MM, 1.0 * MM, (sx * s / 2, 0, 0), segs=12, axis="X"), "Metal")
    return {}


def vtx(p):
    """Analog: a pagoda antenna on a coax lead. HD: the air unit box with two dipoles."""
    if p["hd"]:
        link("airunit", box((0, 0, 6 * MM), (30 * MM, 30 * MM, 12 * MM)), "Plastic", bevel=1.5 * MM)
        for k in range(5):
            link("fin", box(((k - 2) * 5 * MM, 0, 12.8 * MM), (1.2 * MM, 26 * MM, 2 * MM)), "Metal", bevel=0.2 * MM)
        for sx in (-1, 1):
            tube_path([(sx * 10 * MM, -15 * MM, 6 * MM), (sx * 18 * MM, -26 * MM, 14 * MM), (sx * 24 * MM, -34 * MM, 26 * MM)], 0.9 * MM, "Wire", "coax")
            link("dipole", cylinder(1.6 * MM, 28 * MM, (sx * 24 * MM, -34 * MM, 26 * MM), segs=10, axis="Z"), "Plastic", bevel=0.4 * MM)
    else:
        tube_path([(0, 0, 0), (0, -6 * MM, 6 * MM), (0, -12 * MM, 14 * MM)], 0.9 * MM, "Wire", "coax")
        link("pagoda", cylinder(5 * MM, 9 * MM, (0, -12 * MM, 14 * MM), segs=16, r2=3.5 * MM), "Plastic", bevel=0.6 * MM)
    return {}


def receiver(p):
    link("rxboard", box((0, 0, 0), (12 * MM, 18 * MM, 2.5 * MM)), "PCB", bevel=0.3 * MM)
    link("rxshrink", box((0, 0, 0), (12.6 * MM, 18.6 * MM, 3 * MM)), "Plastic", bevel=1 * MM)
    for sx in (-1, 1):
        tube_path([(sx * 3 * MM, -9 * MM, 0), (sx * 10 * MM, -18 * MM, 6 * MM), (sx * 20 * MM, -24 * MM, 10 * MM)], 0.5 * MM, "Wire", "rxant", segs=6)
        link("sleeve", cylinder(0.9 * MM, 14 * MM, (sx * 16 * MM, -22 * MM, 8 * MM), segs=8, axis="X"), "Plastic")
    return {}


def capacitor(p):
    link("cap", cylinder(4 * MM, 16 * MM, (0, 0, 0), segs=20, axis="Y"), "Cap", bevel=0.5 * MM)
    for sx in (-1, 1):
        tube_path([(sx * 1.5 * MM, 0, 0), (sx * 2 * MM, -6 * MM, 0)], 0.35 * MM, "Metal", "capleg", segs=6)
    return {}


def actioncam(p):
    link("gbody", box((0, 0, 0), (62 * MM, 33 * MM, 44 * MM)), "Plastic", bevel=3 * MM, segments=3)
    link("glens", cylinder(9 * MM, 6 * MM, (-12 * MM, 16.5 * MM, 4 * MM), segs=32, axis="Y"), "Plastic", bevel=1 * MM)
    link("gglass", cylinder(7 * MM, 0.8 * MM, (-12 * MM, 22.5 * MM, 4 * MM), segs=32, axis="Y"), "Lens")
    link("gmount", box((0, 0, -26 * MM), (40 * MM, 30 * MM, 8 * MM), rx=math.radians(-15)), "TPU", bevel=2 * MM)
    return {}


BUILDERS = {"frame": frame, "motor": motor, "battery": battery, "stack": stack, "camera": camera, "vtx": vtx,
            "receiver": receiver, "capacitor": capacitor, "actioncam": actioncam}


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    spec_path, out_dir = argv[0], argv[1]
    os.makedirs(out_dir, exist_ok=True)
    with open(spec_path) as f:
        spec = json.load(f)
    only = set(argv[2].split(",")) if len(argv) > 2 else None
    for p in spec["parts"]:
        if only and p["id"] not in only:
            continue
        reset()
        if p["kind"] == "prop":
            n = prop(p, out_dir)
        else:
            sockets = BUILDERS[p["kind"]](p)
            n = export(p["id"], out_dir, sockets)
        print(f"QUADGEN {p['id']} {n} verts", flush=True)
    print("QUADGEN DONE", flush=True)


if __name__ == "__main__":
    main()
