using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using MechKit.Core;
using SwView = SolidWorks.Interop.sldworks.View;

namespace MechKit.Features
{
    internal sealed class PlateDrawingResult
    {
        public string Source, Configuration, Status, Message, Output;
        public override string ToString() { return Status+"\t"+Source+" ["+Configuration+"]\t"+Message+"\t"+Output; }
    }

    internal sealed class PlateDrawingService
    {
        private readonly ISldWorks app;
        private readonly NamingOptions naming;
        private readonly bool machining;
        public readonly List<PlateDrawingResult> Results = new List<PlateDrawingResult>();
        public string ReportPath { get; private set; }
        public bool Cancelled { get; private set; }

        public PlateDrawingService(ISldWorks app, AddinSettings settings) : this(app,settings,false) { }

        public PlateDrawingService(ISldWorks app, AddinSettings settings, bool machining)
        { this.app=app; naming=NamingOptionsFactory.FromSettings(settings); this.machining=machining; }

        private sealed class Candidate
        {
            public string Path;
            public HashSet<string> Configurations=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        // Runs on SOLIDWORKS' UI/STA thread. Progress may pump messages BETWEEN parts only.
        public static bool SupportsDocumentType(int documentType)
        { return documentType == SwUtils.DocPart || documentType == SwUtils.DocAssembly; }

        public void Run(ModelDoc2 source, Func<int,int,string,bool> progress, Action<PlateDrawingResult> completed)
        {
            if (source == null || !SupportsDocumentType(source.GetType()))
                throw new InvalidOperationException("请先激活需要出图的零件或装配体。");
            var originalTitle=source.GetTitle();
            try
            {
                var candidates=new Dictionary<string,Candidate>(StringComparer.OrdinalIgnoreCase);
                if(source.GetType()==SwUtils.DocPart) CollectPart(source,candidates,completed);
                else Collect(source.ConfigurationManager.ActiveConfiguration.GetRootComponent3(true),candidates,completed);
                var template=ExtractTemplate();
                var index=0;
                foreach(var candidate in candidates.Values.OrderBy(c=>c.Path,StringComparer.OrdinalIgnoreCase))
                {
                    if (!progress(index++,candidates.Count,Path.GetFileName(candidate.Path))) { Cancelled=true; break; }
                    var result=new PlateDrawingResult {Source=candidate.Path,
                        Configuration=string.Join("; ",candidate.Configurations),Status="跳过",Output=""};
                    try
                    {
                        // Same-basename policy cannot encode multiple referenced configurations safely.
                        if (candidate.Configurations.Count!=1)
                            throw new InvalidOperationException("同一零件引用多个配置，同名文件会冲突；请分别确定出图配置。");
                        var target=Path.ChangeExtension(candidate.Path,".SLDDRW");
                        if(File.Exists(target) || SwUtils.FindOpenDocument(app,target)!=null)
                            throw new InvalidOperationException("已有同名工程图，保留并跳过。");
                        Draw(candidate.Path,candidate.Configurations.Single(),target,template,result);
                    }
                    catch(Exception ex) { result.Message=ex.Message; }
                    Results.Add(result); completed(result);
                }
            }
            finally
            {
                int error=0;
                try
                {
                    var last = Results.LastOrDefault(r => !string.IsNullOrEmpty(r.Output));
                    if (last != null)
                    {
                        int warnings = 0;
                        var drawing = SwUtils.FindOpenDocument(app, last.Output) ?? app.OpenDoc6(last.Output,
                            SwUtils.DocDrawing, (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref error, ref warnings);
                        if (drawing == null) throw new InvalidOperationException("无法打开最后生成的图纸（" + error + "）：" + last.Output);
                        if (app.ActivateDoc3(drawing.GetTitle(), false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref error) == null)
                            throw new InvalidOperationException("无法激活最后生成的图纸（" + error + "）。");
                    }
                    else app.ActivateDoc3(originalTitle,false,(int)swRebuildOnActivation_e.swDontRebuildActiveDoc,ref error);
                }
                catch(Exception ex) { Log.Warn("显示出图结果失败："+ex.Message); }
                WriteReport(source.GetPathName());
            }
        }

        private void CollectPart(ModelDoc2 part, Dictionary<string,Candidate> targets, Action<PlateDrawingResult> completed)
        {
            var path=part.GetPathName();
            var config=part.ConfigurationManager.ActiveConfiguration.Name;
            try
            {
                if(string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    throw new InvalidOperationException("零件尚未保存，请先另存为独立文件。");
                // Opening a part and pressing Draw is an explicit request, including
                // purchased/standard parts. Naming exclusions apply only to assembly batches.
                object ownerPaths, ownerTitles;
                if(part.Extension.IsVirtualComponent3(out ownerPaths,out ownerTitles))
                    throw new InvalidOperationException("虚拟零件请先另存为独立文件。");
                var candidate=new Candidate{Path=path};
                candidate.Configurations.Add(config);
                targets.Add(path,candidate);
            }
            catch(Exception ex)
            {
                var result=new PlateDrawingResult{Source=string.IsNullOrEmpty(path)?part.GetTitle():path,
                    Configuration=config,Status="跳过",Message=ex.Message,Output=""};
                Results.Add(result); completed(result);
            }
        }

        private void Collect(Component2 parent, Dictionary<string,Candidate> targets, Action<PlateDrawingResult> completed)
        {
            if(parent==null) throw new InvalidOperationException("无法读取装配体根组件，请退出大型设计审阅模式。");
            foreach(var component in PlateMath.Items<Component2>(parent.GetChildren()))
            {
                var path=component.GetPathName();
                try
                {
                    if(component.IsSuppressed()) throw new InvalidOperationException("组件已压缩。");
                    var kind=naming.RecognizeDesignNode(path);
                    if(kind==DesignNodeKind.Reference || kind==DesignNodeKind.Standard || kind==DesignNodeKind.Stock ||
                       kind==DesignNodeKind.Spare || kind==DesignNodeKind.ExternalDrawing)
                        throw new InvalidOperationException("按 MechKit 命名规则排除："+kind);
                    if(string.Equals(Path.GetExtension(path),".sldasm",StringComparison.OrdinalIgnoreCase))
                    {
                        if(component.GetChildren()==null) throw new InvalidOperationException("子装配体未解析，请先设为还原状态。");
                        Collect(component,targets,completed); continue;
                    }
                    if(component.IsVirtual || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                        throw new InvalidOperationException("虚拟、未保存或丢失的零件，请先另存为独立文件。");
                    if(!string.Equals(Path.GetExtension(path),".sldprt",StringComparison.OrdinalIgnoreCase)) continue;
                    Candidate c;
                    if(!targets.TryGetValue(path,out c)) targets[path]=c=new Candidate{Path=path};
                    c.Configurations.Add(component.ReferencedConfiguration);
                }
                catch(Exception ex)
                {
                    var r=new PlateDrawingResult{Source=string.IsNullOrEmpty(path)?component.Name2:path,
                        Configuration=component.ReferencedConfiguration,Status="跳过",Message=ex.Message,Output=""};
                    Results.Add(r); completed(r);
                }
            }
        }

        public static string ExtractTemplate()
        {
            var folder=AppPaths.Ensure(Path.Combine(AppPaths.Root,"drawing-templates"));
            var path=Path.Combine(folder,"plate-a3-third-angle-v1.drwdot");
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("MechKit.Resources.drawings.plate-a3-third-angle.drwdot"))
            {
                if(stream==null) throw new InvalidOperationException("插件缺少内置工程图模板。");
                // Refresh the extracted copy from the embedded template; the resource is authoritative.
                using(var file=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.Read)) stream.CopyTo(file);
            }
            return path;
        }

        private void Activate(ModelDoc2 doc)
        {
            int error=0;
            if(app.ActivateDoc3(doc.GetTitle(),false,(int)swRebuildOnActivation_e.swDontRebuildActiveDoc,ref error)==null)
                throw new InvalidOperationException("无法激活文档："+doc.GetTitle()+"（"+error+"）");
        }

        private void Draw(string path,string config,string target,string template,PlateDrawingResult result)
        {
            ModelDoc2 part=SwUtils.FindOpenDocument(app,path), drawing=null;
            var openedPart=false;
            var staging=Path.Combine(Path.GetDirectoryName(target),".MechKit-"+Guid.NewGuid().ToString("N")+".SLDDRW");
            try
            {
                if(part==null)
                {
                    int errors=0,warnings=0;
                    part=app.OpenDoc6(path,SwUtils.DocPart,(int)(swOpenDocOptions_e.swOpenDocOptions_Silent|
                        swOpenDocOptions_e.swOpenDocOptions_ReadOnly),config,ref errors,ref warnings);
                    openedPart=part!=null;
                    if(part==null) throw new InvalidOperationException("零件无法打开（"+errors+"）。");
                }
                if(part.GetSaveFlag()) throw new InvalidOperationException("零件有未保存修改，请先保存后再出图。");
                if(!string.Equals(part.ConfigurationManager.ActiveConfiguration.Name,config,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("装配引用配置与已打开零件的活动配置不同，请激活引用配置后重试。");
                var geometry=machining?null:PlateGeometry.Inspect(part);
                result.Status="失败"; // From this point failures are generation failures, not exclusions.
                drawing=(ModelDoc2)app.NewDocument(template,0,0,0);
                if(drawing==null) throw new InvalidOperationException("无法新建工程图。");
                var drw=(DrawingDoc)drawing;
                var notes=Requirements(drw);
                SetScale(drw,1);
                var messages=new List<string>();
                List<SwView> views;
                if(machining)
                {
                    views=MachiningDrawingBuilder.Build(part,drawing,path,config,messages);
                    var body=PlateMath.Items<Body2>(((PartDoc)part).GetBodies2(0,false)).Single();
                    AddHoles(drawing,views,CollectHoles(PlateGeometry.ForHoleInspection(body),messages),messages);
                }
                else
                {

                Activate(part);
                part.ClearSelection2(true);
                var selection=(SelectionMgr)part.SelectionManager;
                var frontData=selection.CreateSelectData(); frontData.Mark=1;
                var rightData=selection.CreateSelectData(); rightData.Mark=2;
                if(!((Entity)geometry.Front.Face).Select4(false,frontData) || !((Entity)geometry.Right.Face).Select4(true,rightData))
                    throw new InvalidOperationException("无法选择板件定向基准面。");
                Activate(drawing);
                var main=drw.CreateRelativeView(path,.175,.230,
                    (int)swRelativeViewCreationDirection_e.swRelativeViewCreationDirection_FRONT,
                    (int)swRelativeViewCreationDirection_e.swRelativeViewCreationDirection_RIGHT);
                part.ClearSelection2(true);
                if(main==null) throw new InvalidOperationException("相对模型视图创建失败。");
                main.ReferencedConfiguration=config;
                var x=Axis(main,0); var y=Axis(main,1);
                if(!PlateMath.Parallel(Axis(main,2),geometry.Front.Normal))
                    throw new InvalidOperationException("主视图未正对板件大面。");
                if(geometry.Span(y)>geometry.Span(x)+PlateMath.Epsilon) main.Angle=Math.PI/2;
                x=Axis(main,0); y=Axis(main,1);
                var length=geometry.Span(x); var width=geometry.Span(y);
                var scale=PlateMath.ChooseScale(length,width,geometry.Thickness);
                SetScale(drw,scale); main.UseSheetScale=1; drawing.ForceRebuild3(false);
                var mainY=.262-width*scale/2;
                main.Position=new[]{.175,mainY};
                var sideY=mainY-(width+geometry.Thickness)*scale/2-.023;
                var backY=sideY-(width+geometry.Thickness)*scale/2-.021;
                var side=Project(drawing,main,.175,sideY);
                var back=Project(drawing,side,.175,backY);
                views=new List<SwView>{main,side,back};
                SetScale(drw,scale);
                foreach(var view in views) view.UseSheetScale=1;
                drawing.ForceRebuild3(false);
                if(PlateMath.Items<Loop2>(geometry.Front.Face.GetLoops()).Where(l=>l.IsOuter())
                    .Any(l=>PlateMath.Items<Edge>(l.GetEdges()).Count()>4))
                    messages.Add("外轮廓含多段边/缺口；请复核开口槽及局部槽尺寸。");
                Envelope(drawing,main,geometry,0,.175,mainY+width*scale/2+.009);
                Envelope(drawing,main,geometry,1,.175-length*scale/2-.012,mainY);
                Envelope(drawing,side,geometry,1,.175-length*scale/2-.012,sideY);
                var holes=CollectHoles(geometry,messages);
                // A fourth linked side view, only if a hole mouth needs it and there is room.
                var uncovered=holes.FirstOrDefault(h=>!views.Any(v=>FacesView(h.Plane,v)));
                if(uncovered!=null && Math.Abs(PlateMath.Dot(uncovered.Plane.Normal,Axis(main,0)))>1-1e-7)
                {
                    var sign=PlateMath.Dot(uncovered.Plane.Normal,Axis(main,0))>0?1:-1;
                    var endX=.175+sign*(length*scale/2+geometry.Thickness*scale/2+.04);
                    if(endX>.065 && endX<.340)
                    {
                        var end=Project(drawing,main,endX,mainY); end.UseSheetScale=1; views.Add(end);
                    }
                }
                AddHoles(drawing,views,holes,messages);
                if(geometry.HasUnclassifiedSlots)
                    messages.Add("存在台阶/槽或孔底平面；槽型未自动确认，槽尺寸及专用公差需复核。");
                }
                drawing.ClearSelection2(true); drw.ActivateView(""); drawing.ForceRebuild3(false);
                var annotationSnapshot=AnnotationSnapshot(drw);
                Verify(drawing,path,config,views.Count,notes,annotationSnapshot);
                if(part.GetSaveFlag()) throw new InvalidOperationException("源零件出现未保存状态，停止发布此图；未保存源零件。");
                int saveErrors=0,saveWarnings=0;
                if(!drawing.Extension.SaveAs(staging,0,(int)swSaveAsOptions_e.swSaveAsOptions_Silent,null,ref saveErrors,ref saveWarnings) || saveErrors!=0)
                    throw new InvalidOperationException("保存失败（"+saveErrors+"）。");
                if(saveWarnings!=0) messages.Add("SolidWorks 保存警告码："+saveWarnings);
                app.CloseDoc(drawing.GetTitle()); drawing=null;
                int openErrors=0,openWarnings=0;
                drawing=app.OpenDoc6(staging,SwUtils.DocDrawing,(int)swOpenDocOptions_e.swOpenDocOptions_Silent,"",ref openErrors,ref openWarnings);
                if(drawing==null || openErrors!=0) throw new InvalidOperationException("工程图重开失败（"+openErrors+"）。");
                Verify(drawing,path,config,views.Count,notes,annotationSnapshot);
                if(openWarnings!=0) messages.Add("SolidWorks 重开警告码："+openWarnings);
                app.CloseDoc(drawing.GetTitle()); drawing=null;
                // Rename in the SAME directory preserves relative model references. Move never overwrites.
                File.Move(staging,target);
                result.Output=target;
                result.Status=messages.Count==0?"已生成":"已生成·待复核";
                result.Message=messages.Count==0?"关联视图、长宽厚及已识别原生孔标注；保存重开核验通过。":string.Join("；",messages.Distinct());
            }
            finally
            {
                // Cleanup must not hide the original COM failure or close a user's document.
                try { if(drawing!=null) app.CloseDoc(drawing.GetTitle()); }
                catch(Exception ex) { Log.Warn("关闭临时工程图："+ex.Message); }
                try { if(File.Exists(staging)) File.Delete(staging); }
                catch(Exception ex) { Log.Warn("清理临时工程图："+ex.Message); }
                try
                {
                    var current=SwUtils.FindOpenDocument(app,path);
                    if(current!=null) { current.ClearSelection2(true); if(openedPart && !current.GetSaveFlag()) app.CloseDoc(current.GetTitle()); }
                }
                catch(Exception ex) { Log.Warn("清理零件选择："+ex.Message); }
            }
        }

        internal static double[] Axis(SwView view,int column)
        {
            var a=(double[])view.ModelToViewTransform.ArrayData;
            return PlateMath.Unit(new[]{a[column],a[3+column],a[6+column]});
        }

        internal static void SetScale(DrawingDoc doc,double scale)
        {
            var sheet=(Sheet)doc.GetCurrentSheet(); var p=(double[])sheet.GetProperties2();
            sheet.SetProperties2((int)p[0],(int)p[1],scale>=1?scale:1,scale>=1?1:1/scale,false,p[5],p[6],p[7]!=0);
        }

        internal static SwView Project(ModelDoc2 model,SwView parent,double x,double y)
        {
            var doc=(DrawingDoc)model; model.ClearSelection2(true);
            doc.ActivateView(parent.GetName2());
            if(!model.Extension.SelectByID2(parent.GetName2(),"DRAWINGVIEW",0,0,0,false,0,null,0))
                throw new InvalidOperationException("无法选择父投影视图。");
            var view=doc.CreateUnfoldedViewAt3(x,y,0,false);
            model.ClearSelection2(true);
            if(view==null) throw new InvalidOperationException("关联投影视图创建失败。");
            return view;
        }

        private sealed class Boundary { public object Entity; public bool Arc; }
        private static Boundary BoundaryAt(PlateGeometry g,double[] axis,double extremum)
        {
            foreach(var edge in g.Edges)
                foreach(var vertex in new[]{edge.GetStartVertex() as Vertex,edge.GetEndVertex() as Vertex})
                    if(vertex!=null && Math.Abs(PlateMath.Dot((double[])vertex.GetPoint(),axis)-extremum)<PlateMath.Epsilon)
                        return new Boundary{Entity=vertex};
            foreach(var edge in g.Edges)
            {
                var curve=(Curve)edge.GetCurve(); if(!curve.IsCircle()) continue;
                var c=(double[])curve.CircleParams;
                if(Math.Abs(PlateMath.Dot(axis,new[]{c[3],c[4],c[5]}))>1e-7) continue;
                var center=PlateMath.Dot(c,axis);
                if(Math.Abs(Math.Abs(extremum-center)-c[6])<PlateMath.Epsilon)
                    return new Boundary{Entity=edge,Arc=true};
            }
            throw new InvalidOperationException("包络极值不在可关联的顶点/圆弧上，需人工标注。");
        }

        private static void Envelope(ModelDoc2 model,SwView view,PlateGeometry g,int direction,double x,double y)
        {
            var axis=Axis(view,direction);
            var min=PlateGeometry.Extreme(g.Body,axis,false); var max=PlateGeometry.Extreme(g.Body,axis,true);
            var a=BoundaryAt(g,axis,min); var b=BoundaryAt(g,axis,max);
            model.ClearSelection2(true); ((DrawingDoc)model).ActivateView(view.GetName2());
            if(!view.SelectEntity(a.Entity,false) || !view.SelectEntity(b.Entity,true))
                throw new InvalidOperationException("包络实体选择失败。");
            var display=(DisplayDimension)(direction==0?model.AddHorizontalDimension2(x,y,0):model.AddVerticalDimension2(x,y,0));
            if(display==null) throw new InvalidOperationException("包络标注创建失败。");
            var dimension=(Dimension)display.GetDimension2(0);
            if(a.Arc) dimension.SetArcEndCondition(1,(int)swArcEndCondition_e.swArcEndConditionMax);
            if(b.Arc) dimension.SetArcEndCondition(2,(int)swArcEndCondition_e.swArcEndConditionMax);
            model.ForceRebuild3(false);
            if(Math.Abs(dimension.SystemValue-(max-min))>PlateMath.Epsilon)
                throw new InvalidOperationException("包络标注与实体最大尺寸不一致，未发布工程图。");
            model.ClearSelection2(true);
        }

        private sealed class Hole
        {
            public Edge Edge;
            public PlatePlane Plane;
            public string Feature;
            public string AxisKey;
        }
        private static List<Hole> CollectHoles(PlateGeometry geometry,List<string> messages)
        {
            var result=new List<Hole>();
            foreach(var plane in geometry.Planes)
            {
                // Only exterior entrance planes; recesses and unusual entrances are reviewed.
                if(Math.Abs(plane.Offset-PlateGeometry.Extreme(geometry.Body,plane.Normal,true))>PlateMath.Epsilon) continue;
                foreach(var loop in PlateMath.Items<Loop2>(plane.Face.GetLoops()))
                {
                    if(loop.IsOuter()) continue;
                    var edges=PlateMath.Items<Edge>(loop.GetEdges()).ToList();
                    var edge=edges.FirstOrDefault(e=>((Curve)e.GetCurve()).IsCircle());
                    if(edge==null || edges.Count!=1)
                    { messages.Add("存在非整圆内轮廓（槽/开口），需复核槽尺寸。"); continue; }
                    var c=(double[])((Curve)edge.GetCurve()).CircleParams;
                    if(!PlateMath.Parallel(new[]{c[3],c[4],c[5]},plane.Normal)) continue;
                    var adjacent=PlateMath.Items<Face2>(edge.GetTwoAdjacentFaces2()).ToList();
                    var owner=adjacent.Select(f=>f.GetFeature() as Feature)
                        .FirstOrDefault(f=>f!=null && f.GetTypeName2()=="HoleWzd");
                    // Resolve the actual hole wall, not the surrounding plate's feature.
                    // GetSeedFeature also follows mirrored/copied bodies; pattern-only lookup
                    // misses those and must not take precedence over the original source.
                    var seed=adjacent.Where(f=>!((Surface)f.GetSurface()).IsPlane())
                        .Select(ResolveHoleSeed).FirstOrDefault(f=>f!=null) ?? owner;
                    if(owner!=null)
                    {
                        bool? entry=IsWizardEntrance(owner,c,plane.Normal);
                        if(entry==false) continue; // exit edge must not repeat the entry's counterbore/thread callout
                        if(entry==null) { messages.Add("孔组 "+owner.Name+" 的加工入口未确认，需人工标注。"); continue; }
                        var data=owner.GetDefinition() as WizardHoleFeatureData2;
                        if(data!=null && Math.Max(data.HoleDepth,Math.Max(data.ThreadDepth,data.TapDrillDepth))>geometry.Span(plane.Normal)+PlateMath.Epsilon)
                            messages.Add("孔组 "+owner.Name+" 深度超过该方向实体厚度；已保留模型参数，请复核。");
                    }
                    var axis=PlateMath.Unit(new[]{c[3],c[4],c[5]});
                    if(axis.First(a=>Math.Abs(a)>1e-7)<0) axis=PlateMath.Scale(axis,-1);
                    var offset=PlateMath.Dot(c,axis);
                    var key=string.Join("|",new[]{c[0]-axis[0]*offset,c[1]-axis[1]*offset,c[2]-axis[2]*offset,c[6],axis[0],axis[1],axis[2]}
                        .Select(n=>Math.Round(n,7).ToString("R",System.Globalization.CultureInfo.InvariantCulture)));
                    result.Add(new Hole{Edge=edge,Plane=plane,Feature=seed==null?"":seed.Name,AxisKey=key});
                }
            }
            return result;
        }

        private static bool? IsWizardEntrance(Feature owner,double[] center,double[] normal)
        {
            // The positioning sketch, unlike the hole profile sketch, is perpendicular to
            // the hole axis. Its model-to-sketch Z identifies the actual entrance plane.
            for(var child=owner.GetFirstSubFeature() as Feature;child!=null;child=child.GetNextSubFeature() as Feature)
            {
                if(child.GetTypeName2()!="ProfileFeature") continue;
                var sketch=child.GetSpecificFeature2() as Sketch;
                if(sketch==null) continue;
                var m=(double[])sketch.ModelToSketchTransform.ArrayData;
                var z=new[]{m[2],m[5],m[8]};
                if(!PlateMath.Parallel(z,normal)) continue;
                return Math.Abs(PlateMath.Dot(center,z)*m[12]+m[11])<PlateMath.Epsilon;
            }
            return null;
        }

        private static bool FacesView(PlatePlane plane,SwView view)
        { return PlateMath.Dot(plane.Normal,Axis(view,2))>1-1e-7; }

        internal static bool NeedsAdditionalHoleView(Body2 body, List<SwView> views)
        {
            var holes=CollectHoles(PlateGeometry.ForHoleInspection(body),new List<string>());
            return holes.Any(h=>!views.Any(v=>FacesView(h.Plane,v)) &&
                (h.Feature=="" || !holes.Any(other=>other.Feature==h.Feature && views.Any(v=>FacesView(other.Plane,v)))));
        }

        private static Feature ResolveHoleSeed(Face2 face)
        {
            return face.GetSeedFeature() ?? face.GetPatternSeedFeature() as Feature ?? face.GetFeature() as Feature;
        }

        private sealed class HolePlacement
        {
            public Hole Hole;
            public SwView View;
            public double X, Y, Distance;
            public bool Left;
        }

        private void AddHoles(ModelDoc2 model,List<SwView> views,List<Hole> holes,List<string> messages)
        {
            // One native callout per source feature, across all reused instances and views.
            // SOLIDWORKS owns the quantity prefix; never parse, recalculate or validate it.
            var grouped=new HashSet<string>();
            var through=new HashSet<string>();
            var placements = new List<HolePlacement>();
            var math = (MathUtility)app.GetMathUtility();
            foreach (var hole in holes)
            {
                var view = views.FirstOrDefault(v => FacesView(hole.Plane,v));
                if (view == null)
                {
                    if(hole.Feature=="" || !holes.Any(other=>other.Feature==hole.Feature && views.Any(v=>FacesView(other.Plane,v))))
                        messages.Add("有孔入口未被当前视图覆盖，需补充侧视图和孔标注。");
                    continue;
                }
                var circle = (double[])((Curve)hole.Edge.GetCurve()).CircleParams;
                var point = (MathPoint)math.CreatePoint(new[] { circle[0], circle[1], circle[2] });
                var position = (double[])((MathPoint)point.MultiplyTransform(view.ModelToViewTransform)).ArrayData;
                var box = (double[])view.GetOutline();
                var left = position[0] - box[0] < box[2] - position[0];
                placements.Add(new HolePlacement { Hole=hole, View=view, X=position[0], Y=position[1], Left=left,
                    Distance=Math.Min(Math.Abs(position[0]-box[0]),Math.Abs(box[2]-position[0])) });
            }
            // Prefer the outermost reusable instance instead of the arbitrary topology order.
            var selected = placements.GroupBy(p => p.Hole.Feature == "" ? "unknown:"+placements.IndexOf(p) : "feature:"+p.Hole.Feature)
                .SelectMany(g => g.OrderBy(p=>p.Distance).ThenByDescending(p=>p.Y).Take(1))
                .OrderByDescending(p=>p.Y).ToList();
            var placed = new List<DisplayDimension>();
            var lanes = new Dictionary<string,double>();
            foreach(var placement in selected)
            {
                var hole = placement.Hole;
                if(hole.Feature!="" && grouped.Contains(hole.Feature)) continue;
                var view=placement.View;
                var outline=(double[])view.GetOutline();
                var lane=view.GetName2()+(placement.Left?"/left":"/right");
                double previous;
                var textY=placement.Y;
                if(lanes.TryGetValue(lane,out previous)) textY=Math.Min(textY,previous-.014);
                if(textY<.090)
                { messages.Add("孔标注超出预留空间，已停止增加该处标注，请人工排版补充。"); continue; }
                if(views.Any(v=>v!=view && textY<((double[])v.GetOutline())[3]+.006 && textY>((double[])v.GetOutline())[1]-.006))
                    messages.Add("孔标注与其他视图处于相近高度，请复核引线排版。");
                model.ClearSelection2(true); ((DrawingDoc)model).ActivateView(view.GetName2());
                if(!view.SelectEntity(hole.Edge,false)) { messages.Add("孔入口边选择失败，需人工孔标注。"); continue; }
                var textX=placement.Left?outline[0]-.018:outline[2]+.012;
                var display=(DisplayDimension)((DrawingDoc)model).AddHoleCallout2(textX,textY,0);
                if(display==null) { messages.Add("原生孔标注失败（导入孔/非孔特征），需人工核对规格。"); continue; }
                var annotation=(Annotation)display.GetAnnotation();
                bool clear=false;
                for(int attempt=0;attempt<7;attempt++)
                {
                    var y=textY-attempt*.004;
                    if(y<.090 || y<outline[1]-.018) break;
                    if(!annotation.SetPosition2(textX,y,0)) continue;
                    model.GraphicsRedraw2();
                    if(!placed.Any(other=>CalloutLeadersCross(display,other))) {textY=y;clear=true;break;}
                }
                if(!clear)
                {
                    RemoveHoleCallout(model,display);
                    messages.Add("孔标注在靠近孔的一侧找不到不交叉的排布位置，需人工补充该孔组标注。");
                    continue;
                }
                var text=DimensionText(display);
                var isThrough=text.Contains("贯穿") || text.IndexOf("THRU",StringComparison.OrdinalIgnoreCase)>=0;
                var throughKey=hole.AxisKey+"|"+text;
                if(hole.Feature=="" && isThrough)
                {
                    if(!through.Add(throughKey))
                    {
                        RemoveHoleCallout(model,display);
                        continue;
                    }
                }
                if(hole.Feature=="") messages.Add("孔组源特征无法回读，请复核标注。");
                if(hole.Feature!="") grouped.Add(hole.Feature);
                placed.Add(display);
                lanes[lane]=textY;
                // Blind parameters may intentionally exceed stock thickness; never rewrite them.
                if(text.Length==0) messages.Add("原生孔标注文本无法回读，请复核。");
            }
        }

        private static bool CalloutLeadersCross(DisplayDimension first, DisplayDimension second)
        {
            var a=(Annotation)first.GetAnnotation(); var b=(Annotation)second.GetAnnotation();
            for(int i=0;i<a.GetLeaderCount();i++)
            for(int j=0;j<b.GetLeaderCount();j++)
            {
                var p=a.GetLeaderPointsAtIndex(i) as double[];
                var q=b.GetLeaderPointsAtIndex(j) as double[];
                if(p==null || q==null) continue;
                for(int k=0;k+5<p.Length;k+=3)
                for(int l=0;l+5<q.Length;l+=3)
                    if(PlateMath.LeaderSegmentsCross(p[k],p[k+1],p[k+3],p[k+4],q[l],q[l+1],q[l+3],q[l+4])) return true;
            }
            return false;
        }

        private static void RemoveHoleCallout(ModelDoc2 model,DisplayDimension display)
        {
            model.ClearSelection2(true);
            var fullName=((Dimension)display.GetDimension2(0)).FullName;
            if(!model.Extension.SelectByID2(fullName,"DIMENSION",0,0,0,false,0,null,0) || !model.Extension.DeleteSelection2(0))
                throw new InvalidOperationException("无法移除重复孔组标注。");
        }

        private static string DimensionText(DisplayDimension dimension)
        {
            var data=(DisplayData)dimension.GetDisplayData(); var b=new StringBuilder();
            if(data!=null) for(var i=0;i<data.GetTextCount();i++) b.Append(data.GetTextAtIndex(i)).Append(" ");
            return b.ToString();
        }

        private static string Requirements(DrawingDoc drawing)
        {
            var view=(SwView)drawing.GetFirstView();
            var texts=new List<string>();
            foreach(var annotation in PlateMath.Items<Annotation>(view.GetAnnotations()))
            {
                if(annotation.GetType()!=(int)swAnnotationType_e.swNote) continue;
                var note=annotation.GetSpecificAnnotation() as Note;
                if(note!=null && note.GetText().Contains("技术要求")) texts.Add(note.GetText());
            }
            if(texts.Count==0) throw new InvalidOperationException("模板技术要求未读取到。");
            return string.Join("\n",texts);
        }

        private static string AnnotationSnapshot(DrawingDoc doc)
        {
            var values=new List<string>();
            foreach(var view in PlateMath.Items<SwView>(((Sheet)doc.GetCurrentSheet()).GetViews()))
                for(var d=view.GetFirstDisplayDimension5() as DisplayDimension;d!=null;d=d.GetNext5() as DisplayDimension)
                {
                    var dim=(Dimension)d.GetDimension2(0);
                    values.Add(view.GetName2()+"|"+dim.Name+"|"+dim.SystemValue.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"|"+DimensionText(d));
                }
            return string.Join("\n",values.OrderBy(v=>v,StringComparer.Ordinal));
        }

        private static void Verify(ModelDoc2 model,string source,string config,int expectedViews,string notes,string annotations)
        {
            var doc=(DrawingDoc)model; var sheet=(Sheet)doc.GetCurrentSheet();
            var p=(double[])sheet.GetProperties2();
            if(p[4]!=0 || Math.Abs(p[5]-.420)>PlateMath.Epsilon || Math.Abs(p[6]-.297)>PlateMath.Epsilon)
                throw new InvalidOperationException("图纸幅面/第三角属性核验失败。");
            if(Requirements(doc)!=notes) throw new InvalidOperationException("模板技术要求发生变化。");
            if(AnnotationSnapshot(doc)!=annotations) throw new InvalidOperationException("保存重开后标注数值/文本发生变化。");
            var views=PlateMath.Items<SwView>(sheet.GetViews()).ToList();
            if(views.Count!=expectedViews) throw new InvalidOperationException("工程图视图数量核验失败。");
            var dimensions=0;
            for(var i=0;i<views.Count;i++)
            {
                var view=views[i];
                if(!string.Equals(view.GetReferencedModelName(),source,StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(view.ReferencedConfiguration,config,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("工程图模型/配置引用核验失败。");
                if(Math.Abs(view.ScaleDecimal-p[2]/p[3])>1e-8) throw new InvalidOperationException("视图比例不一致。");
                if(i>0 && view.GetBaseView()==null) throw new InvalidOperationException("投影视图关联丢失。");
                for(var d=view.GetFirstDisplayDimension5() as DisplayDimension;d!=null;d=d.GetNext5() as DisplayDimension)
                {
                    if(((Annotation)d.GetAnnotation()).IsDangling()) throw new InvalidOperationException("检测到悬空标注，未发布工程图。");
                    dimensions++;
                }
            }
            if(dimensions<3) throw new InvalidOperationException("长宽厚标注不完整。");
        }

        private void WriteReport(string source)
        {
            var folder=AppPaths.Ensure(Path.Combine(AppPaths.Root,"plate-drawing-reports"));
            ReportPath=Path.Combine(folder,(machining?"车铣件出图-":"板件出图-")+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".txt");
            File.WriteAllText(ReportPath,"源文档："+source+"\r\n"+(Cancelled?"用户已停止；已生成文件保留。":"处理结束。")+
                (machining?"\r\n范围：关联六向视图、模型尺寸、孔标注和已有制造注解；模型尺寸覆盖不等于制造尺寸完整，请检查待复核项。\r\n":"\r\n范围：关联视图、包络、可识别原生孔规格。槽、公差和密集标注请按待复核项检查。\r\n")+
                string.Join("\r\n",Results.Select(r=>r.ToString())),new UTF8Encoding(true));
        }
    }
}
