"""Blender --background --factory-startup --python <this file>.

Reads the current Unity cape asset without modifying it; emission-bakes the
concept palette and the generated emblem through the existing UV atlas.
"""
import bpy
import collections
import hashlib
import json
import math
from pathlib import Path
import re
import struct
from mathutils import Vector
import numpy as np

SOURCE = Path(__file__).resolve().parent
ROOT = SOURCE.parent
MESH = ROOT / 'Generated/CapeClothMetres.asset'
EMBLEM = SOURCE / 'MoonEmblem.png'
OUTPUT = ROOT / 'Textures/Cape_ConceptMoon.png'
REVIEW = ROOT.parents[2] / 'VegetationLabReview'
SIZE = 2048
BORDER_WIDTH = .020
MOON_HEIGHT = .240
MOON_CENTER = (0., .50)
BAKE_EDGE_STEP = .006


def linear_color(value):
    rgb = [int(value[i:i + 2], 16) / 255 for i in (1, 3, 5)]
    return tuple(c / 12.92 if c <= .04045 else ((c + .055) / 1.055) ** 2.4 for c in rgb) + (1.,)


def read_mesh():
    source = MESH.read_bytes()
    text = source.decode('utf-8-sig')
    count = int(re.search(r'm_VertexCount: (\d+)', text)[1])
    raw = bytes.fromhex(re.search(r'_typelessdata: ([a-f0-9]+)', text)[1])
    channels = [tuple(map(int, item)) for item in re.findall(
        r'- stream: (\d+)\s+offset: (\d+)\s+format: (\d+)\s+dimension: (\d+)', text)]
    assert channels[0] == (0, 0, 0, 3) and channels[4] == (1, 0, 0, 2), 'Unsupported cape vertex layout'
    strides = {}
    for stream, offset, fmt, dimension in channels:
        if not dimension:
            continue
        assert fmt in (0, 10), 'Only Float32 and UInt32 vertex formats are expected'
        strides[stream] = max(strides.get(stream, 0), offset + dimension * 4)
    starts = {}; offset = 0
    for stream, stride in sorted(strides.items()):
        starts[stream] = offset
        offset = (offset + count * stride + 15) // 16 * 16
    assert offset == len(raw), 'Vertex stream byte lengths must match'
    vertices = [list(struct.unpack_from('<3f', raw, starts[0] + i * strides[0])) for i in range(count)]
    uv = [list(struct.unpack_from('<2f', raw, starts[1] + i * strides[1])) for i in range(count)]
    index_format = int(re.search(r'm_IndexFormat: (\d+)', text)[1])
    assert index_format == 0, 'Expected 16-bit cape indices'
    indices = bytes.fromhex(re.search(r'm_IndexBuffer: ([a-f0-9]+)', text)[1])
    faces = [list(struct.unpack_from('<3H', indices, i)) for i in range(0, len(indices), 6)]
    return vertices, uv, faces, hashlib.sha256(source).hexdigest()


def boundary_geometry(vertices, faces):
    welded = {}; mapping = []; points = []
    for vertex in vertices:
        key = tuple(round(c, 5) for c in vertex)
        if key not in welded:
            welded[key] = len(points); points.append(vertex)
        mapping.append(welded[key])
    edges = collections.Counter(); adjacency = collections.defaultdict(set)
    for triangle in faces:
        a, b, c = [mapping[i] for i in triangle]
        for start, end in ((a, b), (b, c), (c, a)):
            edges[tuple(sorted((start, end)))] += 1
            adjacency[start].add(end); adjacency[end].add(start)
    unseen = set(range(len(points))); components = []
    while unseen:
        stack = [unseen.pop()]; group = set(stack)
        while stack:
            for neighbor in adjacency[stack.pop()]:
                if neighbor in unseen:
                    unseen.remove(neighbor); group.add(neighbor); stack.append(neighbor)
        components.append(group)
    main = max(components, key=len)
    boundary_edges = [edge for edge, count in edges.items() if count == 1 and edge[0] in main]
    boundary_adjacency = collections.defaultdict(set)
    for start, end in boundary_edges:
        boundary_adjacency[start].add(end); boundary_adjacency[end].add(start)
    unseen = set(boundary_adjacency); loops = []
    while unseen:
        stack = [unseen.pop()]; group = set(stack)
        while stack:
            for neighbor in boundary_adjacency[stack.pop()]:
                if neighbor in unseen:
                    unseen.remove(neighbor); group.add(neighbor); stack.append(neighbor)
        loops.append(group)
    assert len(loops) == 2, 'Expected the separate neckline and the open hem/front boundary'
    neck = max(loops, key=lambda group: sum(points[i][1] for i in group) / len(group))
    assert min(points[i][1] for i in neck) > max(points[i][1] for group in loops if group != neck for i in group), 'Neckline must be above all hem/front edges'
    segments = [(points[a], points[b]) for a, b in boundary_edges if a not in neck]
    return segments, [float(i in main) for i in mapping], len(boundary_adjacency), len(neck)


def linear_bake_mesh(vertices, uv, faces, main, segments):
    # Only the temporary bake surface is tessellated. Every new position and UV
    # stays on its original triangle; the shipped mesh and its skin never change.
    positions = []; coordinates = []; polygons = []; cloth = []
    for triangle in faces:
        points = np.asarray([vertices[i] for i in triangle], dtype=float)
        texcoords = np.asarray([uv[i] for i in triangle], dtype=float)
        is_cloth = main[triangle[0]]
        longest = max(np.linalg.norm(points[a] - points[b]) for a, b in ((0, 1), (1, 2), (2, 0)))
        count = max(1, math.ceil(longest / BAKE_EDGE_STEP)) if is_cloth else 1
        local = {}
        for i in range(count + 1):
            for j in range(count + 1 - i):
                weights = np.array([1 - (i + j) / count, i / count, j / count])
                local[i, j] = len(positions)
                positions.append((weights @ points).tolist())
                coordinates.append((weights @ texcoords).tolist())
                cloth.append(is_cloth)
        for i in range(count):
            for j in range(count - i):
                polygons.append([local[i, j], local[i + 1, j], local[i, j + 1]])
                if i + j < count - 1:
                    polygons.append([local[i + 1, j], local[i + 1, j + 1], local[i, j + 1]])
    starts = np.array([pair[0] for pair in segments]); directions = np.array([pair[1] for pair in segments]) - starts
    lengths_squared = np.sum(directions * directions, axis=1)
    assert np.min(lengths_squared) > 0
    distances = np.ones(len(positions)); array = np.array(positions)
    for begin in range(0, len(positions), 512):
        points = array[begin:begin + 512]
        delta = points[:, None, :] - starts[None, :, :]
        along = np.clip(np.sum(delta * directions[None, :, :], axis=2) / lengths_squared[None, :], 0, 1)
        separation = delta - along[:, :, None] * directions[None, :, :]
        distances[begin:begin + len(points)] = np.sqrt(np.min(np.sum(separation * separation, axis=2), axis=1))
    distances[np.array(cloth) < .5] = 1.
    return positions, coordinates, polygons, cloth, distances.tolist()


vertices, uv, faces, source_hash = read_mesh()
source_vertex_count, source_triangle_count = len(vertices), len(faces)
segments, main, boundary_count, neck_count = boundary_geometry(vertices, faces)
vertices, uv, faces, main, distance = linear_bake_mesh(vertices, uv, faces, main, segments)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
mesh = bpy.data.meshes.new('Temporary cape with original Unity UV')
# Coordinate reflection displays Unity's rear view with +X on screen-right.
mesh.from_pydata([(p[0], p[2], p[1]) for p in vertices], [], faces)
mesh.update()
layer = mesh.uv_layers.new(name='OriginalUnityUV')
for polygon in mesh.polygons:
    for loop in polygon.loop_indices:
        layer.data[loop].uv = uv[mesh.loops[loop].vertex_index]
for name, values in [('BoundaryDistance', distance), ('MainCloth', main)]:
    attribute = mesh.attributes.new(name, 'FLOAT', 'POINT')
    for item, value in zip(attribute.data, values):
        item.value = value
obj = bpy.data.objects.new('Cape material bake only', mesh)
bpy.context.collection.objects.link(obj)
obj.select_set(True); bpy.context.view_layer.objects.active = obj

moon = bpy.data.images.load(str(EMBLEM), check_existing=False)
moon.colorspace_settings.name = 'sRGB'
alpha = np.asarray(moon.pixels[:], dtype=np.float32).reshape(moon.size[1], moon.size[0], 4)[:, :, 3]
rows, columns = np.where(alpha > .5)
assert len(rows), 'Generated emblem must contain visible alpha'
uv_center = ((columns.min() + columns.max()) / (2 * moon.size[0]), (rows.min() + rows.max()) / (2 * moon.size[1]))
canvas_height = MOON_HEIGHT / ((rows.max() - rows.min() + 1) / moon.size[1])
canvas_width = canvas_height * moon.size[0] / moon.size[1]

material = bpy.data.materials.new('Concept cape emission bake')
material.use_nodes = True; mesh.materials.append(material)
nodes = material.node_tree.nodes; nodes.clear(); links = material.node_tree.links
def node(kind, name):
    item = nodes.new(kind); item.label = name; item.name = name; return item
def math_node(operation, a, b, name):
    item = node('ShaderNodeMath', name); item.operation = operation
    for i, value in enumerate((a, b)):
        if isinstance(value, (int, float)): item.inputs[i].default_value = value
        else: links.new(value, item.inputs[i])
    return item.outputs[0]

position = node('ShaderNodeNewGeometry', 'Rest position projection')
split = node('ShaderNodeSeparateXYZ', 'Unity X Z Y')
links.new(position.outputs['Position'], split.inputs[0])
project = node('ShaderNodeCombineXYZ', 'Generated crescent projection')
u = math_node('ADD', math_node('DIVIDE', math_node('SUBTRACT', split.outputs['X'], MOON_CENTER[0], 'Moon center X'), canvas_width, 'Moon width'), uv_center[0], 'Moon U')
v = math_node('ADD', math_node('DIVIDE', math_node('SUBTRACT', split.outputs['Z'], MOON_CENTER[1], 'Moon center height'), canvas_height, 'Moon height'), uv_center[1], 'Moon V')
links.new(u, project.inputs['X']); links.new(v, project.inputs['Y'])
emblem = node('ShaderNodeTexImage', 'Generated gold crescent')
emblem.image = moon; emblem.extension = 'CLIP'; emblem.interpolation = 'Linear'
links.new(project.outputs[0], emblem.inputs['Vector'])
clean_alpha = node('ShaderNodeMapRange', 'Remove nearly transparent generation specks')
clean_alpha.clamp = True
clean_alpha.inputs['From Min'].default_value = .035
clean_alpha.inputs['From Max'].default_value = .965
links.new(emblem.outputs['Alpha'], clean_alpha.inputs['Value'])
main_attr = node('ShaderNodeAttribute', 'Main cloth excludes button'); main_attr.attribute_name = 'MainCloth'
back = math_node('LESS_THAN', split.outputs['Y'], -.15, 'Back surface only')
moon_mask = math_node('MULTIPLY', clean_alpha.outputs['Result'], math_node('MULTIPLY', main_attr.outputs['Fac'], back, 'Back cloth gate'), 'Moon alpha')
edge_attr = node('ShaderNodeAttribute', 'Distance from real cloth boundary'); edge_attr.attribute_name = 'BoundaryDistance'
border = node('ShaderNodeMapRange', 'Two centimetre cream binding')
border.clamp = True; border.interpolation_type = 'SMOOTHSTEP'
border.inputs['From Min'].default_value = BORDER_WIDTH - .001
border.inputs['From Max'].default_value = BORDER_WIDTH + .001
border.inputs['To Min'].default_value = 1.; border.inputs['To Max'].default_value = 0.
links.new(edge_attr.outputs['Fac'], border.inputs['Value'])
cloth = node('ShaderNodeMixRGB', 'Charcoal cloth and cream trim'); cloth.blend_type = 'MIX'
cloth.inputs[1].default_value = linear_color('#4B5052')
cloth.inputs[2].default_value = linear_color('#E9DDC4')
links.new(border.outputs['Result'], cloth.inputs[0])
finished = node('ShaderNodeMixRGB', 'Gold emblem on cloth'); finished.blend_type = 'MIX'
links.new(moon_mask, finished.inputs[0]); links.new(cloth.outputs[0], finished.inputs[1]); links.new(emblem.outputs['Color'], finished.inputs[2])
emission = node('ShaderNodeEmission', 'Unlit albedo bake'); emission.inputs['Strength'].default_value = 1.
links.new(finished.outputs[0], emission.inputs['Color'])
output = node('ShaderNodeOutputMaterial', 'Material output'); links.new(emission.outputs[0], output.inputs['Surface'])
image = bpy.data.images.new('Cape_ConceptMoon', SIZE, SIZE, alpha=True, float_buffer=False)
image.generated_color = (75 / 255, 80 / 255, 82 / 255, 1.)
image.colorspace_settings.name = 'sRGB'
bake_target = node('ShaderNodeTexImage', 'Original UV bake destination'); bake_target.image = image
nodes.active = bake_target
scene = bpy.context.scene
scene.render.engine = 'CYCLES'; scene.cycles.device = 'CPU'; scene.cycles.samples = 4
scene.render.bake.margin = 16; scene.render.bake.margin_type = 'EXTEND'
scene.render.bake.use_clear = False
scene.view_settings.view_transform = 'Standard'; scene.view_settings.look = 'None'
bpy.ops.object.bake(type='EMIT')
image.filepath_raw = str(OUTPUT); image.file_format = 'PNG'; image.save()

# Inspect the saved texture on the same static mesh in a separate Blender preview.
links.new(bake_target.outputs['Color'], emission.inputs['Color'])
camera_data = bpy.data.cameras.new('Material review camera')
camera = bpy.data.objects.new('Material review camera', camera_data)
bpy.context.collection.objects.link(camera); scene.camera = camera
camera_data.type = 'ORTHO'; camera_data.ortho_scale = 1.45
scene.render.engine = 'BLENDER_EEVEE'
scene.render.resolution_x = 1100; scene.render.resolution_y = 900; scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'; scene.render.film_transparent = False
scene.world.color = (.10, .12, .14)
REVIEW.mkdir(parents=True, exist_ok=True)
for name, location in [('back', (0, -3, .49)), ('front', (0, 3, .49)), ('back_oblique', (1.2, -3, 1.3))]:
    camera.location = location
    camera.rotation_euler = (Vector((0, -.10, .45)) - camera.location).to_track_quat('-Z', 'Y').to_euler()
    scene.render.filepath = str(REVIEW / ('cape_concept_' + name + '.png'))
    bpy.ops.render.render(write_still=True)

report = {
    'sourceMesh': str(MESH), 'meshSHA256Before': source_hash,
    'meshSHA256After': hashlib.sha256(MESH.read_bytes()).hexdigest(),
    'emblemSHA256': hashlib.sha256(EMBLEM.read_bytes()).hexdigest(),
    'output': str(OUTPUT), 'outputSHA256': hashlib.sha256(OUTPUT.read_bytes()).hexdigest(),
    'method': 'Blender shader-node projection with emission bake into unchanged original UV atlas',
    'size': [SIZE, SIZE], 'vertexCount': source_vertex_count, 'triangleCount': source_triangle_count,
    'temporaryBakeVertexCount': len(vertices), 'temporaryBakeTriangleCount': len(faces),
    'temporaryBakeMaximumEdgeStepMetres': BAKE_EDGE_STEP,
    'boundaryVertexCount': boundary_count, 'excludedNecklineVertices': neck_count,
    'trimBoundarySegmentCount': len(segments), 'borderWidthMetres': BORDER_WIDTH,
    'borderDistanceMethod': 'Nearest point on true hem/front boundary segments, sampled on temporary linear tessellation',
    'clothSRGB': '#4B5052', 'trimSRGB': '#E9DDC4',
    'moonCenterXYMetres': list(MOON_CENTER), 'moonHeightMetres': MOON_HEIGHT,
    'emblemProjectionCanvasMetres': [canvas_width, canvas_height],
    'backGateUnityZ': '< -0.15', 'uvMarginPixels': 16,
    'notes': ['Preview is a static Blender material check, not Unity runtime evidence.',
              'Source mesh geometry, weights, UVs and .blend files are unchanged.',
              'The shader uses only the principal cloth component; the separate button and neckline remain charcoal.',
              'Temporary linear tessellation adds no displacement and leaves all original UV triangles unchanged.']
}
assert report['meshSHA256Before'] == report['meshSHA256After']
(REVIEW / 'cape_concept_bake_report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
print('CAPE_CONCEPT_BAKE_COMPLETE', json.dumps(report, ensure_ascii=False))
