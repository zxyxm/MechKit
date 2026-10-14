param([Parameter(Mandatory=$true)][string]$Part)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$dll=Join-Path $repo 'src\MechKit\bin\Release\MechKit.dll'
$interop=Join-Path (Split-Path $dll) 'SolidWorks.Interop.sldworks.dll'
Add-Type -Path $interop
$folder=Join-Path $repo ('artifacts\purchase-link-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($folder)|Out-Null
$copy=Join-Path $folder '20261014-6061-PurchaseLinkTest.SLDPRT'
$before=(Get-FileHash -LiteralPath $Part).Hash
Copy-Item -LiteralPath $Part -Destination $copy
Add-Type -ReferencedAssemblies $interop -TypeDefinition @'
using System;using System.Reflection;using System.Runtime.InteropServices;using SolidWorks.Interop.sldworks;
public static class PurchaseLinkLiveTest {
 public static void Run(string dll,string path){
 var a=Assembly.LoadFrom(dll);var service=a.GetType("MechKit.Features.PurchaseLinkService",true);
 var save=service.GetMethod("Save");var validate=service.GetMethod("Validate");
 string property=(string)service.GetField("PropertyName").GetValue(null);
 string url="https://example.com/item?id=123&sku=456";
 foreach(string invalid in new[]{"javascript:alert(1)","file:///C:/test","not-a-url"}){
 bool rejected=false;try{validate.Invoke(null,new object[]{invalid});}catch(TargetInvocationException ex){rejected=ex.InnerException is ArgumentException;}
 if(!rejected)throw new Exception("Invalid URL accepted");}
 var app=(ISldWorks)Marshal.GetActiveObject("SldWorks.Application");var previous=app.IActiveDoc2;ModelDoc2 doc=null;int e=0,w=0;
 try{
 save.Invoke(null,new object[]{app,path,url});
 doc=app.OpenDoc6(path,1,1,"",ref e,ref w);if(doc==null)throw new Exception("Open saved copy failed");
 var manager=doc.Extension.CustomPropertyManager[""];string raw,resolved;bool wasResolved,linked;
 manager.Get6(property,false,out raw,out resolved,out wasResolved,out linked);
 if(raw!=url || manager.GetType2(property)!=30)throw new Exception("Saved custom property readback mismatch");
 var options=Activator.CreateInstance(a.GetType("MechKit.Features.PartListOptions",true));
 options.GetType().GetProperty("ReadCustomProperties").SetValue(options,true,null);
 var row=a.GetType("MechKit.Features.PartListService",true).GetMethod("FromPart").Invoke(null,new object[]{app,doc,options});
 if(row==null || (string)row.GetType().GetProperty("PurchaseLink").GetValue(row,null)!=url)throw new Exception("BOM did not read saved property");
 Console.WriteLine("PASS: saved text property survives reopen and reads into BOM");
 var saveProperty=service.GetMethod("SaveProperty");
 string installation="\u5b89\u88c5\u8bf4\u660e",remark="\u5907\u6ce8";
 foreach(var name in new[]{installation,remark}){
 saveProperty.Invoke(null,new object[]{app,path,name,"Inline property text"});
 manager.Get6(name,false,out raw,out resolved,out wasResolved,out linked);
 if(raw!="Inline property text")throw new Exception("Inline text property readback failed");
 }
 row=a.GetType("MechKit.Features.PartListService",true).GetMethod("FromPart").Invoke(null,new object[]{app,doc,options});
 if((string)row.GetType().GetProperty("AssemblyNote").GetValue(row,null)!="Inline property text" ||
 (string)row.GetType().GetProperty("Remark").GetValue(row,null)!="Inline property text")throw new Exception("BOM text properties not read back");
 Console.WriteLine("PASS: installation note and remark saved and read into BOM");
 save.Invoke(null,new object[]{app,path,""});manager.Get6(property,false,out raw,out resolved,out wasResolved,out linked);
 if(raw!="")throw new Exception("Clear did not persist");
 Console.WriteLine("PASS: URL validation and clear operation");
 }finally{if(doc!=null)app.CloseDoc(doc.GetTitle());if(previous!=null)app.ActivateDoc3(previous.GetTitle(),false,0,ref e);}
 }
}
'@
[PurchaseLinkLiveTest]::Run($dll,$copy)
if((Get-FileHash -LiteralPath $Part).Hash -ne $before){throw 'Original part changed'}
Write-Output 'PASS: original model hash unchanged'
