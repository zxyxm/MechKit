using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SwView = SolidWorks.Interop.sldworks.View;

namespace MechKit.Features
{
    // Import native, associative dimensions. Never synthesize manufacturing tolerances
    // from a BREP or treat successful insertion as proof of manufacturing completeness.
    internal static class MachiningDrawingBuilder
    {
        internal static string DimensionKey(string name)
        { return string.Join("@", (name ?? "").Split('@').Take(2)); }

        internal static double FitScale(double size)
        {
            if(double.IsNaN(size) || double.IsInfinity(size) || size<=0)
                throw new InvalidOperationException("模型包络尺寸无效。");
            foreach(var scale in new[]{5.0,2,1,.5,.25,.2,.1,.05,.02,.01})
                if(size*scale<=.050+1e-9) return scale;
            throw new InvalidOperationException("零件过大，无法在 A3 图框中清晰排版。");
        }

        // Conservative: axial bores and end faces are fully expressed by an end,
        // side and axial section. Side milling, radial holes and unknown surfaces
        // keep the general layout rather than silently losing their projections.
        private static bool CanSimplifyRoundBody(Body2 body, double[] axis)
        {
            if(Math.Max(Math.Abs(axis[0]),Math.Max(Math.Abs(axis[1]),Math.Abs(axis[2])))<.999) return false;
            foreach(var face in PlateMath.Items<Face2>(body.GetFaces()))
            {
                var surface=(Surface)face.GetSurface();
                double[] direction;
                if(surface.IsCylinder())
                {
                    var c=(double[])surface.CylinderParams;
                    direction=new[]{c[3],c[4],c[5]};
                }
                else if(surface.IsPlane()) direction=(double[])face.Normal;
                else return false;
                if(direction==null || Math.Abs(PlateMath.Dot(axis,direction))<.999) return false;
            }
            return true;
        }

        internal static List<SwView> Build(ModelDoc2 part, ModelDoc2 model,
            string path, string config, List<string> messages)
        {
            var bodies=PlateMath.Items<Body2>(((PartDoc)part).GetBodies2(0,false)).ToList();
            if(bodies.Count!=1) throw new InvalidOperationException("车铣件出图暂支持单实体零件；多实体请拆分后出图。");
            if(bodies[0].IsSheetMetal()) throw new InvalidOperationException("钣金件请使用钣金出图流程。");
            var bounds=(double[])bodies[0].GetBodyBox();
            if(bounds==null || bounds.Length!=6) throw new InvalidOperationException("无法读取实体包络。");
            var size=Math.Max(bounds[3]-bounds[0],Math.Max(bounds[4]-bounds[1],bounds[5]-bounds[2]));
            var expected=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var visited=new HashSet<string>(StringComparer.Ordinal);
            for(var f=part.FirstFeature() as Feature;f!=null;f=f.GetNextFeature() as Feature)
                Inventory(f,expected,visited,messages);

            // A side-on view of the dominant cylindrical axis exposes axial steps.
            // The end view is a linked projection, never a separately oriented snapshot.
            var cylinders=PlateMath.Items<Face2>(bodies[0].GetFaces())
                .Where(f=>((Surface)f.GetSurface()).IsCylinder()).OrderByDescending(f=>f.GetArea()).ToList();
            var orientationIndex=1; // swFrontView; GetModelViewNames includes *Normal To at index 0.
            double[] cylinderAxis=null;
            if(cylinders.Count>0)
            {
                var c=(double[])((Surface)cylinders[0].GetSurface()).CylinderParams;
                cylinderAxis=new[]{c[3],c[4],c[5]};
                if(Math.Abs(c[5])>.9) orientationIndex=4;
                if(Math.Max(Math.Abs(c[3]),Math.Max(Math.Abs(c[4]),Math.Abs(c[5])))<.999)
                    messages.Add("主圆柱轴倾斜于标准基准面，需补正视/辅助视图。");
            }
            var doc=(DrawingDoc)model;
            var names=(string[])part.GetModelViewNames();
            if(names==null || names.Length<=orientationIndex) throw new InvalidOperationException("无法读取标准模型视图。");
            PlateDrawingService.SetScale(doc,FitScale(size));
            var main=doc.CreateDrawViewFromModelView3(path,names[orientationIndex],.145,.180,0);
            if(main==null) throw new InvalidOperationException("无法创建车铣件主视图。");
            main.ReferencedConfiguration=config;
            main.UseSheetScale=1;
            var views=new List<SwView>{main};
            var compact=cylinderAxis!=null && CanSimplifyRoundBody(bodies[0],cylinderAxis);
            // A Y-axis part's axial projection is above the front view, not to its right.
            var endAbove=compact && Math.Abs(cylinderAxis[1])>.999;
            var right=PlateDrawingService.Project(model,main,endAbove?.145:.230,endAbove?.245:.180);
            views.Add(right);
            if(!compact)
            {
                views.Add(PlateDrawingService.Project(model,main,.060,.180));
                // Reserve the right-hand aligned projection slot for the axial section.
                // If no axial section is possible, retain the ordinary back projection.
                if(cylinderAxis==null || Math.Abs(PlateMath.Dot(PlateDrawingService.Axis(right,2),cylinderAxis))<.999)
                    views.Add(PlateDrawingService.Project(model,right,.315,.180));
                views.Add(PlateDrawingService.Project(model,main,.145,.245));
                views.Add(PlateDrawingService.Project(model,main,.145,.110));
            }
            messages.Add(compact ? "回转件简化布局：侧视、端视及轴向剖视；省略重复的反向和上下投影。" :
                "含侧向或尚未识别的结构，保留通用视图布局。");
            foreach(var view in views) {view.ReferencedConfiguration=config;view.UseSheetScale=1;}
            model.ForceRebuild3(false);
            if(compact && PlateDrawingService.NeedsAdditionalHoleView(bodies[0],views))
            {
                var opposite=PlateDrawingService.Project(model,main,endAbove?.145:.060,endAbove?.110:.180);
                opposite.ReferencedConfiguration=config;opposite.UseSheetScale=1;views.Add(opposite);
                messages.Add("另一端存在独立孔特征，补充反向端视，避免漏掉孔标注。");
            }
            if(cylinders.Count>0)
            {
                // Cut the end view through the dominant cylinder axis. Coordinates in
                // a drawing-view sketch are unscaled and relative to that view's origin.
                var c=(double[])((Surface)cylinders[0].GetSurface()).CylinderParams;
                var normal=PlateDrawingService.Axis(right,2);
                if(Math.Abs(PlateMath.Dot(normal,new[]{c[3],c[4],c[5]}))>.999)
                {
                    var a=(double[])right.ModelToViewTransform.ArrayData;
                    var pos=(double[])right.Position;
                    var cx=(c[0]*a[0]+c[1]*a[3]+c[2]*a[6])*a[12]+a[9];
                    var cy=(c[0]*a[1]+c[1]*a[4]+c[2]*a[7])*a[12]+a[10];
                    var x=(cx-pos[0])/right.ScaleDecimal;
                    var y=(cy-pos[1])/right.ScaleDecimal;
                    model.ClearSelection2(true);doc.ActivateView(right.GetName2());
                    var line=model.SketchManager.CreateLine(x,y-size,0,x,y+size,0);
                    if(line==null) throw new InvalidOperationException("无法创建轴向剖切线。");
                    // Keep SOLIDWORKS' native section alignment on a third-angle sheet.
                    // A vertical cutting line produces a horizontally aligned section.
                    var section=doc.CreateSectionViewAt5(pos[0]+.085,pos[1],0,"A",0,null,0);
                    if(section==null) throw new InvalidOperationException("轴向剖视创建失败，未发布不完整图纸。");
                    section.ReferencedConfiguration=config;section.UseSheetScale=1;
                    views.Add(section);
                    model.ClearSelection2(true);
                }
                else messages.Add("圆柱轴未正对端视图，未自动建立轴向剖视。");
            }
            // SW may automatically change sheet scale when inserting its first view.
            PlateDrawingService.SetScale(doc,FitScale(size));
            foreach(var view in views) view.UseSheetScale=1;
            model.ForceRebuild3(false);
            for(var i=0;i<views.Count;i++)
            {
                var box=(double[])views[i].GetOutline();
                if(box[0]<.025 || box[1]<.070 || box[2]>.390 || box[3]>.283)
                    throw new InvalidOperationException("视图超出可用绘图区："+views[i].GetName2()+" ["+string.Join(",",box.Select(v=>v.ToString("F4")))+"]，未发布图纸。");
                for(var j=0;j<i;j++)
                {
                    var other=(double[])views[j].GetOutline();
                    if(Math.Min(box[2],other[2])>Math.Max(box[0],other[0]) &&
                       Math.Min(box[3],other[3])>Math.Max(box[1],other[1]))
                        throw new InvalidOperationException("视图重叠，未发布图纸。");
                }
            }
            model.ClearSelection2(true); doc.ActivateView("");
            var types=swInsertAnnotation_e.swInsertDimensionsMarkedForDrawing |
                swInsertAnnotation_e.swInsertDimensionsNotMarkedForDrawing |
                swInsertAnnotation_e.swInsertHoleWizardProfileDimensions |
                swInsertAnnotation_e.swInsertHoleWizardLocationDimensions |
                swInsertAnnotation_e.swInsertInstanceCounts | swInsertAnnotation_e.swInsertCThreads | swInsertAnnotation_e.swInsertDatums |
                swInsertAnnotation_e.swInsertGTols | swInsertAnnotation_e.swInsertSFSymbols;
            doc.InsertModelAnnotations3((int)swImportModelItemsSource_e.swImportModelItemsFromEntireModel,
                (int)types,true,true,false,false);

            var actual=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var patternCounts=new List<Tuple<DisplayDimension,string>>();
            var count=0;
            foreach(var view in views)
            {
                model.ClearSelection2(true);
                for(var d=view.GetFirstDisplayDimension5();d!=null;d=d.GetNext5())
                {
                    var dimension=d.GetDimension2(0);
                    if(dimension!=null)
                    {
                        var key=DimensionKey(dimension.FullName);
                        actual.Add(key);
                        var fields=key.Split('@');
                        var feature=fields.Length==2?((PartDoc)part).FeatureByName(fields[1]) as Feature:null;
                        var pattern=feature!=null && feature.GetTypeName2()=="CirPattern"
                            ? feature.GetDefinition() as CircularPatternFeatureData : null;
                        if(pattern!=null && Math.Abs(dimension.SystemValue-pattern.TotalInstances)<1e-8)
                        {
                            // Keep SW's associative count dimension; do not calculate or
                            // rewrite the quantity prefix on a native hole callout.
                            patternCounts.Add(Tuple.Create(d,feature.Name));
                        }
                    }
                    var annotation=(Annotation)d.GetAnnotation();
                    if(annotation.IsDangling()) throw new InvalidOperationException("模型尺寸导入后出现悬空标注。");
                    annotation.Select3(true,null); count++;
                }
                if(model.SelectionManager is SelectionMgr selection && selection.GetSelectedObjectCount2(-1)>0)
                    if(!model.Extension.AlignDimensions((int)swAlignDimensionType_e.swAlignDimensionType_AutoArrange,.006))
                        messages.Add(view.GetName2()+"：自动排布失败，请调整文字及引线。");
            }
            for(var i=0;i<patternCounts.Count;i++)
            {
                // Editing an imported dimension's prefix can dirty the source model.
                // A drawing-only label keeps the model dimension completely read-only.
                var label=model.InsertNote(patternCounts[i].Item2+" 数量：") as Note;
                if(label!=null) ((Annotation)label.GetAnnotation()).SetPosition2(.260,.250-i*.012,0);
                ((Annotation)patternCounts[i].Item1.GetAnnotation()).SetPosition2(.295,.245-i*.012,0);
            }
            model.ClearSelection2(true);
            if(count<3) throw new InvalidOperationException("模型可导入尺寸不足，无法输出车铣加工图。导入实体请先补充关联尺寸。");
            var missing=expected.Except(actual,StringComparer.OrdinalIgnoreCase).OrderBy(x=>x).ToArray();
            messages.Add("模型尺寸覆盖 "+(expected.Count-missing.Length)+"/"+expected.Count+"；已导入尺寸 "+count+" 个（不含随后补充的原生孔标注）。");
            if(missing.Length>0) messages.Add("未进入图纸的模型尺寸："+string.Join("、",missing));
            messages.Add("待复核：检查内孔台阶/退刀槽的剖视表达、孔系定位、尺寸基准、重复尺寸和引线交叉。模型未定义的尺寸及公差不自动推定。");
            return views;
        }

        private static void Inventory(Feature feature, HashSet<string> dimensions,
            HashSet<string> visited, List<string> messages)
        {
            if(!visited.Add(feature.Name) || feature.IsSuppressed()) return;
            var type=feature.GetTypeName2();
            if(type=="Imported" || type=="ImportedBody")
                messages.Add("含导入实体，模型尺寸不代表全部几何特征，需人工补全制造尺寸。");
            for(var d=feature.GetFirstDisplayDimension() as DisplayDimension;d!=null;
                d=feature.GetNextDisplayDimension(d) as DisplayDimension)
            {
                var dimension=d.GetDimension2(0);
                if(dimension!=null) dimensions.Add(DimensionKey(dimension.FullName));
            }
            for(var child=feature.GetFirstSubFeature() as Feature;child!=null;child=child.GetNextSubFeature() as Feature)
                Inventory(child,dimensions,visited,messages);
        }
    }
}
