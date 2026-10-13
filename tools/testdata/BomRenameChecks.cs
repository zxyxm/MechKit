using System;
using System.IO;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using SolidWorks.Interop.sldworks;
public sealed class BomProxy : RealProxy {
 readonly Func<IMethodCallMessage,object> run;
 public BomProxy(Type type, Func<IMethodCallMessage,object> action):base(type){run=action;}
 public override IMessage Invoke(IMessage message){var call=(IMethodCallMessage)message;try{return new ReturnMessage(run(call),call.Args,call.ArgCount,call.LogicalCallContext,call);}catch(Exception ex){return new ReturnMessage(ex,call);}}
 public static T Make<T>(Func<IMethodCallMessage,object> action){return (T)new BomProxy(typeof(T),action).GetTransparentProxy();}
}
public static class BomRenameChecks {
 public static void Run(string root,string dll){
  var method=Assembly.LoadFrom(dll).GetType("MechKit.Features.PartListService").GetMethod("RenameWithDrawing",BindingFlags.NonPublic|BindingFlags.Static);
  foreach(var mode in new[]{"success","collision","bad-reference","save-failure","drawing-save-failure"}){
   var folder=Path.Combine(root,mode);Directory.CreateDirectory(folder);
   var source=Path.Combine(folder,"old.sldprt");var target=Path.Combine(folder,"new.sldprt");
   var oldDrawing=Path.Combine(folder,"old.slddrw");var newDrawing=Path.Combine(folder,"new.slddrw");
   File.WriteAllText(source,"model");File.WriteAllText(oldDrawing,"drawing");
   if(mode=="collision") File.WriteAllText(newDrawing,"existing");
   bool renamed=false,savedModel=false,savedDrawing=false;int closes=0;
   var ext=BomProxy.Make<ModelDocExtension>(m=>{
    if(m.MethodName=="RenameDocument"){renamed=true;return 0;}
    if(m.MethodName=="SaveAs"){
     if(!savedModel)throw new Exception("Drawing saved before assembly");
     if(mode=="drawing-save-failure")return false;
     File.WriteAllText((string)m.Args[0],"new drawing");savedDrawing=true;return true;
    }return null;
   });
   var drawing=BomProxy.Make<ModelDoc2>(m=>{
    if(m.MethodName=="GetDependencies2")return new string[]{"model",mode=="bad-reference"?"wrong.sldprt":renamed?target:source};
    if(m.MethodName=="get_Extension")return ext;
    if(m.MethodName=="GetPathName")return savedDrawing?newDrawing:oldDrawing;
    if(m.MethodName=="GetTitle")return "drawing";
    if(m.MethodName=="GetSaveFlag")return renamed&&!savedDrawing;
    if(m.MethodName=="EditRebuild3")return true;
    return null;
   });
   var assembly=BomProxy.Make<ModelDoc2>(m=>{
    if(m.MethodName=="get_Extension")return ext;
    if(m.MethodName=="GetPathName")return Path.Combine(folder,"assembly.sldasm");
    if(m.MethodName=="EditRebuild3")return true;
    if(m.MethodName=="Save3"){
     if(((int)m.Args[0]&4)==0)throw new Exception("Missing SaveReferenced");
     if(mode=="save-failure")return false;
     File.WriteAllText(target,"new model");savedModel=true;return true;
    }return null;
   });
   var component=BomProxy.Make<Component2>(m=>m.MethodName=="Select4"?(object)true:null);
   var sw=BomProxy.Make<ISldWorks>(m=>{
    if(m.MethodName=="OpenDoc6")return drawing;
    if(m.MethodName=="CloseDoc")closes++;
    return null;
   });
   bool failed=false;
   try{method.Invoke(null,new object[]{sw,assembly,component,source,target,"new",(Action<string>)(s=>{})});}catch(TargetInvocationException){failed=true;}
   if(mode=="success"){
    if(failed||!savedDrawing||File.Exists(oldDrawing)||!File.Exists(newDrawing)||closes!=1)throw new Exception("Successful rename scenario failed");
   }else{
    if(!failed||!File.Exists(oldDrawing))throw new Exception("Failure did not preserve original drawing: "+mode);
    if((mode=="collision"||mode=="bad-reference")&&renamed)throw new Exception("Preflight failed after mutating model");
    if(mode=="collision"&&File.ReadAllText(newDrawing)!="existing")throw new Exception("Overwrote existing drawing");
   }
   Console.WriteLine("PASS: "+mode);
  }
 }
}