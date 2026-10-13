using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
namespace MechKit.Core
{
    internal sealed class PartCategory
    {
        public string Name { get; set; }
        public string Prefixes { get; set; }
        public bool ExcludeFromBom { get; set; }
        public static List<PartCategory> Load(AddinSettings settings)
        {
            var saved = settings.Get("EditableCategoriesV1", null);
            if (saved == null) return new List<PartCategory> {
                new PartCategory { Name="参考件", Prefixes="参考", ExcludeFromBom=true },
                new PartCategory { Name="库存件", Prefixes=settings.StockPrefixes },
                new PartCategory { Name="外部图纸", Prefixes=settings.ExternalDrawingPrefixes } };
            return XElement.Parse(saved).Elements("category").Select(e => new PartCategory {
                Name=(string)e.Attribute("name"), Prefixes=(string)e.Attribute("prefixes") ?? "",
                ExcludeFromBom=(bool?)e.Attribute("exclude") ?? false }).ToList();
        }
        public static void Save(AddinSettings settings, IEnumerable<PartCategory> values)
        {
            var list=values.ToList();
            settings.Set("EditableCategoriesV1",new XElement("categories",list.Select(c=>new XElement("category",
                new XAttribute("name",c.Name),new XAttribute("prefixes",c.Prefixes),new XAttribute("exclude",c.ExcludeFromBom)))).ToString(SaveOptions.DisableFormatting));
            settings.StockPrefixes=list.FirstOrDefault(c=>c.Name=="库存件")?.Prefixes ?? "";
            settings.SparePrefixes="";
            settings.ExternalDrawingPrefixes=list.FirstOrDefault(c=>c.Name=="外部图纸")?.Prefixes ?? "";
        }
        public string Match(string name)
        {
            var clean=NamingOptions.GetFileNameWithoutExtension(name ?? "").Trim();
            return NamingOptionsFactory.ParsePrefixes(Prefixes,true).Where(p=>clean.StartsWith(p,StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(p=>p.Length).FirstOrDefault() ?? "";
        }
    }
}
