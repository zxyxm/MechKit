using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;

namespace MechKit.Features
{
    // All lengths in metres; tolerances below are geometric comparison tolerances,
    // not manufacturing tolerances or user-specified plate aspect ratios.
    internal static class PlateMath
    {
        public const double Epsilon = 1e-7;
        public static bool LeaderSegmentsCross(double ax, double ay, double bx, double by,
            double cx, double cy, double dx, double dy)
        {
            var p = (bx-ax)*(cy-ay)-(by-ay)*(cx-ax);
            var q = (bx-ax)*(dy-ay)-(by-ay)*(dx-ax);
            var r = (dx-cx)*(ay-cy)-(dy-cy)*(ax-cx);
            var s = (dx-cx)*(by-cy)-(dy-cy)*(bx-cx);
            if (Math.Abs(p)<1e-12 && Math.Abs(q)<1e-12 && Math.Abs(r)<1e-12 && Math.Abs(s)<1e-12)
            {
                if (Math.Abs(bx-ax)>Math.Abs(by-ay))
                    return Math.Min(Math.Max(ax,bx),Math.Max(cx,dx))-Math.Max(Math.Min(ax,bx),Math.Min(cx,dx)) > Epsilon;
                return Math.Min(Math.Max(ay,by),Math.Max(cy,dy))-Math.Max(Math.Min(ay,by),Math.Min(cy,dy)) > Epsilon;
            }
            return p*q < -1e-20 && r*s < -1e-20;
        }
        public static double Dot(double[] a, double[] b) { return a[0]*b[0]+a[1]*b[1]+a[2]*b[2]; }
        public static double[] Scale(double[] a, double s) { return new[] { a[0]*s, a[1]*s, a[2]*s }; }
        public static double[] Cross(double[] a, double[] b)
        { return new[] { a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0] }; }
        public static double[] Unit(double[] a)
        {
            var length = Math.Sqrt(Dot(a,a));
            if (length < Epsilon) throw new InvalidOperationException("无法确定板件方向。");
            return Scale(a, 1/length);
        }
        public static bool Parallel(double[] a, double[] b) { return Math.Abs(Dot(Unit(a),Unit(b))) > 1-1e-7; }
        public static bool PlateDimensions(double x, double y, double thickness)
        { return thickness > Epsilon && x > thickness + Epsilon && y > thickness + Epsilon; }
        public static double ChooseScale(double length, double width, double thickness)
        {
            // Reserve left/bottom for the supplied template's requirements and title block.
            foreach (var scale in new[] {1.0, .5, .25, .2, .1, .05, .02, .01})
                if (length*scale <= .205 && (2*width+thickness)*scale <= .145) return scale;
            throw new InvalidOperationException("板件过大，无法在内置 A3 图框中清晰排版。");
        }
        public static IEnumerable<T> Items<T>(object array)
        { return (array as object[] ?? new object[0]).OfType<T>(); }
    }

    internal sealed class PlatePlane
    {
        public Face2 Face;
        public double[] Normal;
        public double Offset;
        public double Area;
    }

    internal sealed class PlateGeometry
    {
        public Body2 Body;
        public PlatePlane Front, Right;
        public double Thickness;
        public List<PlatePlane> Planes;
        public List<Edge> Edges;
        public bool HasUnclassifiedSlots;

        public static double Extreme(Body2 body, double[] axis, bool maximum)
        {
            var direction = PlateMath.Scale(axis, maximum ? 1 : -1);
            double x, y, z;
            if (!body.GetExtremePoint(direction[0],direction[1],direction[2],out x,out y,out z))
                throw new InvalidOperationException("无法读取实体精确包络。");
            return PlateMath.Dot(new[] {x,y,z},axis);
        }
        public double Span(double[] axis) { return Extreme(Body,axis,true)-Extreme(Body,axis,false); }

        public static PlateGeometry Inspect(ModelDoc2 model)
        {
            var part = model as PartDoc;
            if (part == null) throw new InvalidOperationException("不是零件文档。");
            var bodies = PlateMath.Items<Body2>(part.GetBodies2(0,false)).ToList();
            if (bodies.Count != 1) throw new InvalidOperationException("非单实体板件（空实体或多实体），跳过。");
            var body = bodies[0];
            if (body.IsSheetMetal()) throw new InvalidOperationException("钣金件，按规则不出图。");
            // Also excludes suppressed/unfolded sheet-metal histories.
            for (var f = model.FirstFeature() as Feature; f != null; f = f.GetNextFeature() as Feature)
                if (f.GetTypeName2() == "SheetMetal" || f.GetTypeName2() == "SMBaseFlange")
                    throw new InvalidOperationException("具有钣金特征，按规则不出图。");

            var result = ForHoleInspection(body);
            // Both largest opposing outer planes must bound the entire solid.
            // No name/material-only decision, and no arbitrary fixed width/thickness ratio.
            foreach (var plane in result.Planes.OrderByDescending(p=>p.Area))
            {
                if (Math.Abs(plane.Offset-Extreme(body,plane.Normal,true)) > PlateMath.Epsilon) continue;
                var back = result.Planes.Where(p=>PlateMath.Dot(p.Normal,plane.Normal)<-1+1e-7 &&
                    Math.Abs(-p.Offset-Extreme(body,plane.Normal,false)) < PlateMath.Epsilon)
                    .OrderByDescending(p=>p.Area).FirstOrDefault();
                if (back == null) continue;
                var side = result.Planes.Where(p=>Math.Abs(PlateMath.Dot(p.Normal,plane.Normal))<1e-7)
                    .OrderByDescending(p=>p.Area).FirstOrDefault();
                if (side == null) continue;
                var thickness=result.Span(plane.Normal);
                var width=result.Span(side.Normal);
                var length=result.Span(PlateMath.Cross(plane.Normal,side.Normal));
                if (!PlateMath.PlateDimensions(length,width,thickness) || plane.Area <= side.Area || back.Area <= side.Area) continue;
                result.Front=plane; result.Right=side; result.Thickness=thickness;
                result.HasUnclassifiedSlots=result.Planes.Any(p=> !IsHoleFace(p.Face) && PlateMath.Parallel(p.Normal,plane.Normal) &&
                    Math.Abs(PlateMath.Dot(p.Normal,plane.Normal)*p.Offset-plane.Offset)>PlateMath.Epsilon &&
                    Math.Abs(PlateMath.Dot(p.Normal,plane.Normal)*p.Offset+back.Offset)>PlateMath.Epsilon);
                return result;
            }
            throw new InvalidOperationException("没有可确认的两个平行外侧大面与较小厚度，不套用板件规则。");
        }

        private static bool IsHoleFace(Face2 face)
        {
            var feature=face.GetFeature() as Feature;
            return feature!=null && feature.GetTypeName2()=="HoleWzd";
        }

        internal static PlateGeometry ForHoleInspection(Body2 body)
        {
            var result=new PlateGeometry {Body=body,Edges=PlateMath.Items<Edge>(body.GetEdges()).ToList(),Planes=new List<PlatePlane>()};
            foreach(var face in PlateMath.Items<Face2>(body.GetFaces()))
            {
                var surface=face.GetSurface() as Surface;
                if(surface==null || !surface.IsPlane()) continue;
                var p=(double[])surface.PlaneParams;
                var normal=PlateMath.Unit((double[])face.Normal);
                result.Planes.Add(new PlatePlane {Face=face,Normal=normal,Area=face.GetArea(),
                    Offset=PlateMath.Dot(new[]{p[3],p[4],p[5]},normal)});
            }
            return result;
        }
    }
}
