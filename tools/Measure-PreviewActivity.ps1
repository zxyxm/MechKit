param([string]$SampleFolder)
$ErrorActionPreference='Stop'
$interop='C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SolidWorks.Interop.sldworks.dll'
Add-Type -Path $interop
Add-Type -ReferencedAssemblies $interop -TypeDefinition @'
using System;using System.IO;using System.Collections.Generic;using System.Diagnostics;using System.Runtime.InteropServices;using SolidWorks.Interop.sldworks;
public static class PreviewActivityProbe {
 static int changes,loads,opens;
 static int Changed(){changes++;return 0;}
 static int Loaded(string title,string path){loads++;Console.WriteLine("LOAD "+path);return 0;}
 static int Opened(string path){opens++;Console.WriteLine("OPEN "+path);return 0;}
 public static void Run(string folder,string sampleFolder){
 var app=(ISldWorks)Marshal.GetActiveObject("SldWorks.Application");var events=(SldWorks)app;
 var active=app.IActiveDoc2;var initiallyActive=active==null?"":active.GetPathName();
 var docs=app.GetDocuments() as object[];
 if(active==null && docs!=null && docs.Length>0)active=(ModelDoc2)docs[0];
 if(active==null && !Directory.Exists(sampleFolder))throw new Exception("No document or sample folder");
 var initial=active==null?"":active.GetPathName();var countBefore=docs==null?0:docs.Length;
 Console.WriteLine("ACTIVE "+initial+" documents="+countBefore);
 var samples=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
 var assembly=active as AssemblyDoc;
 if(assembly!=null){foreach(var obj in (object[])assembly.GetComponents(false)){var c=(Component2)obj;var p=c.GetPathName();if(p.EndsWith(".sldprt",StringComparison.OrdinalIgnoreCase)&&File.Exists(p)&&!samples.ContainsKey(p))samples[p]=c.ReferencedConfiguration;if(samples.Count>=6)break;}}
 else {
 if(active!=null)samples[initial]=active.IGetActiveConfiguration().Name;
 foreach(var p in Directory.EnumerateFiles(active==null?sampleFolder:Path.GetDirectoryName(initial),"*.sldprt")){
 if(!samples.ContainsKey(p))samples[p]="";if(samples.Count>=6)break;}
 }
 events.ActiveDocChangeNotify+=Changed;events.DocumentLoadNotify2+=Loaded;events.FileOpenPostNotify+=Opened;
 try{
 foreach(var sample in samples){
 for(int attempt=0;attempt<2;attempt++){
 int c0=changes,l0=loads,o0=opens;var clock=Stopwatch.StartNew();var config=sample.Value;
 if(string.IsNullOrEmpty(config)){var names=app.GetConfigurationNames(sample.Key) as string[];config=names==null||names.Length==0?"":names[0];}
 var temp=Path.Combine(folder,Guid.NewGuid().ToString("N")+".bmp");
 var ok=app.GetPreviewBitmapFile(sample.Key,config,temp);clock.Stop();
 Console.WriteLine("PREVIEW ms="+clock.ElapsedMilliseconds+" ok="+ok+" activeChanges="+(changes-c0)+" loads="+(loads-l0)+" opens="+(opens-o0)+" "+sample.Key);
 }
 }
 var after=app.IActiveDoc2;var documentsAfter=app.GetDocuments() as object[];
 Console.WriteLine("SUMMARY activeChanges="+changes+" loads="+loads+" opens="+opens+" documentsAfter="+(documentsAfter==null?0:documentsAfter.Length)+" activeSame="+((after==null?"":after.GetPathName())==initiallyActive));
 }finally{events.ActiveDocChangeNotify-=Changed;events.DocumentLoadNotify2-=Loaded;events.FileOpenPostNotify-=Opened;}
 }
}
'@
$folder=Join-Path (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts') ('preview-probe-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($folder)|Out-Null
[PreviewActivityProbe]::Run($folder,$SampleFolder)
