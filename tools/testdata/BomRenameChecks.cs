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
  CheckVirtual(root,dll);
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
 static void CheckVirtual(string root,string dll){
  var service=Assembly.LoadFrom(dll).GetType("MechKit.Features.PartListService");
  var save=service.GetMethod("SaveVirtualComponentExternal",BindingFlags.Static|BindingFlags.NonPublic);
  var makeTarget=service.GetMethod("VirtualComponentTarget",BindingFlags.Static|BindingFlags.NonPublic);
  foreach(var mode in new[]{"assembly","part","collision","api-failure","wrong-reference","still-virtual","owner-save-failure","unsaved-root","invalid-name"}){
   var folder=Path.Combine(root,"virtual-"+mode);Directory.CreateDirectory(folder);
   var rootPath=Path.Combine(folder,"root.SLDASM");File.WriteAllText(rootPath,"assembly");
   bool external=false,called=false,ownerSaved=false;
   string target=Path.Combine(folder,"new"+(mode=="part"?".SLDPRT":".SLDASM"));
   var model=BomProxy.Make<ModelDoc2>(m=>m.MethodName=="GetType"?(object)(mode=="part"?1:2):null);
   var owner=BomProxy.Make<ModelDoc2>(m=>{
    if(m.MethodName=="GetPathName")return mode=="unsaved-root"?"":rootPath;
    if(m.MethodName=="EditRebuild3")return true;
    if(m.MethodName=="Save3"){if(!external)throw new Exception("Save ordering");ownerSaved=true;return mode!="owner-save-failure";}
    return null;
   });
   var component=BomProxy.Make<Component2>(m=>{
    if(m.MethodName=="GetModelDoc2")return model;
    if(m.MethodName=="get_IsVirtual")return !external||mode=="still-virtual";
    if(m.MethodName=="GetPathName")return mode=="wrong-reference"?"wrong":target;
    if(m.MethodName=="SaveVirtualComponent"){
     called=true;if(mode=="api-failure")return false;
     File.WriteAllText((string)m.Args[0],"external");external=true;return true;
    }return null;
   });
   if(mode=="collision")File.WriteAllText(target,"existing");
   bool failed=false;
   try{
    var actual=(string)makeTarget.Invoke(null,new object[]{owner,component,mode=="invalid-name"?"../bad":"new"});
    if(actual!=target)throw new Exception("Wrong root or extension");
    save.Invoke(null,new object[]{owner,component,actual});
   }catch(TargetInvocationException){failed=true;}
   bool success=mode=="assembly"||mode=="part";
   if(success&&(failed||!external||!ownerSaved))throw new Exception("External save failed: "+mode);
   if(!success&&!failed)throw new Exception("Expected safe failure: "+mode);
   if((mode=="collision"||mode=="unsaved-root"||mode=="invalid-name")&&called)throw new Exception("Preflight mutation: "+mode);
   if(mode=="collision"&&File.ReadAllText(target)!="existing")throw new Exception("Collision overwritten");
   Console.WriteLine("PASS: virtual-"+mode);
  }
 }
}
