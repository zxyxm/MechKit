using System;
using System.IO;
using System.Linq;
using MechKit.Core;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace MechKit.Features
{
    internal static class LaserDxfService
    {
        internal static string Export(ModelDoc2 model)
        {
            if(model==null || model.GetType()!=SwUtils.DocPart)
                throw new InvalidOperationException("请先打开需要激光切割的零件。");
            var path=model.GetPathName();
            object ownerPaths,ownerTitles;
            if(string.IsNullOrWhiteSpace(path) || !File.Exists(path) ||
                model.Extension.IsVirtualComponent3(out ownerPaths,out ownerTitles))
                throw new InvalidOperationException("请先将零件保存为独立文件。");
            var target=Path.ChangeExtension(path,".dxf");
            if(File.Exists(target)) throw new IOException("已有同名 DXF，未覆盖：\r\n"+target);
            var part=(PartDoc)model;
            var bodies=PlateMath.Items<Body2>(part.GetBodies2(0,false)).ToList();
            if(bodies.Count!=1) throw new InvalidOperationException("请使用单实体零件，避免多实体切割轮廓遗漏。");
            var face=PlateMath.Items<Face2>(bodies[0].GetFaces())
                .Where(f=>((Surface)f.GetSurface()).IsPlane())
                .OrderByDescending(f=>f.GetArea()).FirstOrDefault();
            if(face==null) throw new InvalidOperationException("零件没有可导出的平面。");
            var temporary=Path.Combine(Path.GetDirectoryName(path),".MechKit-laser-"+Guid.NewGuid().ToString("N")+".dxf");
            try
            {
                model.ClearSelection2(true);
                if(!((Entity)face).Select4(false,null)) throw new InvalidOperationException("最大平面选择失败。");
                if(!part.ExportToDWG2(temporary,path,(int)swExportToDWG_e.swExportToDWG_ExportSelectedFacesOrLoops,
                    true,null,false,false,0,null) || !File.Exists(temporary) || new FileInfo(temporary).Length==0)
                    throw new IOException("最大面 DXF 导出失败。");
                File.Move(temporary,target);
                return target;
            }
            finally
            {
                model.ClearSelection2(true);
                if(File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
