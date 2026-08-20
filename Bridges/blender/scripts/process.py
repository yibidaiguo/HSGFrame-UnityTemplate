"""八步模型加工脚本：由 BridgeBlender 以 blender --background --factory-startup --python process.py -- <参数文件> 方式驱动。

读参数文件（sys.argv 里 -- 之后那个路径），按加工计划的八个步骤加工：
导入 / 统一单位 / pivot归位 / 减面 / UV / 烘法线 / 命名 / 导出。
步骤名以 ProcessingPlanBuilder.StepNames 为准，顺序恒定。

铁律：
- stdout 上只许有一行 BRIDGE_RESULT <json>（加工结果），其余一切输出走 sys.stderr。
- 计划里 启用=false 的步骤必须跳过并登记原因，不静默跳过（决策 46）。
- 指标不许写时间戳、随机数、机器名、绝对路径（决策 45 同源）——同输入要能跑出逐字节相同的指标。
- 任何异常：traceback 打 stderr、退出码非 0、不打 BRIDGE_RESULT——绝不让调用方拿到半份结果。
"""
import json
import os
import sys
import traceback

import bpy
from mathutils import Vector

STEP_NAMES = ["导入", "统一单位", "pivot归位", "减面", "UV", "烘法线", "命名", "导出"]


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


def get_step(plan, name):
    for step in plan.get("步骤", []):
        if step.get("名称") == name:
            return step
    return None


def mesh_objects():
    return [obj for obj in bpy.data.objects if obj.type == "MESH"]


def select_only(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def step_import(input_model):
    bpy.ops.import_scene.gltf(filepath=input_model)


def step_unified_unit(params):
    # 规格数据里单位是「米」；glTF 场景本身就是米，把 Blender 场景单位钉成公制即可。
    bpy.context.scene.unit_settings.system = "METRIC"


def step_pivot(params):
    pivot = params.get("pivot", "中心")
    for obj in mesh_objects():
        select_only(obj)
        if pivot == "脚底":
            bpy.ops.object.origin_set(type="ORIGIN_CENTER_OF_VOLUME")
            corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
            center_x = sum(c.x for c in corners) / 8
            center_y = sum(c.y for c in corners) / 8
            z_min = min(c.z for c in corners)
            bpy.context.scene.cursor.location = Vector((center_x, center_y, z_min))
            bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
        else:
            bpy.ops.object.origin_set(type="ORIGIN_CENTER_OF_VOLUME")
        obj.select_set(False)


def step_decimate(params):
    target_text = params.get("目标面数", "").strip()
    if not target_text:
        raise RuntimeError("减面步骤启用了，但计划参数缺「目标面数」")
    target = int(target_text)
    if target <= 0:
        raise RuntimeError("减面目标面数必须大于 0，拿到：%d" % target)
    for obj in mesh_objects():
        if len(obj.data.polygons) <= target:
            continue
        select_only(obj)
        # DECIMATE 的 ratio 是近似值，且接近 1 时可能减不动（上一轮实测 0.996 停在 3012 > 3000）。
        # 每次打 10% 余量下探，迭代直到不超过目标面数（最多 8 轮）——上限是规格的硬约束。
        for _ in range(8):
            current = len(obj.data.polygons)
            if current <= target:
                break
            ratio = max(0.05, (target / current) * 0.9)
            modifier = obj.modifiers.new("减面", "DECIMATE")
            modifier.ratio = ratio
            bpy.ops.object.modifier_apply(modifier="减面")
        obj.select_set(False)


def step_uv(params):
    for obj in mesh_objects():
        if len(obj.data.uv_layers) == 0:
            select_only(obj)
            bpy.ops.object.mode_set(mode="EDIT")
            bpy.ops.mesh.select_all(action="SELECT")
            bpy.ops.uv.smart_project()
            bpy.ops.object.mode_set(mode="OBJECT")
            obj.select_set(False)


def step_naming(params):
    naming = params.get("命名", "").strip()
    if not naming:
        raise RuntimeError("命名步骤启用了，但计划参数缺「命名」")
    for obj in mesh_objects():
        obj.name = naming
        break
    return naming


def step_export(params, output_dir, export_name):
    export_path = os.path.join(output_dir, export_name + ".gltf")
    bpy.ops.export_scene.gltf(filepath=export_path, export_format="GLTF_SEPARATE")
    return export_path


def compute_metrics():
    meshes = mesh_objects()
    triangle_count = sum(sum(len(p.vertices) - 2 for p in obj.data.polygons) for obj in meshes)
    materials = set()
    for obj in meshes:
        for slot in obj.material_slots:
            if slot.material:
                materials.add(slot.material)
    texture_size = 0
    for material in materials:
        if material.node_tree:
            for node in material.node_tree.nodes:
                if node.type == "TEX_IMAGE" and node.image:
                    width, height = node.image.size
                    texture_size = max(texture_size, width, height)
    bone_count = sum(len(obj.data.bones) for obj in bpy.data.objects if obj.type == "ARMATURE")

    # 包围盒必须真算：所有 mesh 对象世界包围盒的并集，单位米（glTF 场景就是米）。
    min_corner = [float("inf")] * 3
    max_corner = [float("-inf")] * 3
    for obj in meshes:
        for corner in obj.bound_box:
            world = obj.matrix_world @ Vector(corner)
            for axis in range(3):
                min_corner[axis] = min(min_corner[axis], world[axis])
                max_corner[axis] = max(max_corner[axis], world[axis])

    return {
        "面数": triangle_count,
        "材质数": len(materials),
        "贴图尺寸": texture_size,
        "骨骼数": bone_count,
        "包围盒米": {
            "x": round(max_corner[0] - min_corner[0], 6),
            "y": round(max_corner[1] - min_corner[1], 6),
            "z": round(max_corner[2] - min_corner[2], 6)
        }
    }


def execute():
    payload = load_payload()
    input_model_path = payload["输入模型"]
    output_dir = payload["输出目录"]
    plan = payload["加工计划"]

    if not os.path.exists(input_model_path):
        raise RuntimeError("输入模型不存在：%s" % input_model_path)
    os.makedirs(output_dir, exist_ok=True)

    # --factory-startup 下场景自带默认 Cube（带默认材质）与相机灯光。
    # 不清掉它们，加工会把默认 Cube 一起导出进 glTF，指标也会把它的面/材质算进去——
    # 加工必须只作用于输入模型。
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)

    executed = []
    skipped = []

    def skip(name, reason):
        skipped.append({"步骤": name, "原因": reason})

    naming = ""
    export_path = ""

    for name in STEP_NAMES:
        step = get_step(plan, name)
        if step is None:
            skip(name, "计划里没有这一步")
            continue
        if not step.get("启用"):
            skip(name, step.get("跳过原因") or "计划里没启用")
            continue

        params = step.get("参数") or {}
        if name == "导入":
            step_import(input_model_path)
        elif name == "统一单位":
            step_unified_unit(params)
        elif name == "pivot归位":
            step_pivot(params)
        elif name == "减面":
            step_decimate(params)
        elif name == "UV":
            step_uv(params)
        elif name == "烘法线":
            # 基线恒禁用烘法线（ProcessingPlanBuilder 硬编码不启用）。万一哪份计划启用了它，
            # 显式跳过并登记，绝不假装烘了——让调用方看见「加工站还没实现」而不是静默的假绿。
            skip(name, "加工站当前不支持烘法线，启用它之前先实现")
            continue
        elif name == "命名":
            naming = step_naming(params)
        elif name == "导出":
            export_name = naming if naming else os.path.splitext(os.path.basename(input_model_path))[0]
            export_path = step_export(params, output_dir, export_name)
        else:
            skip(name, "加工站不认识这个步骤")
            continue

        executed.append(name)

    bpy.context.view_layer.update()

    if not export_path:
        raise RuntimeError("导出步骤没跑成，没有可用的输出模型")

    metrics = compute_metrics()
    export_name = os.path.splitext(os.path.basename(export_path))[0]
    metrics_path = os.path.join(output_dir, export_name + ".指标.json")
    with open(metrics_path, "w", encoding="utf-8") as f:
        json.dump(metrics, f, ensure_ascii=False, indent=2)
        f.write("\n")

    result = {
        "输出模型": export_path,
        "指标文件": metrics_path,
        "执行了的步骤": executed,
        "跳过的步骤": skipped
    }
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
