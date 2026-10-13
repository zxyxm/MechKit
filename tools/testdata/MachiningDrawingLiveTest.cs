using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;

public static class MachiningDrawingLiveTest
{
    public static void Run(string input,string folder)
    {
        folder=Path.GetFullPath(folder);
        Directory.CreateDirectory(folder);
        var original=ReadShared(input);
        var app=(ISldWorks)Marshal.GetActiveObject("SldWorks.Application");
        var old=app.IActiveDoc2;
        var path=Path.Combine(folder,"MachTest-"+Guid.NewGuid().ToString("N").Substring(0,8)+"-"+Path.GetFileName(input));
        File.Copy(input,path,false);
        ModelDoc2 part=null,drawing=null;
        try
        {
            int error=0,warning=0;
            part=app.OpenDoc6(path,1,1,"",ref error,ref warning);
            if(part==null) throw new Exception("Fixture open failed: "+error);
            var lib=typeof(MechKit.MechKitAddin).Assembly;
            var type=lib.GetType("MechKit.Features.PlateDrawingService",true);
            var settings=Activator.CreateInstance(lib.GetType("MechKit.Core.AddinSettings",true));
            var service=Activator.CreateInstance(type,new object[]{app,settings,true});
            type.GetMethod("Run").Invoke(service,new object[]{part,
                new Func<int,int,string,bool>((i,n,s)=>true),new Action<object>(r=>Console.WriteLine(r))});
            var output=Path.ChangeExtension(path,".SLDDRW");
            if(!File.Exists(output)) throw new Exception("Drawing was not generated");
            var active=app.IActiveDoc2;
            if(active==null || !string.Equals(active.GetPathName(),output,StringComparison.OrdinalIgnoreCase))
                throw new Exception("Last drawing was not activated");
            drawing=active;
            if(part.GetSaveFlag()) throw new Exception("Source was changed");
            var views=((object[])((Sheet)((DrawingDoc)drawing).GetCurrentSheet()).GetViews()).Cast<View>().ToArray();
            if(views.Length!=3 && views.Length!=4 && views.Length<6) throw new Exception("Incomplete compact or general layout");
            Console.WriteLine("View count: "+views.Length);
            var keys=new System.Collections.Generic.HashSet<string>();
            foreach(var view in views)
            {
                if(!string.Equals(view.GetReferencedModelName(),path,StringComparison.OrdinalIgnoreCase))
                    throw new Exception("Model reference lost");
                for(var d=view.GetFirstDisplayDimension5();d!=null;d=d.GetNext5())
                {
                    if(((Annotation)d.GetAnnotation()).IsDangling()) throw new Exception("Dangling dimension");
                    if(!keys.Add(d.GetDimension2(0).FullName)) throw new Exception("Repeated model dimension");
                }
            }
            if(keys.Count<3) throw new Exception("Insufficient dimensions");
            drawing.ViewZoomtofit2();
            if(!drawing.SaveBMP(Path.Combine(folder,"preview.bmp"),0,0)) throw new Exception("Preview failed");
            var before=File.GetLastWriteTimeUtc(output);
            // Re-running must preserve the existing drawing and leave it untouched.
            type.GetMethod("Run").Invoke(service,new object[]{part,
                new Func<int,int,string,bool>((i,n,s)=>true),new Action<object>(r=>{})});
            if(File.GetLastWriteTimeUtc(output)!=before) throw new Exception("Existing drawing overwritten");
            Console.WriteLine("PASS: references, dimensions, active drawing, non-overwrite. "+output);
        }
        finally
        {
            if(drawing!=null) app.CloseDoc(drawing.GetTitle());
            if(part!=null) app.CloseDoc(part.GetTitle()); // This is exclusively our disposable copy.
            if(old!=null){int error=0;app.ActivateDoc3(old.GetTitle(),false,2,ref error);}
            if(!original.SequenceEqual(ReadShared(input))) throw new Exception("Original source file changed");
        }
    }
    private static byte[] ReadShared(string path)
    {
        using(var s=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
        using(var m=new MemoryStream()){s.CopyTo(m);return m.ToArray();}
    }
}
