"""三视图批渲脚本：由 BridgeBlender 以 blender --background --factory-startup --python render_views.py -- <参数文件> 方式驱动。

读参数文件（sys.argv 里 -- 之后那个路径），渲前 / 侧 / 45° 三张 PNG：
清默认场景 → 按后缀导入 → 算世界包围盒 → 架正交相机 → 钉死渲染参数 → 逐视角渲。

铁律：
- stdout 上只许有一行 BRIDGE_RESULT <json>（三张输出图），其余一切输出走 sys.stderr。
- 渲染参数全部钉死，不写时间戳、随机数、机器名进产物（决策 45 同源）——同一个模型跑两次
  必须出逐字节相同的图，否则「换一批」时人分不清是模型变了还是渲染抖了。
- 输出文件名是跨环的硬约定：<模型文件名（带后缀）>.<front|side|iso>.png，
  少一个点、大小写不同，下一环的九宫格就找不到图（AssetPaths.VariantViewFile）。
- 任何异常：traceback 打 stderr、退出码非 0、不打 BRIDGE_RESULT——绝不让调用方拿到半份结果。
"""
import json
import math
import os
import sys
import traceback

import bpy
from mathutils import Euler, Vector

# 视角名 → 相机欧拉角（XYZ，度）。front 从 -Y 看向 +Y，side 从 +X 看向 -X，iso 是方位 45°、俯角 30°。
VIEWS = [
    ("front", (90.0, 0.0, 0.0)),
    ("side", (90.0, 0.0, 90.0)),
    ("iso", (60.0, 0.0, 45.0)),
]

# 边长的合法区间，与 BlenderRunner 里的钳制保持一致。
MINIMUM_SIDE_LENGTH = 64
MAXIMUM_SIDE_LENGTH = 2048
DEFAULT_SIDE_LENGTH = 512


def log(*args):
    """除 BRIDGE_RESULT 之外的输出一律走 stderr。"""
    print(*args, file=sys.stderr)


def find_arguments_file(argv):
    """找 -- 之后的参数文件路径；找不到 -- 时退回最后一个参数（兼容不同 Blender 版本的 sys.argv 形状）。"""
    if "--" in argv:
        index = argv.index("--")
        if index + 1 < len(argv):
            return argv[index + 1]
    if len(argv) >= 2:
        return argv[-1]
    return None


def load_payload():
    args_file = find_arguments_file(sys.argv)
    if not args_file or not os.path.exists(args_file):
        raise RuntimeError("找不到参数文件：%r（sys.argv=%r）" % (args_file, sys.argv))
    with open(args_file, "r", encoding="utf-8") as f:
        return json.load(f)


def mesh_objects():
    return [obj for obj in bpy.data.objects if obj.type == "MESH"]


def import_model(input_model_path):
    """按后缀导入。不认识的后缀直接报错——不猜、不硬试，让调用方看见「不支持」而不是一张空图。"""
    extension = os.path.splitext(input_model_path)[1].lower()
    if extension in (".glb", ".gltf"):
        bpy.ops.import_scene.gltf(filepath=input_model_path)
    elif extension == ".fbx":
        bpy.ops.import_scene.fbx(filepath=input_model_path)
    else:
        raise RuntimeError("不支持的模型后缀「%s」，本脚本只吃 .glb / .gltf / .fbx" % extension)


def compute_world_bounds(meshes):
    """全部 MESH 对象世界坐标包围盒的并集，返回 (中心, 尺寸) 两个 Vector。"""
    minimum = [float("inf")] * 3
    maximum = [float("-inf")] * 3
    for obj in meshes:
        for corner in obj.bound_box:
            world = obj.matrix_world @ Vector(corner)
            for axis in range(3):
                if world[axis] < minimum[axis]:
                    minimum[axis] = world[axis]
                if world[axis] > maximum[axis]:
                    maximum[axis] = world[axis]

    center = Vector(((minimum[a] + maximum[a]) * 0.5 for a in range(3)))
    size = Vector((maximum[a] - minimum[a] for a in range(3)))
    return center, size


def setup_camera(center, size):
    """架一台正交相机对准包围盒；返回相机对象与环绕半径。"""
    longest = max(size.x, size.y, size.z)
    if longest <= 0.0:
        # 退化模型（单点 / 零尺寸）也要能渲出一张图来，不然人看不到「它是空的」这件事。
        longest = 1.0

    # 取景按包围盒的对角线算，不按最长边：45° 视角下模型的投影宽度是对角线那么长，
    # 按最长边定 ortho_scale 会把立方体这类模型的四个角切掉。三个视角共用同一个尺度，
    # 人对着卡片比大小时才有意义。
    diagonal = math.sqrt(size.x * size.x + size.y * size.y + size.z * size.z)
    if diagonal <= 0.0:
        diagonal = longest

    camera_data = bpy.data.cameras.new("三视图相机")
    camera_data.type = "ORTHO"
    camera_data.ortho_scale = max(diagonal * 1.1, 0.001)
    camera_data.clip_start = 0.001
    camera_data.clip_end = longest * 100.0 + 1000.0

    camera = bpy.data.objects.new("三视图相机", camera_data)
    bpy.context.scene.collection.objects.link(camera)
    bpy.context.scene.camera = camera
    return camera, longest * 2.0 + 1.0


def setup_render(scene, side_length):
    """渲染设置全部钉死：Workbench 引擎、固定抗锯齿、透明底、不烧时间戳。"""
    # EEVEE 在 --background 下要 GL 上下文，Workbench 不要，且看轮廓足够——审模型看的就是形。
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.render_aa = "8"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"

    # 透明底：下一环的九宫格按 alpha 合成到自己的底色上，这里不要烧死一个背景色。
    scene.render.film_transparent = True
    scene.render.resolution_x = side_length
    scene.render.resolution_y = side_length
    scene.render.resolution_percentage = 100

    # use_stamp 只管「把字烧进画面」，管不住写进 PNG 的 tEXt 元数据块——Blender 默认往里写
    # Date / RenderTime / hostname 这类东西，同一个模型渲两次就出两份不同的字节流，
    # 「换一批」时人分不清是模型变了还是渲染抖了（决策 45 同源）。逐个关死。
    scene.render.use_stamp = False
    scene.render.use_stamp_date = False
    scene.render.use_stamp_time = False
    scene.render.use_stamp_render_time = False
    scene.render.use_stamp_frame = False
    scene.render.use_stamp_frame_range = False
    scene.render.use_stamp_memory = False
    scene.render.use_stamp_hostname = False
    scene.render.use_stamp_camera = False
    scene.render.use_stamp_lens = False
    scene.render.use_stamp_scene = False
    scene.render.use_stamp_marker = False
    scene.render.use_stamp_filename = False
    scene.render.use_stamp_sequencer_strip = False
    scene.render.use_stamp_note = False

    image_settings = scene.render.image_settings
    image_settings.file_format = "PNG"
    image_settings.color_mode = "RGBA"
    image_settings.color_depth = "8"
    image_settings.compression = 15


def read_side_length(payload):
    """读边长；缺省 512，越界钳回区间（与 BlenderRunner 的钳制同源，两侧都兜一道）。"""
    raw = payload.get("边长", DEFAULT_SIDE_LENGTH)
    try:
        value = int(raw)
    except (TypeError, ValueError):
        value = DEFAULT_SIDE_LENGTH

    if value < MINIMUM_SIDE_LENGTH:
        value = MINIMUM_SIDE_LENGTH
    if value > MAXIMUM_SIDE_LENGTH:
        value = MAXIMUM_SIDE_LENGTH
    return value


def execute():
    payload = load_payload()
    input_model_path = payload.get("输入模型")
    output_directory = payload.get("输出目录")
    if not input_model_path or not output_directory:
        raise RuntimeError("参数文件缺「输入模型」或「输出目录」")

    if not os.path.exists(input_model_path):
        raise RuntimeError("输入模型不存在：%s" % input_model_path)

    side_length = read_side_length(payload)

    # 默认场景带 Cube/Light/Camera，不清就会把那个立方体一起渲进去。
    bpy.ops.wm.read_factory_settings(use_empty=True)
    import_model(input_model_path)

    meshes = mesh_objects()
    if not meshes:
        raise RuntimeError("导入后场景里没有网格：%s" % input_model_path)

    center, size = compute_world_bounds(meshes)
    camera, distance = setup_camera(center, size)

    scene = bpy.context.scene
    setup_render(scene, side_length)

    os.makedirs(output_directory, exist_ok=True)
    model_file_name = os.path.basename(input_model_path)

    outputs = []
    for view_name, degrees in VIEWS:
        rotation = Euler((math.radians(degrees[0]), math.radians(degrees[1]), math.radians(degrees[2])), "XYZ")
        camera.rotation_euler = rotation
        # 先把「相机朝自己 -Z 看」这条约定用同一个旋转搬到世界里，得到该视角的机位。
        camera.location = center + (rotation.to_matrix() @ Vector((0.0, 0.0, distance)))
        bpy.context.view_layer.update()

        output_path = os.path.join(output_directory, "%s.%s.png" % (model_file_name, view_name))
        scene.render.filepath = output_path
        log("渲 %s → %s" % (view_name, output_path))
        bpy.ops.render.render(write_still=True)

        if not os.path.exists(output_path):
            raise RuntimeError("渲完但文件没落盘：%s" % output_path)

        outputs.append({"视角": view_name, "路径": output_path})

    result = {"输出图": outputs}
    # 唯一允许出现在 stdout 上的内容。
    print("BRIDGE_RESULT " + json.dumps(result, ensure_ascii=False))


def main():
    try:
        execute()
    except Exception:
        traceback.print_exc(file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()
