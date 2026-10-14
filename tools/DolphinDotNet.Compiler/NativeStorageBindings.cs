using System.Text;
namespace DolphinDotNet.Compiler;
internal static class NativeStorageBindings
{
    // name, native symbol, parameter types; h requests the active managed heap.
    private static readonly Dictionary<string,(string Symbol,string Types,bool Heap)> Bindings=new()
    {
        ["AsyncRunning"]=("dnd_async_running","",false),
        ["AsyncContext"]=("dnd_async_context","",false),
        ["AsyncWake"]=("dnd_async_wake","",false),
        ["AsyncProgress"]=("dnd_async_progress","",false),
        ["GraphicsBeginFrame"]=("dnd_graphics_begin_frame","ffff",false),
        ["GraphicsFrustum"]=("dnd_graphics_frustum","ffffff",false),
        ["GraphicsTranslate"]=("dnd_graphics_translate","fff",false),
        ["GraphicsRotate"]=("dnd_graphics_rotate","ffff",false),
        ["GraphicsBeginTriangles"]=("dnd_graphics_begin_triangles","",false),
        ["GraphicsColor"]=("dnd_graphics_color","fff",false),
        ["GraphicsVertex"]=("dnd_graphics_vertex","fff",false),
        ["GraphicsEndTriangles"]=("dnd_graphics_end_triangles","",false),
        ["GraphicsEndFrame"]=("dnd_graphics_present","",false),
        ["NetFailure"]=("dnd_net_failure","ii",true),["NetFailureCode"]=("dnd_net_failure_code","o",false),
        ["NetStart"]=("dnd_net_start","iiii",false),["NetStatus"]=("dnd_net_status","",false),["NetTime"]=("dnd_net_time","",false),["NetError"]=("dnd_net_error","",false),
        ["NetOpen"]=("dnd_net_open","i",false),["NetBind"]=("dnd_net_bind","iii",false),["NetListen"]=("dnd_net_listen","ii",false),["NetAccept"]=("dnd_net_accept","ia",false),
        ["NetConnect"]=("dnd_net_connect","iii",false),["NetConnected"]=("dnd_net_connected","i",false),["NetRead"]=("dnd_net_read","iaiia",false),["NetWrite"]=("dnd_net_write","iaiiii",false),
        ["NetEndpoint"]=("dnd_net_endpoint","iai",false),["NetOption"]=("dnd_net_option","iii",false),["NetClose"]=("dnd_net_close","i",false),["NetStop"]=("dnd_net_stop","",false),
        ["PerformanceVisible"]=("dnd_performance_visible","",false),
        ["PerformanceSetVisible"]=("dnd_performance_set_visible","i",false),
        ["ModelTrs"]=("dnd_model_trs","aa",false),["ModelMultiply"]=("dnd_model_multiply_arrays","aaa",false),["ModelPosition"]=("dnd_model_position","aafff",false),["ModelTranslate"]=("dnd_model_translate","afff",false),["ModelRotate"]=("dnd_model_rotate","afff",false),["ModelCamera"]=("dnd_model_camera","aaa",false),
        ["ModelBegin"]=("dnd_model_begin","si",false),["ModelStep"]=("dnd_model_step","i",false),["ModelDispose"]=("dnd_model_dispose","i",false),["ModelStat"]=("dnd_model_stat","ii",false),
        ["ModelName"]=("dnd_model_name","iii",true),["ModelDuration"]=("dnd_model_duration","ii",false),["ModelCopy"]=("dnd_model_copy","iiaii",false),
        ["ModelRestoreBegin"]=("dnd_model_restore_begin","i",false),["ModelRestoreEnd"]=("dnd_model_restore_end","ii",false),["ModelUnload"]=("dnd_model_unload","i",false),
        ["ModelInstance"]=("dnd_model_instance","ii",false),["ModelInstanceBytes"]=("dnd_model_instance_bytes","i",false),["ModelInstanceDispose"]=("dnd_model_instance_dispose","i",false),["ModelPlay"]=("dnd_model_play","iiif",false),["ModelControl"]=("dnd_model_control","iif",false),
        ["ModelNodeMatrix"]=("dnd_model_node_matrix","iia",false),["ModelDraw"]=("dnd_model_draw","ia",false),["ModelFrameBegin"]=("dnd_model_frame_begin","aa",false),["ModelFrameEnd"]=("dnd_model_frame_end","",false),
        ["AramInitialize"]=("dnd_aram_initialize","i",false),["AramAvailable"]=("dnd_aram_available","",false),["AramStat"]=("dnd_aram_stat","i",false),["AramAllocate"]=("dnd_aram_allocate","i",false),
        ["AramRetain"]=("dnd_aram_retain","i",false),["AramRelease"]=("dnd_aram_release","i",false),["AramDispose"]=("dnd_aram_dispose","i",false),["AramBegin"]=("dnd_aram_begin","iiaiii",false),["AramPoll"]=("dnd_aram_poll","ai",false),
        ["TextCompare"]=("dnd_text_compare","ssi",false),["TextHash"]=("dnd_text_hash","si",false),["MountDisc"]=("dnd_storage_mount_disc","i",false),["Encode"]=("dnd_text_encode","sii",true),["Decode"]=("dnd_text_decode","aiiii",true),["FromChars"]=("dnd_text_from_chars","aii",true),
        ["Mount"]=("dnd_storage_mount","i",false),["Unmount"]=("dnd_storage_unmount","i",false),["IsMounted"]=("dnd_storage_is_mounted","i",false),
        ["GetPath"]=("dnd_storage_path","is",true),["FullPath"]=("dnd_fs_full_path","s",true),["Exists"]=("dnd_fs_exists","si",false),
        ["ReadAllBytes"]=("dnd_fs_read_all","s",true),["WriteAllBytes"]=("dnd_fs_write_all","sa",false),["Delete"]=("dnd_fs_delete","si",false),
        ["Move"]=("dnd_fs_move","ss",false),["CreateDirectory"]=("dnd_fs_mkdir","s",false),["Open"]=("dnd_fs_open","siii",false),
        ["Read"]=("dnd_fs_read","iaii",false),["Write"]=("dnd_fs_write","iaii",false),["Seek"]=("dnd_fs_seek","ili",false),
        ["Length"]=("dnd_fs_length","i",false),["SetLength"]=("dnd_fs_set_length","il",false),["Flush"]=("dnd_fs_flush","i",false),["Close"]=("dnd_fs_close","i",false),
        ["DirectoryOpen"]=("dnd_fs_dir_open","s",false),["DirectoryNext"]=("dnd_fs_dir_next","ii",true),["DirectoryClose"]=("dnd_fs_dir_close","i",false),
        ["GetCurrentDirectory"]=("dnd_fs_getcwd","",true),["SetCurrentDirectory"]=("dnd_fs_setcwd","s",false),
        ["CardMount"]=("dnd_card_mount","iss",false),["CardRead"]=("dnd_card_read","is",true),["CardWrite"]=("dnd_card_write","isa",false),
        ["CardLength"]=("dnd_card_length","is",false),["CardWriteSave"]=("dnd_card_write_save","isassaa",false),
        ["CardDelete"]=("dnd_card_delete","is",false),["CardEntries"]=("dnd_card_entries","i",true)
    };
    internal static void Emit(StringBuilder b,ValueIrNativeStorage call)
    {
        if(!Bindings.TryGetValue(call.Operation,out var binding)||binding.Types.Length!=call.Arguments.Count)throw new NotSupportedException($"Invalid native storage binding {call.Operation}.");
        var args=call.Arguments.Select((arg,index)=>$"({(binding.Types[index] switch {'s'=>"DndString*",'a'=>"DndArray*",'o'=>"DndObject*",'l'=>"int64_t",'f'=>"float",_=>"int"})})v{arg.Id}").ToList();
        if(binding.Heap)args.Insert(0,"dnd_value_heap");
        if(call.Operation.StartsWith("Graphics",StringComparison.Ordinal)||call.Operation is "ModelDraw" or "ModelFrameBegin" or "ModelFrameEnd")
            b.AppendLine("  dnd_async_require_application_thread();");
        var expression=$"{binding.Symbol}({string.Join(", ",args)})";
        if(call.Result is {} result)b.AppendLine($"  v{result.Id} = {(result.Kind==IrValueKind.ObjectReference?"(intptr_t)":"")}{expression};");else b.AppendLine($"  {expression};");
    }
}
