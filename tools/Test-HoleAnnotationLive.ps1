param([Parameter(Mandatory=$true)][string]$Drawing)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$dll=Join-Path $repo 'src\MechKit\bin\Release\MechKit.dll'
$interop=Join-Path (Split-Path $dll) 'SolidWorks.Interop.sldworks.dll'
Add-Type -Path $interop
$folder=Join-Path $repo ('artifacts\hole-annotation-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($folder)|Out-Null
$copy=Join-Path $folder 'HoleAnnotationTest.SLDDRW'
Copy-Item -LiteralPath $Drawing -Destination $copy
Add-Type -ReferencedAssemblies $interop -TypeDefinition @'
using System;using System.Reflection;using System.Runtime.InteropServices;using SolidWorks.Interop.sldworks;
public static class HoleAnnotationLiveTest {
 static void CheckTolerance(DisplayDimension d){var t=d.GetDimension2(0).Tolerance;if(t.Type!=2 || Math.Abs(t.GetMinValue())>1e-12 || Math.Abs(t.GetMaxValue()-0.00003)>1e-12)throw new Exception("Tolerance readback failed");}
 public static void Run(string dll,string path){
 var service=Assembly.LoadFrom(dll).GetType("MechKit.Features.QuickAnnotationService",true);
 var apply=service.GetMethod("Apply");var dowel=service.GetMethod("SetDowelHoles");
 var app=(ISldWorks)Marshal.GetActiveObject("SldWorks.Application");var previous=app.IActiveDoc2;int e=0,w=0;
 var doc=app.OpenDoc6(path,3,1,"",ref e,ref w);if(doc==null)throw new Exception("Open failed "+e);
 try {
 View holeView=null;DisplayDimension hole=null;
 for(var view=(View)((DrawingDoc)doc).GetFirstView();view!=null;view=(View)view.GetNextView()){
 var dims=view.GetDisplayDimensions() as object[];if(dims==null)continue;
 foreach(var x in dims){var d=x as DisplayDimension;if(d!=null && d.IsHoleCallout()){hole=d;holeView=view;break;}}if(hole!=null)break;
 }
 if(hole==null)throw new Exception("Fixture needs a native circular-cut hole callout");
 var model=holeView.ReferencedDocument;var modelDirty=model.GetSaveFlag();
 var vars=hole.GetHoleCalloutVariables() as object[];if(vars!=null && vars.Length>0)throw new Exception("Fixture must reproduce the empty-variable callout case");
 doc.ClearSelection2(true);((Annotation)hole.GetAnnotation()).Select3(false,null);
 Console.WriteLine(apply.Invoke(null,new object[]{app,"0.03/0",0,null}));CheckTolerance(hole);
 Console.WriteLine("PASS: empty-variable callout tolerance readback");
 doc.ClearSelection2(true);((Annotation)hole.GetAnnotation()).Select3(false,null);
 Console.WriteLine(dowel.Invoke(null,new object[]{app}));CheckTolerance(hole);
 Console.WriteLine("PASS: existing callout dowel setting");
 var bodies=((PartDoc)model).GetBodies2(0,false) as object[];Edge edge=null;
 foreach(var b in bodies){foreach(var x in (object[])((Body2)b).GetEdges()){var candidate=(Edge)x;var curve=(Curve)candidate.GetCurve();if(!curve.IsCircle())continue;var circle=(double[])curve.CircleParams;if(circle[6]>.001 && circle[6]<.002){edge=candidate;break;}}if(edge!=null)break;}
 if(edge==null)throw new Exception("No fixture hole edge");
 // Delete only hole callouts in the disposable drawing to test creation from an edge.
 foreach(var x in (object[])holeView.GetDisplayDimensions()){var d=(DisplayDimension)x;if(!d.IsHoleCallout())continue;doc.ClearSelection2(true);((Annotation)d.GetAnnotation()).Select3(false,null);doc.EditDelete();}
 doc.ClearSelection2(true);((DrawingDoc)doc).ActivateView(holeView.GetName2());
 if(!holeView.SelectEntity(edge,false))throw new Exception("Cannot select fixture edge");
 Console.WriteLine(dowel.Invoke(null,new object[]{app}));
 DisplayDimension generated=null;int count=0;
 foreach(var x in (object[])holeView.GetDisplayDimensions()){var d=(DisplayDimension)x;if(d.IsHoleCallout()){generated=d;count++;}}
 if(count!=1)throw new Exception("Expected one generated hole callout, got "+count);CheckTolerance(generated);
 Console.WriteLine("PASS: edge creates one associative callout with +0.03/0");
 // Repeat on the same edge: reuse rather than add a second callout.
 doc.ClearSelection2(true);holeView.SelectEntity(edge,false);Console.WriteLine(dowel.Invoke(null,new object[]{app}));
 count=0;foreach(var x in (object[])holeView.GetDisplayDimensions())if(((DisplayDimension)x).IsHoleCallout())count++;
 if(count!=1)throw new Exception("Duplicate callout created");
 if(model.GetSaveFlag()!=modelDirty)throw new Exception("Source model dirtied");
 if(!doc.Save3(1,ref e,ref w))throw new Exception("Scratch save failed "+e);
 Console.WriteLine("PASS: repeated edge reuses callout; source save flag unchanged");
 }finally{app.CloseDoc(doc.GetTitle());if(previous!=null)app.ActivateDoc3(previous.GetTitle(),false,0,ref e);}
 }
}
'@
[HoleAnnotationLiveTest]::Run($dll,$copy)
Write-Output "Verified scratch drawing: $copy"
