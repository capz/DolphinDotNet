# Compiler architecture

The production path is a closed-world AOT compiler. The legacy DND bytecode VM remains a bootstrap/reference path.

Pipeline: MetadataLoader -> type model -> DependencyGraph -> IL importer -> typed DND IR -> lowering -> CBackend -> devkitPPC.

C is an implementation detail. Managed programs do not depend on DndObject, libogc, or generated headers.

The compiler targets the runtime ABI in dnd_managed.h. Runtime metadata will grow with vtables, interface maps, GC reference maps and static roots.

Current status: the type/dependency models, IR and first C backend are present. The existing bytecode compiler remains the executable default while IL import/lowering is migrated incrementally so the current DOL remains buildable.
