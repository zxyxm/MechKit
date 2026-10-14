$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
Add-Type -ReferencedAssemblies System.Windows.Forms,System.Drawing -TypeDefinition @'
using System;using System.Reflection;using System.Windows.Forms;
public sealed class CountingWindow : Form {
 public int PositionChanges,Activations;
 public CountingWindow(){ShowInTaskbar=false;Width=180;Height=100;Activated+=(s,e)=>Activations++;}
 protected override void WndProc(ref Message m){if(m.Msg==0x47)PositionChanges++;base.WndProc(ref m);}
}
public static class WindowLoweringCheck {
 public static void Run(string dll){
 var type=Assembly.LoadFrom(dll).GetType("MechKit.UI.WindowLayout",true);
 var lower=type.GetMethod("LowerAfterDocumentOpened");
 var alongside=type.GetMethod("KeepAlongsideHost",BindingFlags.NonPublic|BindingFlags.Static);
 var keys=type.GetMethod("HandleWindowShortcut",BindingFlags.NonPublic|BindingFlags.Static);
 using(var host=new CountingWindow())using(var bom=new CountingWindow()){
 host.Show();bom.Show();alongside.Invoke(null,new object[]{bom,host.Handle});
 type.GetMethod("EnableEscapeToClose").Invoke(null,new object[]{bom});
 host.Activate();Application.DoEvents();int activations=host.Activations;
 for(int i=0;i<20;i++)lower.Invoke(null,new object[]{bom,host.Handle});
 Application.DoEvents();int positions=bom.PositionChanges;
 for(int i=0;i<20;i++)lower.Invoke(null,new object[]{bom,host.Handle});
 Application.DoEvents();
 if(bom.PositionChanges!=positions)throw new Exception("Repeated lowering changed z-order again");
 if(host.Activations!=activations)throw new Exception("Lowering redundantly activated host");
 if(bom.TopMost)throw new Exception("BOM became system topmost");
 if(!(bool)keys.Invoke(null,new object[]{Keys.Tab}))throw new Exception("Tab cannot restore lowered BOM");
 if(bom.TopMost)throw new Exception("Tab used system topmost");
 Console.WriteLine("PASS: repeated lowering coalesced; no redundant host activation; Tab restores normal window");
 using(var auxiliary=new CountingWindow())using(var external=new CountingWindow()){
 type.GetMethod("EnableEscapeToClose").Invoke(null,new object[]{auxiliary});
 auxiliary.Show();external.Show();external.Activate();Application.DoEvents();
 var foreground=Form.ActiveForm;
 type.GetMethod("YieldToOpenedContent",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{IntPtr.Zero});
 Application.DoEvents();
 var lowered=(System.Collections.Generic.HashSet<Form>)type.GetField("LoweredForms",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
 if(!lowered.Contains(bom) || !lowered.Contains(auxiliary))throw new Exception("Not all plugin windows yielded");
 if(Form.ActiveForm!=foreground)throw new Exception("External focus was taken back");
 if(bom.TopMost || auxiliary.TopMost)throw new Exception("Yield used system topmost");
 Console.WriteLine("PASS: all plugin windows yield without taking focus from external content");
 }
 }
 }
}
'@
[WindowLoweringCheck]::Run("$repo\src\MechKit\bin\Release\MechKit.dll")
