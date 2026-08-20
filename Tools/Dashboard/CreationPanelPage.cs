namespace Template.Toolkit.Dashboard
{
    /// <summary>
    /// 创作管线面板页面：总览 / 任务 / 需求池 / 门禁 / 引擎 / 资产 / 设计池 / 供给对账八页装在一份自包含 HTML 里，
    /// 零外部依赖、零 CDN。每页都是「拉一次 /api/panel/* 再渲染」，页面自己不存业务状态。
    /// </summary>
    public static class CreationPanelPage
    {
        /// <summary>面板的完整 HTML 文档。</summary>
        public static string Html { get; } = @"<!DOCTYPE html>
<html lang='zh-CN'>
<head>
<meta charset='utf-8'>
<meta name='viewport' content='width=device-width, initial-scale=1'>
<title>创作管线面板</title>
<style>
:root { color-scheme: dark; }
html, body { height: 100%; margin: 0; }
body {
    font-family: 'Microsoft YaHei', 'PingFang SC', sans-serif;
    background: #1e1e2e; color: #cdd6f4;
    display: flex; flex-direction: column;
}
header { padding: 12px 16px; background: #181825; border-bottom: 1px solid #313244; }
header h1 { margin: 0 0 10px; font-size: 17px; }
nav button {
    font: inherit; font-size: 13px; margin-right: 6px; padding: 6px 14px;
    background: #313244; color: #cdd6f4; border: 1px solid #45475a;
    border-radius: 5px; cursor: pointer;
}
nav button.当前 { background: #89b4fa; color: #11111b; border-color: #89b4fa; font-weight: bold; }
main { flex: 1; overflow: auto; padding: 16px; }
h2 { font-size: 15px; margin: 0 0 10px; color: #a6adc8; }
table { border-collapse: collapse; width: 100%; margin-bottom: 18px; font-size: 13px; }
th, td { border: 1px solid #313244; padding: 6px 10px; text-align: left; vertical-align: top; }
th { background: #181825; color: #a6adc8; font-weight: normal; }
td.空 { color: #6c7086; }
.卡片组 { display: flex; flex-wrap: wrap; gap: 12px; margin-bottom: 20px; }
.卡片 {
    min-width: 132px; padding: 12px 16px; background: #181825;
    border: 1px solid #313244; border-radius: 6px;
}
.卡片 .数 { font-size: 26px; font-weight: bold; }
.卡片 .名 { font-size: 12px; color: #a6adc8; margin-top: 4px; }
.绿 { color: #a6e3a1; } .红 { color: #f38ba8; } .灰 { color: #6c7086; }
#命令区 { border-top: 1px solid #313244; background: #181825; padding: 10px 16px; }
#命令行 {
    font: inherit; font-size: 13px; width: 60%; padding: 6px 10px;
    background: #1e1e2e; color: #cdd6f4; border: 1px solid #45475a; border-radius: 5px;
}
#命令区 button {
    font: inherit; font-size: 13px; padding: 6px 16px; margin-left: 8px;
    background: #89b4fa; color: #11111b; border: none; border-radius: 5px; cursor: pointer;
}
#命令提示 { font-size: 12px; color: #6c7086; margin-top: 6px; }
#命令输出 {
    margin: 8px 0 0; padding: 8px 10px; max-height: 200px; overflow: auto;
    background: #11111b; border-radius: 5px;
    font-family: 'Consolas', monospace; font-size: 12px; white-space: pre-wrap; word-break: break-all;
}
</style>
</head>
<body>
<header>
<h1>创作管线面板</h1>
<nav id='导航'></nav>
</header>
<main id='内容'>加载中…</main>
<div id='命令区'>
<input id='命令行' placeholder='pool.validate --PoolRoot Pools'>
<button id='执行'>执行</button>
<div id='命令提示'>只放行 task. / pool. / bridge. / engine. / conflict. / spec. 六族命令；其余一律拒绝。</div>
<pre id='命令输出'></pre>
</div>
<script>
var 页面表 = [
    { 键: '总览', 地址: '/api/panel/overview', 渲染: 渲染总览 },
    { 键: '任务', 地址: '/api/panel/tasks', 渲染: 渲染任务 },
    { 键: '需求池', 地址: '/api/panel/requirements', 渲染: 渲染需求池 },
    { 键: '门禁', 地址: '/api/panel/gates', 渲染: 渲染门禁 },
    { 键: '引擎', 地址: '/api/panel/engine', 渲染: 渲染引擎 },
    { 键: '资产', 地址: '/api/panel/assets', 渲染: 渲染资产 },
    { 键: '设计池', 地址: '/api/panel/designs', 渲染: 渲染设计池 },
    { 键: '供给对账', 地址: '/api/panel/provision', 渲染: 渲染供给对账 }
];
var 当前页 = 0;
var 内容区 = document.getElementById('内容');

function 转义(值) {
    if (值 === null || 值 === undefined || 值 === '') { return ''; }
    return String(值).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

function 单元格(值) {
    var 文本 = 转义(值);
    if (文本 === '') { return ""<td class='空'>—</td>""; }
    return '<td>' + 文本 + '</td>';
}

function 表格(标题, 列名, 行列表, 取值) {
    if (!行列表 || 行列表.length === 0) {
        return '<h2>' + 转义(标题) + '</h2><p class=""灰"">暂无数据。</p>';
    }
    var 文本 = '<h2>' + 转义(标题) + '（' + 行列表.length + ' 条）</h2><table><tr>';
    for (var i = 0; i < 列名.length; i++) { 文本 += '<th>' + 转义(列名[i]) + '</th>'; }
    文本 += '</tr>';
    for (var j = 0; j < 行列表.length; j++) {
        文本 += '<tr>';
        var 格子 = 取值(行列表[j]);
        for (var k = 0; k < 格子.length; k++) { 文本 += 单元格(格子[k]); }
        文本 += '</tr>';
    }
    return 文本 + '</table>';
}

function 卡片(数, 名, 样式) {
    return ""<div class='卡片'><div class='数 "" + (样式 || '') + ""'>"" + 转义(数) +
        ""</div><div class='名'>"" + 转义(名) + '</div></div>';
}

function 门禁样式(状态) {
    if (状态 === '绿') { return '绿'; }
    if (状态 === '红') { return '红'; }
    return '灰';
}

function 渲染总览(数据) {
    return ""<div class='卡片组'>"" +
        卡片(数据['进行中任务'], '进行中任务') +
        卡片(数据['停在关卡'], '停在关卡等人') +
        卡片(数据['待确认需求'], '待确认需求') +
        卡片(数据['队列长度'], '队列长度') +
        卡片(数据['门禁'], '门禁', 门禁样式(数据['门禁'])) +
        卡片(数据['已供给'] + ' / ' + 数据['下游数'], '下游已供给') +
        '</div><p class=""灰"">每个数字都是现读文件算出来的，面板自己不存状态；刷新即最新。</p>';
}

function 渲染任务(数据) {
    return 表格('进行中的任务', ['需求 id', '标题', '阶段', '子状态', '停在关卡', '当前工作项'], 数据,
        function (行) {
            return [行['需求id'], 行['标题'], 行['阶段'], 行['子状态'], 行['停在关卡'], 行['当前工作项']];
        });
}

function 渲染需求池(数据) {
    return 表格('需求池', ['id', '标题', '类型', '状态', '专项', '锁定'], 数据,
        function (行) {
            return [行['id'], 行['标题'], 行['类型'], 行['状态'], 行['专项'], 行['锁定'] ? '是' : '否'];
        });
}

function 渲染门禁(数据) {
    var 状态 = 数据['状态'];
    var 文本 = ""<div class='卡片组'>"" + 卡片(状态, '门禁总状态', 门禁样式(状态)) + '</div>';
    if (状态 === '未跑') {
        文本 += '<p class=""灰"">还没有门禁报告文件（' + 转义(数据['报告路径']) +
            '）。这里如实写「未跑」，不会把没有的东西说成绿。</p>';
    }
    return 文本 + 表格('逐道结果', ['名称', '结果', '问题数'], 数据['条目'],
        function (行) { return [行['名称'], 行['结果'], 行['问题数']]; });
}

function 渲染引擎(数据) {
    var 文本 = ""<div class='卡片组'>"" +
        卡片(数据['模式'], '引擎模式') +
        卡片(数据['确认人'].length, '确认人') +
        卡片(数据['队列'].length, '队列长度') +
        '</div>';
    文本 += '<h2>确认人白名单</h2><p>' +
        (数据['确认人'].length ? 转义(数据['确认人'].join('、')) : '<span class=""灰"">暂无——没人能把需求拨到「已确认」。</span>') +
        '</p>';
    文本 += 表格('执行队列（顺序即先进先出）', ['需求 id', '入队时间', '理由'], 数据['队列'],
        function (行) { return [行['需求id'], 行['入队时间'], 行['理由']]; });
    var 路由 = 数据['卡片路由'];
    var 路由行 = Object.keys(路由).map(function (键) { return { 卡片: 键, 职责: 路由[键] }; });
    文本 += 表格('卡片路由表', ['卡片类型', '默认职责'], 路由行,
        function (行) { return [行['卡片'], 行['职责']]; });
    return 文本;
}

function 渲染资产(数据) {
    if (!数据 || 数据.length === 0) {
        return '<p class=""灰"">（还没有资产请求）</p>';
    }
    return 表格('资产', ['资产 id', '需求', '类型', '落点', '规格', '变体(合格/请求)', '弃置', '预览'], 数据,
        function (行) {
            return [行['资产id'], 行['需求'], 行['类型'], 行['落点'], 行['规格'],
                行['合格变体'] + '/' + 行['请求变体'], 行['弃置'], 行['预览'] ? '是' : '否'];
        });
}

function 渲染设计池(数据) {
    if (!数据 || 数据.length === 0) {
        return '<p class=""灰"">（设计池是空的）</p>';
    }
    return 表格('设计池', ['分类', '名称', '标题', '版本', '时间', '可读'], 数据,
        function (行) {
            return [行['分类'], 行['名称'], 行['标题'], 行['版本'], 行['时间'], 行['可读'] ? '是' : '否'];
        });
}

function 对账样式(状态) {
    if (状态 === '一致') { return '绿'; }
    if (状态 === '失配') { return '红'; }
    return '灰';
}

function 渲染供给对账(数据) {
    if (!数据 || 数据.length === 0) {
        return '<p class=""灰"">（Bridges/ 下还没有 driver）</p>';
    }
    var 文本 = '<h2>供给对账（' + 数据.length + ' 个 driver）</h2><table><tr>' +
        '<th>driver</th><th>形态</th><th>端口</th><th>供给</th><th>对账</th>' +
        '<th>依赖清单</th><th>配方数</th><th>问题数</th></tr>';
    for (var i = 0; i < 数据.length; i++) {
        var 行 = 数据[i];
        // 对账状态按门禁页的样式上色：一致绿、失配红、未跑灰——「未跑」不染绿（没有的东西不说成绿）。
        文本 += '<tr>' +
            单元格(行['driver']) +
            单元格(行['形态']) +
            单元格(行['端口'].join('、')) +
            单元格(行['供给']) +
            '<td class=""' + 对账样式(行['对账']) + '"">' + 转义(行['对账']) + '</td>' +
            单元格(行['依赖清单'] ? '是' : '否') +
            单元格(行['配方数']) +
            单元格(行['问题数']) +
            '</tr>';
    }
    return 文本 + '</table>';
}

function 切换(序号) {
    当前页 = 序号;
    画导航();
    内容区.innerHTML = '加载中…';
    var 页 = 页面表[序号];
    fetch(页.地址).then(function (响应) {
        if (!响应.ok) { throw new Error('HTTP ' + 响应.status); }
        return 响应.json();
    }).then(function (数据) {
        内容区.innerHTML = 页.渲染(数据);
    }).catch(function (错误) {
        内容区.innerHTML = '<p class=""红"">这一页取数据失败：' + 转义(错误.message) +
            '。面板未配置仓库根时会是这个结果。</p>';
    });
}

function 画导航() {
    var 导航 = document.getElementById('导航');
    导航.innerHTML = '';
    页面表.forEach(function (页, 序号) {
        var 按钮 = document.createElement('button');
        按钮.textContent = 页.键;
        if (序号 === 当前页) { 按钮.className = '当前'; }
        按钮.onclick = function () { 切换(序号); };
        导航.appendChild(按钮);
    });
}

document.getElementById('执行').onclick = function () {
    var 输出区 = document.getElementById('命令输出');
    var 命令行 = document.getElementById('命令行').value;
    输出区.textContent = '执行中…';
    fetch('/cmd', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json; charset=utf-8' },
        body: JSON.stringify({ '命令行': 命令行 })
    }).then(function (响应) {
        return 响应.json();
    }).then(function (结果) {
        if (!结果['允许']) {
            输出区.textContent = '被拒绝：' + 结果['原因'];
            return;
        }
        输出区.textContent = '退出码 ' + 结果['退出码'] + '\n' + 结果['输出'];
        切换(当前页);
    }).catch(function (错误) {
        输出区.textContent = '请求失败：' + 错误.message;
    });
};

切换(0);
</script>
</body>
</html>";
    }
}
