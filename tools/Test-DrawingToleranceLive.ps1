param([string]$DllPath = (Join-Path $PSScriptRoot '..\src\MechKit\bin\Release\MechKit.dll'))
$ErrorActionPreference='Stop'
$dir=Split-Path -Parent $DllPath
[void][Reflection.Assembly]::LoadFrom((Join-Path $dir 'SolidWorks.Interop.sldworks.dll'))
[void][Reflection.Assembly]::LoadFrom((Join-Path $dir 'SolidWorks.Interop.swconst.dll'))
Add-Type -ReferencedAssemblies @((Join-Path $dir 'SolidWorks.Interop.sldworks.dll'),(Join-Path $dir 'SolidWorks.Interop.swconst.dll')) -TypeDefinition @"
using System;using System.Reflection;using System.Runtime.InteropServices;using SolidWorks.Interop.sldworks;using SolidWorks.Interop.swconst;
public static class ToleranceScratchTest {
 public static string Run(string dllPath) {
 var service=Assembly.LoadFrom(dllPath).GetType("MechKit.Features.QuickAnnotationService",true);
 var apply=service.GetMethod("Apply",BindingFlags.Public|BindingFlags.Static);
 var app=(ISldWorks)Marshal.GetActiveObject("SldWorks.Application");var previous=app.IActiveDoc2;var previousTitle=previous==null?null:previous.GetTitle();
 var template=app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplateDrawing);
 if(string.IsNullOrEmpty(template))throw new Exception("No default drawing template");
 ModelDoc2 doc=null;
 try {
 doc=app.NewDocument(template,0,0,0) as ModelDoc2;if(doc==null)throw new Exception("Cannot create scratch drawing");
 doc.SketchManager.InsertSketch(true);var line=doc.SketchManager.CreateLine(0.05,0.05,0,0.06,0.05,0);
 doc.ClearSelection2(true);line.Select4(false,null);
 var display=doc.AddDimension2(0.055,0.07,0) as DisplayDimension;if(display==null)throw new Exception("Cannot create scratch dimension");
 var tol=display.GetDimension2(0).Tolerance;tol.Type=(int)swTolType_e.swTolBILAT;
 string result="Type after setting bilateral="+tol.Type;
 bool ok=tol.SetValues2(0,0.00005,(int)swSetValueInConfiguration_e.swSetValue_InThisConfiguration,null);
 result+=" SetValues2="+ok+" Min="+tol.GetMinValue()+" Max="+tol.GetMaxValue();
 bool legacy=tol.SetValues(0,0.00005);
 result+=" SetValues="+legacy+" Type="+tol.Type+" Min="+tol.GetMinValue()+" Max="+tol.GetMaxValue();
 int unchanged=(int)swDimensionPrecisionSettings_e.swDoNotChangePrecisionSetting;
 result+=" Precision="+display.SetPrecision3(unchanged,unchanged,2,unchanged);
 string[] presets={"0.05/0","0/-0.05","-0.01/-0.03","0.012/0","±0.02","清除公差"};
 double[] lowers={0,-0.00005,-0.00003,0,-0.00002,0};
 double[] uppers={0.00005,0,-0.00001,0.000012,0.00002,0};
 for(int i=0;i<presets.Length;i++){
 doc.ClearSelection2(true);
 if(!((Annotation)display.GetAnnotation()).Select3(false,null))throw new Exception("Cannot select scratch dimension");
 var status=(string)apply.Invoke(null,new object[]{app,presets[i],0,null});
 int expected=i==5?(int)swTolType_e.swTolNONE:i==4?(int)swTolType_e.swTolSYMMETRIC:(int)swTolType_e.swTolBILAT;
 if(!status.StartsWith("已写入 1 个") || tol.Type!=expected || (i!=5 &&
 (Math.Abs(tol.GetMinValue()-lowers[i])>1e-12 || Math.Abs(tol.GetMaxValue()-uppers[i])>1e-12)))
 throw new Exception("Failed preset "+presets[i]+": "+status+" Type="+tol.Type+" Min="+tol.GetMinValue()+" Max="+tol.GetMaxValue());
 result+="\nPASS "+presets[i]+" Type="+tol.Type+" Min="+tol.GetMinValue()+" Max="+tol.GetMaxValue();
 }
 return result;
 } finally {if(doc!=null)app.CloseDoc(doc.GetTitle());if(previousTitle!=null){int error=0;app.ActivateDoc3(previousTitle,false,0,ref error);}}
 }
}
"@
[ToleranceScratchTest]::Run([IO.Path]::GetFullPath($DllPath))
