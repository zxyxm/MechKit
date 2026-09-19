namespace MechKit
{
    /// <summary>
    /// 插件的固定标识。修改 GUID 相当于变成另一个插件，
    /// 会导致注册表中的旧注册项失效，请谨慎修改。
    /// </summary>
    internal static class AddinConstants
    {
        /// <summary>插件 CLSID（COM 注册 + SOLIDWORKS 加载都依赖它）。</summary>
        public const string AddinGuid = "A7F3C1E2-5B4D-4E8A-9C21-3D6F0B7A5E10";

        /// <summary>COM ProgID。</summary>
        public const string ProgId = "MechKit.Addin";

        /// <summary>SOLIDWORKS「工具 > 插件」列表中显示的名称。</summary>
        public const string Title = "MechKit";

        /// <summary>SOLIDWORKS「工具 > 插件」列表中显示的说明。</summary>
        public const string Description = "MechKit：图号 / 材料 / 加工件数量一键汇总，一键生成 BOM，批量导出与属性批处理";

        /// <summary>CommandManager 命令组 ID，需在插件之间保持唯一。</summary>
        // 42117 是旧版固定按钮布局。动态前缀/中间名加入后改用新 ID，
        // 避免 SOLIDWORKS 继续复用旧 CommandManager 注册表缓存。
        public const int CommandGroupId = 42121;

        /// <summary>
        /// 命令组 ID 的可轮换数量（42121 ~ 42121 + Count - 1）。
        /// SOLIDWORKS 明确规定：命令按钮集合发生变化（增删按钮）时必须换用新的
        /// UserID，否则会沿用上一次的布局缓存。保存命名规则后需要立即刷新选项卡，
        /// 因此每次重建都取一个新的 ID，把旧组删掉，无需重启 SOLIDWORKS。
        /// </summary>
        public const int CommandGroupIdCount = 20;

        public static readonly int[] LegacyCommandGroupIds = { 42117, 42118, 42119, 42120 };

        /// <summary>前缀快捷按钮最多数量（图标条里为此预留了 12 格）。</summary>
        public const int MaxPrefixCommands = 12;

        /// <summary>分隔线按钮的 UserID（图标条里紧跟 9 个命令图标的那一格）。</summary>
        public const int PrefixSpacerUserId = 900;

        /// <summary>动态前缀命令的 UserID 起点；每个按钮必须有不同的持久 ID。</summary>
        public const int PrefixCommandUserIdBase = 1000;

        /// <summary>中间名快捷按钮最多数量。</summary>
        public const int MaxMiddleNameCommands = 12;

        /// <summary>前缀组与中间名组之间的分隔线 UserID。</summary>
        public const int MiddleNameSpacerUserId = 901;

        /// <summary>动态中间名命令的 UserID 起点。</summary>
        public const int MiddleNameCommandUserIdBase = 2000;

        public const int MaxMachinedLevel2Commands = 12;
        public const int MaxMachinedLevel3Commands = 12;
        public const int MachinedLevel2CommandUserIdBase = 4000;
        public const int MachinedLevel3CommandUserIdBase = 5000;

        /// <summary>加工件快捷命名：写入当天日期。</summary>
        public const int CmdMachinedDate = 3000;

        /// <summary>加工件快捷命名：在日期段之后加入参考材料 6061。</summary>
        public const int CmdMachinedSeparator = 3001;

        /// <summary>撤回最近一次 MechKit 快捷命名。</summary>
        public const int CmdMachinedUndo = 3002;

        /// <summary>把选中零件或装配体文件名中的下划线转换为短横线。</summary>
        // 3003 曾以“菜单 + 工具栏”方式注册。改用新 ID，确保升级后
        // SOLIDWORKS 重建命令项，并仅供 CommandManager 选项卡使用。
        public const int CmdConvertUnderscores = 3004;

        /// <summary>给选中组件文件名增加“装配-”前缀。</summary>
        public const int CmdAssemblyName = 3005;

        /// <summary>把选中组件命名为“参考-原名称”，并归为参考件。</summary>
        public const int CmdReferencePart = 3006;

        /// <summary>命令项的用户 ID（存入注册表用于刷新命令组）。</summary>
        public const int CmdGenerateBom = 1;
        public const int CmdPartList = 2;
        public const int CmdPartNamingRule = 3;
        public const int CmdStandardPrefix = 4;
        public const int CmdBatchExport = 5;
        // 6 曾用于“属性工具”，保留空号，避免旧版 SOLIDWORKS 命令映射串位。
        public const int CmdTaskPane = 7;
        // 8 曾用于“个人配置”；新“设置”使用新 ID，强制 SOLIDWORKS 刷新旧按钮文字与布局。
        public const int CmdSettings = 10;
        /// <summary>把 MechKit 便携配置直接导出到桌面。</summary>
        public const int CmdExportConfiguration = 11;
        public const int CmdAbout = 9;

        public static readonly int[] CommandIds =
        {
            CmdGenerateBom, CmdPartList, CmdPartNamingRule, CmdStandardPrefix, CmdReferencePart,
            CmdBatchExport, CmdTaskPane, CmdExportConfiguration, CmdSettings, CmdAbout
        };

        /// <summary>图标条里的固定图标数量；“导出配置”复用批量导出的导出图标。</summary>
        public const int CommandCount = 9;
    }
}
