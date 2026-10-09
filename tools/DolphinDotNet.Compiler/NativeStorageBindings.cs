using System.Text;
namespace DolphinDotNet.Compiler;
internal static class NativeStorageBindings
{
    // name, native symbol, parameter types; h requests the active managed heap.
    private static readonly Dictionary<string,(string Symbol,string Types,bool Heap)> Bindings=new()
    {
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
        var args=call.Arguments.Select((arg,index)=>$"({(binding.Types[index] switch {'s'=>"DndString*",'a'=>"DndArray*",'l'=>"int64_t",_=>"int"})})v{arg.Id}").ToList();
        if(binding.Heap)args.Insert(0,"dnd_value_heap");
        var expression=$"{binding.Symbol}({string.Join(", ",args)})";
        if(call.Result is {} result)b.AppendLine($"  v{result.Id} = {(result.Kind==IrValueKind.ObjectReference?"(intptr_t)":"")}{expression};");else b.AppendLine($"  {expression};");
    }
}
