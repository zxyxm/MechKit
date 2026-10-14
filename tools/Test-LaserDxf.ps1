param([Parameter(Mandatory=$true)][string]$Part)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$bin=Join-Path $repo 'src\MechKit\bin\Release'
Add-Type -Path "$bin\SolidWorks.Interop.sldworks.dll"
Add-Type -ReferencedAssemblies @("$bin\SolidWorks.Interop.sldworks.dll",'System.Core') -TypeDefinition @"
using System;using System.IO;using System.Reflection;using System.Runtime.InteropServices;using SolidWorks.Interop.sldworks;
public static class LaserTest {
public static void Run(string dll,string fixture,string folder){
Directory.CreateDirectory(folder);var path=Path.Combine(folder,"laser-test.SLDPRT");File.Copy(fixture,path,false);
var sw=(ISldWorks)Marshal.GetActiveObject("SldWorks.Application");var old=sw.IActiveDoc2;ModelDoc2 part=null;
try {int e=0,w=0;part=sw.OpenDoc6(path,1,1,"",ref e,ref w);if(part==null)throw new Exception("Open failed");sw.ActivateDoc3(part.GetTitle(),false,0,ref e);
var method=Assembly.LoadFrom(dll).GetType("MechKit.Features.LaserDxfService").GetMethod("Export",BindingFlags.Static|BindingFlags.NonPublic);
var output=(string)method.Invoke(null,new object[]{part});if(!File.Exists(output)||part.GetSaveFlag())throw new Exception("Export/source state invalid");
var before=File.ReadAllBytes(output);bool rejected=false;try{method.Invoke(null,new object[]{part});}catch(TargetInvocationException){rejected=true;}
if(!rejected||File.ReadAllBytes(output).Length!=before.Length)throw new Exception("Overwrite protection failed");
Console.WriteLine("PASS: DXF saved, source unchanged, existing output protected. "+output);
}finally{if(part!=null)sw.CloseDoc(part.GetTitle());if(old!=null){int e=0;sw.ActivateDoc3(old.GetTitle(),false,0,ref e);}}
}}
"@
[LaserTest]::Run("$bin\MechKit.dll",(Resolve-Path -LiteralPath $Part).Path,(Join-Path $repo ('artifacts\laser-test-'+[guid]::NewGuid().ToString('N'))))
