using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;

public static class PlateDrawingLiveTest
{
    public static void Reject(string[] inputs,string outputRoot)
    {
        var app=(ISldWorks)Marshal.GetActiveObject("SldWorks.Application");
        var old=app.IActiveDoc2;
        var inspect=typeof(MechKit.MechKitAddin).Assembly.GetType("MechKit.Features.PlateGeometry",true).GetMethod("Inspect");
        var folder=Path.Combine(outputRoot,"negative-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"));Directory.CreateDirectory(folder);
        try
        {
            foreach(var input in inputs)
            {
                var path=Path.Combine(folder,"MechKitReject-"+Path.GetFileName(input));File.Copy(input,path,false);
                ModelDoc2 part=null;
                try
                {
                    int error=0,warning=0;part=app.OpenDoc6(path,1,1,"",ref error,ref warning);
                    if(part==null) throw new Exception("Negative fixture cannot open");
                    var rejected=false;
                    try {inspect.Invoke(null,new object[]{part});}
                    catch(TargetInvocationException ex)
                    {
                        if(!(ex.InnerException is InvalidOperationException)) throw;
                        rejected=true;Console.WriteLine("PASS rejected: "+input+" "+ex.InnerException.Message);
                    }
                    if(!rejected) throw new Exception("Nonplate/sheet metal fixture was accepted: "+input);
                }
                finally {if(part!=null) app.CloseDoc(part.GetTitle());}
            }
        }
        finally {if(old!=null){int error=0;app.ActivateDoc3(old.GetTitle(),false,2,ref error);}}
    }

    public static void Run(string[] inputs,string outputRoot)
    {
        var app=(ISldWorks)Marshal.GetActiveObject("SldWorks.Application");
        var old=app.IActiveDoc2;
        var folder=Path.Combine(outputRoot,DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(folder);
        var lib=typeof(MechKit.MechKitAddin).Assembly;
        var serviceType=lib.GetType("MechKit.Features.PlateDrawingService",true);
        var settings=Activator.CreateInstance(lib.GetType("MechKit.Core.AddinSettings",true));
        var service=Activator.CreateInstance(serviceType,new[]{(object)app,settings});
        var resultType=lib.GetType("MechKit.Features.PlateDrawingResult",true);
        var template=(string)serviceType.GetMethod("ExtractTemplate").Invoke(null,null);
        int failures=0;
        var sourceBytes=inputs.Select(ReadShared).ToArray();
        try
        {
            foreach(var input in inputs)
            {
                // Distinct filename: SOLIDWORKS cannot open two parts with identical basenames.
                var source=Path.Combine(folder,"MechKitTest-"+Path.GetFileName(input));
                File.Copy(input,source,false);
                ModelDoc2 part=null,drawing=null;
                try
                {
                    int error=0,warning=0;
                    part=app.OpenDoc6(source,1,1,"",ref error,ref warning);
                    if(part==null) throw new Exception("Fixture open failed: "+error);
                    string config=part.ConfigurationManager.ActiveConfiguration.Name;
                    var target=Path.ChangeExtension(source,".SLDDRW");
                    Func<int,int,string,bool> progress=(index,total,name)=>true;
                    Action<object> completed=r=>{};
                    serviceType.GetMethod("Run").Invoke(service,new object[]{part,progress,completed});
                    var results=((System.Collections.IEnumerable)serviceType.GetField("Results").GetValue(service)).Cast<object>().ToArray();
                    if(results.Length!=1) throw new Exception("Single part must produce exactly one result");
                    var result=results[0];
                    if((string)resultType.GetField("Configuration").GetValue(result)!=config)
                        throw new Exception("Single part did not use active configuration");
                    if(app.IActiveDoc2.GetPathName()!=source) throw new Exception("Source part was not restored");
                    Console.WriteLine("PASS "+input+" -> "+resultType.GetField("Status").GetValue(result)+" "+resultType.GetField("Message").GetValue(result));
                    if(!File.Exists(target) || part.GetSaveFlag()) throw new Exception("Missing output or changed source");
                    drawing=app.OpenDoc6(target,3,1,"",ref error,ref warning);
                    if(drawing==null) throw new Exception("Final output cannot reopen");
                    var drw=(DrawingDoc)drawing;
                    var views=(object[])((Sheet)drw.GetCurrentSheet()).GetViews();
                    if(Path.GetFileName(input).Contains("扫码枪滑板"))
                    {
                        var callouts=new System.Collections.Generic.List<string>();
                        foreach(View v in views)
                            for(var d=v.GetFirstDisplayDimension5() as DisplayDimension;d!=null;d=d.GetNext5() as DisplayDimension)
                            {
                                var data=(DisplayData)d.GetDisplayData();var text="";
                                if(data!=null) for(var i=0;i<data.GetTextCount();i++) text+=data.GetTextAtIndex(i)+" ";
                                callouts.Add(text);
                            }
                        var m3=callouts.Where(t=>System.Text.RegularExpressions.Regex.IsMatch(t,@"8\s*[x×]\s*M3\b")).ToArray();
                        if(m3.Length!=1) throw new Exception("Expected one 8 x M3 pattern callout, found "+m3.Length);
                        if(!callouts.Any(t=>t.Contains("M8")) || !callouts.Any(t=>t.Contains("5.50")) ||
                            !callouts.Any(t=>System.Text.RegularExpressions.Regex.IsMatch(t,@"4\s*[x×]")))
                            throw new Exception("Independent hole callouts lost");
                        Console.WriteLine("PASS pattern: one 8 x M3, M8/counterbore/dowel callouts retained");
                    }
                    foreach(View v in views)
                        if(!string.Equals(v.GetReferencedModelName(),source,StringComparison.OrdinalIgnoreCase))
                            throw new Exception("Final rename broke model reference");
                    drawing.ViewZoomtofit2();
                    drawing.SaveBMP(Path.ChangeExtension(target,".bmp"),0,0);
                }
                catch(Exception ex)
                {
                    failures++;
                    Console.WriteLine("FAIL "+input+" "+(ex.InnerException??ex));
                }
                finally
                {
                    if(drawing!=null) app.CloseDoc(drawing.GetTitle());
                    var remaining=app.GetOpenDocumentByName(source) as ModelDoc2;
                    if(remaining!=null && !remaining.GetSaveFlag()) app.CloseDoc(remaining.GetTitle());
                }
            }
            if(failures==0) TestBatch(app,lib,folder,inputs);
            for(var i=0;i<inputs.Length;i++)
                if(!sourceBytes[i].SequenceEqual(ReadShared(inputs[i]))) throw new Exception("Original fixture was modified");
        }
        finally {if(old!=null){int error=0;app.ActivateDoc3(old.GetTitle(),false,2,ref error);}}
        Console.WriteLine("Live artifacts: "+folder);
        if(failures>0) throw new Exception(failures+" live tests failed");
    }

    private static byte[] ReadShared(string path)
    {
        using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
        using(var memory=new MemoryStream()) {stream.CopyTo(memory);return memory.ToArray();}
    }

    private static void TestBatch(ISldWorks app,Assembly lib,string folder,string[] inputs)
    {
        ModelDoc2 assembly=null;
        var opened=new System.Collections.Generic.List<ModelDoc2>();
        var sources=inputs.Select(p=>Path.Combine(folder,"MechKitTest-"+Path.GetFileName(p))).ToArray();
        var target=Path.ChangeExtension(sources[0],".SLDDRW");
        File.Delete(target); // only the fixture generated above, never an original file
        var serviceType=lib.GetType("MechKit.Features.PlateDrawingService",true);
        var resultType=lib.GetType("MechKit.Features.PlateDrawingResult",true);
        var resultsField=serviceType.GetField("Results");
        try
        {
            var template=app.GetDocumentTemplate(2,"",0,0,0);
            assembly=(ModelDoc2)app.NewDocument(template,0,0,0);
            if(assembly==null) throw new Exception("No assembly template for batch test");
            foreach(var path in sources)
            {
                int error=0,warning=0;
                var model=app.OpenDoc6(path,1,1,"",ref error,ref warning);opened.Add(model);
                app.ActivateDoc3(assembly.GetTitle(),false,2,ref error);
                if(((AssemblyDoc)assembly).AddComponent5(path,0,"",false,"",0,0,0)==null) throw new Exception("Cannot insert fixture");
            }
            if(((AssemblyDoc)assembly).AddComponent5(sources[0],0,"",false,"",.2,0,0)==null) throw new Exception("Cannot insert duplicate fixture");
            Func<int,int,string,bool> progress=(a,b,c)=>true;
            Action<object> callback=r=>Console.WriteLine("BATCH "+resultType.GetField("Status").GetValue(r)+" "+resultType.GetField("Message").GetValue(r));
            Func<object> makeService=()=>Activator.CreateInstance(serviceType,new[]{(object)app,Activator.CreateInstance(lib.GetType("MechKit.Core.AddinSettings",true))});
            var service=makeService();
            serviceType.GetMethod("Run").Invoke(service,new object[]{assembly,progress,callback});
            var results=((System.Collections.IEnumerable)resultsField.GetValue(service)).Cast<object>().ToArray();
            if(results.Length!=inputs.Length || !File.Exists(target)) throw new Exception("Batch deduplication/generation failed");
            var snapshots=sources.Select(p=>File.ReadAllBytes(Path.ChangeExtension(p,".SLDDRW"))).ToArray();
            service=makeService();serviceType.GetMethod("Run").Invoke(service,new object[]{assembly,progress,callback});
            for(var i=0;i<sources.Length;i++)
                if(!snapshots[i].SequenceEqual(File.ReadAllBytes(Path.ChangeExtension(sources[i],".SLDDRW")))) throw new Exception("Existing drawing overwritten");
            service=makeService();
            serviceType.GetMethod("Run").Invoke(service,new object[]{assembly,new Func<int,int,string,bool>((a,b,c)=>false),callback});
            if(!(bool)serviceType.GetProperty("Cancelled").GetValue(service,null)) throw new Exception("Cancellation ignored");
            Console.WriteLine("PASS batch: deduplication, existing outputs preserved, cancellation");
        }
        finally
        {
            if(assembly!=null) app.CloseDoc(assembly.GetTitle());
            foreach(var source in sources)
            {
                var doc=app.GetOpenDocumentByName(source) as ModelDoc2;
                if(doc!=null) app.CloseDoc(doc.GetTitle());
            }
        }
    }
}
