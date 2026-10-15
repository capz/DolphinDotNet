"""DolphinDotNet PSO import adapter. Requires Blender 4.2+ and an explicit
local copy of the theanine3D PSO Ultimate Importer. Never downloads code."""
import bpy
import importlib.util
import pathlib
import sys
import traceback

def main():
    args = sys.argv[sys.argv.index("--") + 1:]
    if len(args) != 6:
        raise RuntimeError("Expected ADDON INPUT OUTPUT KIND PLATFORM ANIMATIONS")
    addon_path, source, target, kind, platform, animations = args
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    spec = importlib.util.spec_from_file_location("dnd_pso_import", addon_path)
    if not spec or not spec.loader:
        raise RuntimeError("PSO addon cannot be loaded")
    plugin = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = plugin
    spec.loader.exec_module(plugin)
    plugin.register()
    common = {"filepath": source}
    if kind == "stage":
        result = bpy.ops.import_scene.pso_stage(**common, platform=platform,
                                                import_skybox=False)
    elif kind == "archive":
        result = bpy.ops.import_scene.pso_bml(**common, import_animations=animations == "1")
    elif kind == "actor":
        result = bpy.ops.import_scene.pso_actor(**common, platform=platform,
                                                import_animations=animations == "1")
    else:
        raise RuntimeError("Invalid PSO import kind")
    if "FINISHED" not in result:
        raise RuntimeError("PSO import operator returned " + str(result))
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    if not meshes:
        raise RuntimeError("No meshes imported from PSO input")
    bpy.ops.export_scene.gltf(filepath=target, export_format="GLB",
                              export_animations=animations == "1",
                              export_yup=True)
    if not pathlib.Path(target).is_file() or pathlib.Path(target).stat().st_size < 20:
        raise RuntimeError("GLB export was empty")

try:
    main()
except Exception:
    traceback.print_exc()
    sys.exit(1)
