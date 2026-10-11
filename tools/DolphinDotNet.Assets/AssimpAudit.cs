using System.Globalization;
using System.Xml.Linq;
namespace DolphinDotNet.Assets;
internal static class AssimpAudit
{
    public static Dictionary<string,float> Check(string path)
    {
        var document=XDocument.Load(path);var durations=new Dictionary<string,float>(StringComparer.Ordinal);
        foreach(var mesh in document.Descendants("Mesh"))
        {
            var influences=new Dictionary<int,int>();
            foreach(var weight in mesh.Descendants("Weight"))
            {
                if(float.Parse(weight.Value,CultureInfo.InvariantCulture)<=0)continue;
                int vertex=int.Parse(weight.Attribute("index")!.Value,CultureInfo.InvariantCulture);influences.TryGetValue(vertex,out int count);if(++count>4)throw new InvalidDataException("Source mesh has more than four bone influences per vertex; reduce weights explicitly in the authoring tool.");influences[vertex]=count;
            }
        }
        foreach(var animation in document.Descendants("Animation"))
        {
            string name=animation.Attribute("name")?.Value??"";float ticks=float.Parse(animation.Attribute("tick_cnt")!.Value,CultureInfo.InvariantCulture),duration=float.Parse(animation.Attribute("duration")!.Value,CultureInfo.InvariantCulture);
            if(ticks>0&&!durations.ContainsKey(name))durations.Add(name,duration/ticks);
        }
        return durations;
    }
}
