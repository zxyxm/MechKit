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
        public const int CommandGroupId = 42117;

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
        public const int CmdAbout = 9;

        public static readonly int[] CommandIds =
        {
            CmdGenerateBom, CmdPartList, CmdPartNamingRule, CmdStandardPrefix,
            CmdBatchExport, CmdTaskPane, CmdSettings, CmdAbout
        };

        /// <summary>命令数量，图标条必须为每个命令预留一格（见 tools\Generate-Icons.ps1）。</summary>
        public const int CommandCount = 9;
    }
}
