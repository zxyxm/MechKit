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
 [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
 public static void Run(string dll){
 var type=Assembly.LoadFrom(dll).GetType("MechKit.UI.WindowLayout",true);
 var lower=type.GetMethod("LowerAfterDocumentOpened");
 var alongside=type.GetMethod("KeepAlongsideHost",BindingFlags.NonPublic|BindingFlags.Static);
 var keys=type.GetMethod("HandleWindowShortcut",BindingFlags.NonPublic|BindingFlags.Static);
 using(var host=new CountingWindow())using(var bom=new CountingWindow()){
 host.Show();bom.Show();alongside.Invoke(null,new object[]{bom,host.Handle});
 type.GetMethod("EnableEscapeToClose").Invoke(null,new object[]{bom});
 host.Activate();Application.DoEvents();
 for(int i=0;i<20;i++)lower.Invoke(null,new object[]{bom,host.Handle});
 Application.DoEvents();int positions=bom.PositionChanges;int activations=host.Activations;
 for(int i=0;i<20;i++)lower.Invoke(null,new object[]{bom,host.Handle});
 Application.DoEvents();
 if(bom.PositionChanges!=positions)throw new Exception("Repeated lowering changed z-order again");
 if(host.Activations!=activations)throw new Exception("Lowering redundantly activated host");
 if(bom.TopMost)throw new Exception("BOM became system topmost");
 // The fake host is a WinForms Form (SW's real host is native). Give the
 // registered window focus for the shortcut check; unrelated Forms keep keys.
 bom.Activate();Application.DoEvents();
 var state=(System.Collections.Generic.HashSet<Form>)type.GetField("LoweredForms",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
 state.Remove(bom);
 bool wasFocused=bom.ContainsFocus;
 if(!(bool)keys.Invoke(null,new object[]{Keys.Tab}))throw new Exception("Tab cannot restore lowered BOM");
 if(bom.TopMost)throw new Exception("Tab used system topmost");
 Application.DoEvents();
 if(wasFocused && !state.Contains(bom))throw new Exception("Tab from foreground did not lower BOM");
 // Native SW has no Form.ActiveForm; simulate the restore branch with a marked window.
 bom.Activate();Application.DoEvents();state.Add(bom);
 keys.Invoke(null,new object[]{Keys.Tab});Application.DoEvents();
 if(state.Contains(bom))throw new Exception("Tab did not restore marked BOM");
 Console.WriteLine("PASS: Tab lowers foreground BOM and restores lowered BOM");
 using(var grid=new DataGridView()){
 grid.Columns.Add("note","Note");grid.Rows.Add("original");bom.Controls.Add(grid);
 bom.Activate();grid.Focus();grid.CurrentCell=grid.Rows[0].Cells[0];grid.BeginEdit(true);
 ((TextBox)grid.EditingControl).Text="unsaved draft";Application.DoEvents();state.Remove(bom);
 if(!grid.IsCurrentCellInEditMode)throw new Exception("Fixture did not enter cell editing");
 if(!(bool)keys.Invoke(null,new object[]{Keys.Tab}))throw new Exception("Cell editing swallowed Tab");
 Application.DoEvents();
 if(!state.Contains(bom))throw new Exception("Tab during cell editing did not lower BOM");
 var draft=grid.IsCurrentCellInEditMode ? ((TextBox)grid.EditingControl).Text : Convert.ToString(grid.Rows[0].Cells[0].Value);
 if(draft!="unsaved draft")throw new Exception("Tab lost the cell draft");
 grid.CancelEdit();grid.EndEdit();grid.CurrentCell=null;bom.Controls.Remove(grid);
 Console.WriteLine("PASS: Tab during cell editing lowers BOM and preserves input");
 }
 Console.WriteLine("PASS: repeated lowering coalesced; no redundant host activation; Tab restores normal window");
 using(var auxiliary=new CountingWindow())using(var external=new CountingWindow()){
 type.GetMethod("EnableEscapeToClose").Invoke(null,new object[]{auxiliary});
 auxiliary.Show();external.Show();external.Activate();Application.DoEvents();
 var foreground=GetForegroundWindow();
 type.GetMethod("YieldToOpenedContent",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{IntPtr.Zero});
 Application.DoEvents();
 var lowered=(System.Collections.Generic.HashSet<Form>)type.GetField("LoweredForms",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
 if(!lowered.Contains(bom) || !lowered.Contains(auxiliary))throw new Exception("Not all plugin windows yielded");
 if(foreground==external.Handle && GetForegroundWindow()!=external.Handle)throw new Exception("External focus was taken back");
 if(bom.TopMost || auxiliary.TopMost)throw new Exception("Yield used system topmost");
 Console.WriteLine("PASS: all plugin windows yield without taking focus from external content");
 }
 }
 }
}
'@
[WindowLoweringCheck]::Run("$repo\src\MechKit\bin\Release\MechKit.dll")
