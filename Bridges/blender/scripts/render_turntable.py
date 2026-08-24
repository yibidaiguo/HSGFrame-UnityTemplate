"""转台帧渲脚本：由 BridgeBlender 以 blender --background --factory-startup --python render_turntable.py -- <参数文件> 方式驱动。

读参数文件（sys.argv 里 -- 之后那个路径），把一个模型渲成一串**透明底**的帧：
清默认场景 → 按后缀导入 → 算世界包围盒 → 架正交相机 → 钉死渲染参数 → 相机绕一圈逐帧渲。

**这条路是 3D 动画那一支的第一步。** 为什么不在 ComfyUI 里做：本机这套 ComfyUI 的
Load3D 要一个交互式视口输入（LOAD_3D），headless 提交不了；RenderSplat 要 SPLAT 不吃网格。
所以 3D 那一支的帧由 Blender 出——而这反倒是好事：Blender 的 film_transparent 出的是**真 alpha**，
不用像 2D 那两支一样先铺纯色底再回本地抠（本机 ComfyUI 的 background_removal 里一个模型都没有）。

两种动法，按参数「模式」选：
- `环绕`（默认）：模型不动，相机绕 Z 轴匀速转一圈。用来出转台预览、审模型的形。
- `自带动画`：用模型自己的动画（有 armature/关键帧的 glb），按场景帧区间逐帧渲，相机不动。
  模型里没有动画时**报错而不是悄悄退回环绕**——退回去人会拿到一圈转台图，
  却以为自己看到的是角色在走路。

铁律（与 render_views.py 同源）：
- stdout 上只许有一行 BRIDGE_RESULT <json>，其余一切输出走 sys.stderr。
- 渲染参数全部钉死，不写时间戳/随机数/机器名进产物（决策 45 同源）——同一个模型跑两次
  必须出逐字节相同的帧，否则「这一版跟上一版哪里不一样」根本没法比。
- 文件名是跨环硬约定：`<模型文件名（带后缀）>.frame_<四位序号>.png`，序号从 0 起。
  帧序列描述（FrameSequence）按这个名字找帧，少一个点或位数不同就等于没渲。
- 任何异常：traceback 打 stderr、退出码非 0、不打 BRIDGE_RESULT——绝不让调用方拿到半份结果。
"""
import json
import math
import os
import sys
import traceback

import bpy
from mathutils import Euler, Vector

# 边长的合法区间，与 BlenderRunner 里的钳制保持一致。
MINIMUM_SIDE_LENGTH = 64
MAXIMUM_SIDE_LENGTH = 2048
DEFAULT_SIDE_LENGTH = 512

# 帧数的合法区间。上限 240 是拍脑袋的保护值：一帧一次渲，几百帧要跑很久，
# 而这条链的第一步是给人看方向的，不是出成片。
MINIMUM_FRAME_COUNT = 2
MAXIMUM_FRAME_COUNT = 240
DEFAULT_FRAME_COUNT = 12

# 环绕模式相机的俯角（度）。30° 是三视图那份 iso 视角用的角度，两处一致，
# 人从三视图切到转台时视线不会跳。
ORBIT_ELEVATION_DEGREES = 30.0

MODE_ORBIT = "环绕"
MODE_BAKED = "自带动画"
ALLOWED_MODES = (MODE_ORBIT, MODE_BAKED)


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
    """按后缀导入。不认识的后缀直接报错——不猜、不硬试，让调用方看见「不支持」而不是一串空图。"""
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
        longest = 1.0

    # 取景按对角线算而不是最长边：转一圈的过程中模型的投影宽度会变，
    # 按最长边定尺度的话转到 45° 时会把角切掉——而那时人看到的是「模型缺了一块」。
    diagonal = math.sqrt(size.x * size.x + size.y * size.y + size.z * size.z)
    if diagonal <= 0.0:
        diagonal = longest

    camera_data = bpy.data.cameras.new("转台相机")
    camera_data.type = "ORTHO"
    camera_data.ortho_scale = max(diagonal * 1.1, 0.001)
    camera_data.clip_start = 0.001
    camera_data.clip_end = longest * 100.0 + 1000.0

    camera = bpy.data.objects.new("转台相机", camera_data)
    bpy.context.scene.collection.objects.link(camera)
    bpy.context.scene.camera = camera
    return camera, longest * 2.0 + 1.0


def setup_render(scene, side_length):
    """渲染设置全部钉死：Workbench 引擎、固定抗锯齿、透明底、不烧任何时间戳。"""
    # EEVEE 在 --background 下要 GL 上下文，Workbench 不要，且看轮廓足够。
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.render_aa = "8"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"

    # **真透明底**。2D 那两支要先铺纯色再回本地抠，这一支不用——这是 3D 走 Blender 的额外好处。
    scene.render.film_transparent = True
    scene.render.resolution_x = side_length
    scene.render.resolution_y = side_length
    scene.render.resolution_percentage = 100

    # use_stamp 只管「把字烧进画面」，管不住写进 PNG 的 tEXt 元数据块——Blender 默认往里写
    # Date / RenderTime / hostname，同一个模型渲两次就出两份不同的字节流（决策 45 同源）。
    scene.render.use_stamp = False
    for flag in (
        "use_stamp_date", "use_stamp_time", "use_stamp_render_time", "use_stamp_frame",
        "use_stamp_frame_range", "use_stamp_memory", "use_stamp_hostname", "use_stamp_camera",
        "use_stamp_lens", "use_stamp_scene", "use_stamp_marker", "use_stamp_filename",
        "use_stamp_sequencer_strip", "use_stamp_note",
    ):
        setattr(scene.render, flag, False)

    image_settings = scene.render.image_settings
    image_settings.file_format = "PNG"
    image_settings.color_mode = "RGBA"
    image_settings.color_depth = "8"
    image_settings.compression = 15


def read_clamped_int(payload, key, default_value, minimum, maximum):
    """读一个整数参数；缺省用默认值，越界钳回区间。"""
    raw = payload.get(key, default_value)
    try:
        value = int(raw)
    except (TypeError, ValueError):
        value = default_value

    if value < minimum:
        value = minimum
    if value > maximum:
        value = maximum
    return value


def read_mode(payload):
    """读模式；不认识的值直接报错——静默退回会让人拿到一圈转台图却以为看到了角色动作。"""
    mode = payload.get("模式", MODE_ORBIT)
    if mode not in ALLOWED_MODES:
        raise RuntimeError("不认识的模式「%s」，只有：%s" % (mode, "、".join(ALLOWED_MODES)))
    return mode


def orbit_frames(scene, camera, center, distance, frame_count, output_directory, model_file_name):
    """环绕模式：模型不动，相机绕 Z 轴匀速转一圈，逐帧渲。"""
    outputs = []
    elevation = math.radians(90.0 - ORBIT_ELEVATION_DEGREES)
    for index in range(frame_count):
        azimuth = 2.0 * math.pi * index / float(frame_count)
        rotation = Euler((elevation, 0.0, azimuth), "XYZ")
        camera.rotation_euler = rotation
        # 与三视图那份同一条约定：相机朝自己的 -Z 看，所以机位是中心加上旋转后的 +Z 方向。
        camera.location = center + (rotation.to_matrix() @ Vector((0.0, 0.0, distance)))
        bpy.context.view_layer.update()
        outputs.append(render_one(scene, output_directory, model_file_name, index))
    return outputs


def baked_frames(scene, camera, center, distance, frame_count, output_directory, model_file_name):
    """
    自带动画模式：用模型自己的关键帧，相机钉在 iso 机位不动，按场景帧区间等距抽 frame_count 帧。

    等距抽而不是逐帧渲：一段 60 帧的走路循环渲 60 张要很久，而这一步是给人审方向的。
    抽样步长写进结果里交给调用方——帧序列描述要靠它算真实帧率。
    """
    if not has_animation():
        raise RuntimeError(
            "这个模型里没有任何动画数据（没有 action / 关键帧），出不了「自带动画」的帧。"
            "要转台预览就把模式改成「%s」" % MODE_ORBIT)

    rotation = Euler((math.radians(60.0), 0.0, math.radians(45.0)), "XYZ")
    camera.rotation_euler = rotation
    camera.location = center + (rotation.to_matrix() @ Vector((0.0, 0.0, distance)))
    bpy.context.view_layer.update()

    start = scene.frame_start
    end = scene.frame_end
    span = max(1, end - start)
    outputs = []
    for index in range(frame_count):
        # 末帧不取 end：循环动画的首末帧几乎一样，取了就等于多一张重复帧。
        scene.frame_set(start + int(round(span * index / float(frame_count))))
        bpy.context.view_layer.update()
        outputs.append(render_one(scene, output_directory, model_file_name, index))
    return outputs


def has_animation():
    """场景里有没有动画数据：任何对象带 animation_data.action，或有 armature 带 action。"""
    for obj in bpy.data.objects:
        animation_data = getattr(obj, "animation_data", None)
        if animation_data is not None and animation_data.action is not None:
            return True
    return len(bpy.data.actions) > 0


def render_one(scene, output_directory, model_file_name, index):
    """渲一帧并落盘；文件名是跨环硬约定。渲完文件不在就报错，绝不当成渲成了。"""
    output_path = os.path.join(output_directory, "%s.frame_%04d.png" % (model_file_name, index))
    scene.render.filepath = output_path
    log("渲第 %d 帧 → %s" % (index, output_path))
    bpy.ops.render.render(write_still=True)
    if not os.path.exists(output_path):
        raise RuntimeError("渲完但文件没落盘：%s" % output_path)
    return {"序号": index, "路径": output_path}


def execute():
    payload = load_payload()
    input_model_path = payload.get("输入模型")
    output_directory = payload.get("输出目录")
    if not input_model_path or not output_directory:
        raise RuntimeError("参数文件缺「输入模型」或「输出目录」")

    if not os.path.exists(input_model_path):
        raise RuntimeError("输入模型不存在：%s" % input_model_path)

    side_length = read_clamped_int(payload, "边长", DEFAULT_SIDE_LENGTH, MINIMUM_SIDE_LENGTH, MAXIMUM_SIDE_LENGTH)
    frame_count = read_clamped_int(payload, "帧数", DEFAULT_FRAME_COUNT, MINIMUM_FRAME_COUNT, MAXIMUM_FRAME_COUNT)
    mode = read_mode(payload)

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

    if mode == MODE_ORBIT:
        outputs = orbit_frames(scene, camera, center, distance, frame_count, output_directory, model_file_name)
    else:
        outputs = baked_frames(scene, camera, center, distance, frame_count, output_directory, model_file_name)

    result = {"模式": mode, "边长": side_length, "输出帧": outputs}
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
