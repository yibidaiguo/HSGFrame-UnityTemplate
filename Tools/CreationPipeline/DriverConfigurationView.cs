using System;
using System.Collections.Generic;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>一格下游配置的解析结果：值、配没配、是不是台账托管的。</summary>
    /// <param name="Value">当前值；**密钥恒为空串**，密钥的值一次都不往外带（决策 5、78）。</param>
    /// <param name="IsConfigured">配没配：非密钥看值非空，密钥看键在不在。</param>
    /// <param name="IsLedgerOwned">值是不是来自下游对象台账。台账托管的格不该手填——改 local.json 不生效。</param>
    public sealed record DriverConfigurationCell(string Value, bool IsConfigured, bool IsLedgerOwned);

    /// <summary>
    /// 「这个 driver 的这一格配成什么了」——**全仓唯一的判据**。
    ///
    /// 存在的理由是一次真出过的错：同一个问题原本有两套实现——
    /// <see cref="HostPackageInventory"/> 一套（桥接包页 + bridge.inventory 命令），
    /// 面板下游页自己又一套。两套都只读 <c>local.json</c>，于是
    /// <c>bridge.ensure</c> 已经建好并回填台账的那几样（任务表 / 多维表格 / 各种父节点）
    /// 在两个页面上都显示「未配」，而人照提示手填进 local.json **根本不生效**——
    /// 因为 <see cref="BridgeInvoker"/> 把台账压在本机配置之上。
    ///
    /// 摆一个填了没用的输入框比不摆更坏：人会以为自己配好了，然后去别处找为什么还是不通。
    ///
    /// **取值顺序必须与 <see cref="BridgeInvoker"/> 一致**：先本机配置，台账有就盖上去。
    /// 面板显示的值与真正调用时用的值不是同一个，面板就是在骗人。
    /// </summary>
    public static class DriverConfigurationView
    {
        /// <summary>没配时的状态文案。</summary>
        public const string NotConfigured = "未配";

        /// <summary>配好了时的状态文案。</summary>
        public const string Configured = "已配";

        /// <summary>
        /// 解析一格非密钥配置。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="driverName">driver 名。</param>
        /// <param name="fieldName">字段名。</param>
        public static DriverConfigurationCell Resolve(string repositoryRoot, string driverName, string fieldName)
        {
            var settings = LocalBridgeSettings.Load(repositoryRoot);
            var ledger = DownstreamObjectLedger.Load(repositoryRoot);
            return Resolve(settings, ledger, driverName, fieldName);
        }

        /// <summary>
        /// 解析一格非密钥配置（调用方已经加载好本机配置与台账时用这一支，省得逐格重读文件）。
        /// </summary>
        /// <param name="settings">本机配置。</param>
        /// <param name="ledger">下游对象台账。</param>
        /// <param name="driverName">driver 名。</param>
        /// <param name="fieldName">字段名。</param>
        public static DriverConfigurationCell Resolve(
            LocalBridgeSettings settings,
            DownstreamObjectLedger ledger,
            string driverName,
            string fieldName)
        {
            var value = ReadLocalValue(settings, driverName, fieldName);

            // 台账压在本机配置之上——与 BridgeInvoker 同序。
            var ledgerValues = ledger?.ReadAll(driverName ?? "")
                ?? (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(StringComparer.Ordinal);
            if (ledgerValues.TryGetValue(fieldName ?? "", out var ledgerValue) && !string.IsNullOrEmpty(ledgerValue))
            {
                return new DriverConfigurationCell(ledgerValue, true, true);
            }

            return new DriverConfigurationCell(value, !string.IsNullOrEmpty(value), false);
        }

        /// <summary>
        /// 解析一格密钥：**只判键在不在，值一次都不取**。
        /// 台账里永远没有密钥（决策 5），所以这一支不看台账。
        /// </summary>
        /// <param name="repositoryRoot">仓库根目录。</param>
        /// <param name="secretFieldName">密钥键名。</param>
        public static DriverConfigurationCell ResolveSecret(string repositoryRoot, string secretFieldName)
        {
            return ResolveSecret(LocalBridgeSettings.Load(repositoryRoot), secretFieldName);
        }

        /// <summary>解析一格密钥（调用方已经加载好本机配置时用这一支）。</summary>
        /// <param name="settings">本机配置。</param>
        /// <param name="secretFieldName">密钥键名。</param>
        public static DriverConfigurationCell ResolveSecret(LocalBridgeSettings settings, string secretFieldName)
        {
            // out 参数只用来判「有没有」，随即丢掉，绝不往外带。
            var present = settings != null && settings.TryGetSecret(secretFieldName ?? "", out _);
            return new DriverConfigurationCell("", present, false);
        }

        /// <summary>把一格解析结果翻成「已配 / 未配」。</summary>
        /// <param name="cell">解析结果。</param>
        public static string StateOf(DriverConfigurationCell cell)
        {
            return cell != null && cell.IsConfigured ? Configured : NotConfigured;
        }

        /// <summary>读本机配置里某个 driver 的某一格；缺失、类型不对、空串统一给空串。</summary>
        private static string ReadLocalValue(LocalBridgeSettings settings, string driverName, string fieldName)
        {
            if (settings == null
                || !settings.TryGetDriverConfiguration(driverName ?? "", out var configuration)
                || configuration.ValueKind != System.Text.Json.JsonValueKind.Object
                || !configuration.TryGetProperty(fieldName ?? "", out var value))
            {
                return "";
            }

            return value.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => value.GetString() ?? "",
                System.Text.Json.JsonValueKind.Number => value.GetRawText(),
                System.Text.Json.JsonValueKind.True => "true",
                System.Text.Json.JsonValueKind.False => "false",
                _ => ""
            };
        }
    }
}
