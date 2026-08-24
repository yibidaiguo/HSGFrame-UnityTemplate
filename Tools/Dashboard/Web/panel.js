// 创作管线面板的全部前端逻辑。
// 三条约束：零外部依赖（不许出现 CDN、不许 import）；页面不存业务状态（每页都是拉一次接口再渲染）；
// 没有的东西不说成有——取数失败、字段缺失一律如实写原因，绝不用默认值糊过去。

// ── 图标：极简线性 SVG，按页取用。写死在这里是为了不引外部图标库。 ──
var 图标库 = {
    总览: 'M4 13h6V4H4v9zm0 7h6v-5H4v5zm10 0h6V11h-6v9zm0-16v5h6V4h-6z',
    需求: 'M5 4h11l3 3v13H5V4zm11 0v4h4M8 12h8M8 16h5',
    任务: 'M4 6h16M4 12h16M4 18h9',
    图: 'M6 4h5v4H6V4zm7 6h5v4h-5v-4zM6 16h5v4H6v-4zm5-8h2m0 8h2m-8-4v4m0-8V8',
    引擎: 'M12 3v3m0 12v3M3 12h3m12 0h3M5.6 5.6l2.1 2.1m8.6 8.6l2.1 2.1m0-12.8l-2.1 2.1M7.7 16.3l-2.1 2.1M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8z',
    资产: 'M12 3l9 5-9 5-9-5 9-5zm9 9l-9 5-9-5m18 4l-9 5-9-5',
    设计: 'M12 3a9 9 0 1 0 0 18c1.1 0 2-.9 2-2 0-.5-.2-1-.6-1.4-.3-.4-.4-.8-.4-1.1 0-.8.7-1.5 1.5-1.5H16a5 5 0 0 0 5-5c0-3.9-4-7-9-7zM7.5 11a1 1 0 1 0 0-2 1 1 0 0 0 0 2zm4-3a1 1 0 1 0 0-2 1 1 0 0 0 0 2zm5 1a1 1 0 1 0 0-2 1 1 0 0 0 0 2z',
    门禁: 'M12 3l8 3v6c0 4.6-3.2 8.3-8 9-4.8-.7-8-4.4-8-9V6l8-3zm-3 9l2.2 2.2L15.5 10',
    审查: 'M12 5c-5 0-9 4.5-9 7s4 7 9 7 9-4.5 9-7-4-7-9-7zm0 4a3 3 0 1 1 0 6 3 3 0 0 1 0-6z',
    冲突: 'M12 3l9 16H3l9-16zm0 6v5m0 3v.5',
    放行: 'M4 6h16v4H4V6zm0 8h16v4H4v-4zm3-6v0m0 8v0',
    规范: 'M6 3h9l4 4v14H6V3zm9 0v4h4M9 12h7M9 16h7M9 8h3',
    晋升: 'M12 20V6m0 0l-5 5m5-5l5 5M5 3h14',
    提案: 'M9 4h9v16H6V7l3-3zm0 0v3H6m4 6h6m-6 4h4',
    供给: 'M3 8h13v9H3V8zm13 3h3l2 3v3h-5v-6zM7 20a1.5 1.5 0 1 0 0-3 1.5 1.5 0 0 0 0 3zm10 0a1.5 1.5 0 1 0 0-3 1.5 1.5 0 0 0 0 3z',
    下游: 'M5 4h6v6H5V4zm8 10h6v6h-6v-6zM8 10v4a2 2 0 0 0 2 2h3',
    桥接: 'M12 3l8 4v10l-8 4-8-4V7l8-4zm-8 4l8 4m0 0l8-4m-8 4v10',
    进度: 'M4 19h16M6 19V9m5 10V5m5 14v-7',
    路由: 'M4 7h5l3 5 3-5h5M4 17h5l3-5m3 5h5M6 5v4m12 6v4'
};

// 页表里写了一个图标库里没有的名字时，取这个问号形状顶上。
// 从前这里直接把 undefined 拼进 `d=`，浏览器每重画一次导航就报一条
// 「Expected moveto path command」——一屏几十条，把真正的报错埋掉了，
// 而按钮上那块图标是空的，人只当是没画好。**兜底要看得见**：
// 问号形状摆在那儿，一眼知道是页表写错了名字，不是漏画。
var 缺图形状 = 'M9 9a3 3 0 1 1 4 2.8c-.7.3-1 .9-1 1.6V14m0 3v.5';

function 取图形状(名) {
    return 图标库[名] || 缺图形状;
}

// ── 页表：每页的名字、接口、渲染函数、一句话职责说明。 ──
// 「说明」不是装饰：它回答「这一页是干嘛的、数字从哪来」，
// 面板十七页里有一半的名字外人看不懂，光靠导航按钮猜不出来。
var 页面表 = [
    {
        键: '总览', 别名: 'overview', 组: '纵览', 图: '总览', 地址: '/api/panel/overview',
        说明: '一屏看完管线现状：在跑什么、卡在哪、门禁红没红、下游供给到什么程度。卡片可以点，点进去就是那一页。',
        渲染: 渲染总览, 自备取数: true
    },
    {
        键: '进度', 别名: 'progress', 组: '纵览', 图: '进度', 地址: '/api/panel/progress',
        说明: '项目进度在仓库与飞书之间同步后的样子。工程侧那几格现算，策划端那几格读回流账——' +
            '面板自己不与飞书说话，要拉新的去跑 sync.progress。每一格都标了以哪侧为准。',
        渲染: 渲染进度
    },
    {
        键: '需求池', 别名: 'requirements', 组: '需求与调度', 图: '需求', 地址: '/api/panel/requirements',
        说明: '池子里全部需求的现状。状态分布条按 Pools/ 下的状态字段现算，不含任何缓存。',
        渲染: 渲染需求池
    },
    {
        键: '任务', 别名: 'tasks', 组: '需求与调度', 图: '任务', 地址: '/api/panel/tasks',
        说明: '正在推进的需求：走到哪个阶段、卡在哪道关卡、当前工作项是哪个。停在关卡等人的行会标出来。',
        渲染: 渲染任务
    },
    {
        键: '任务图', 别名: 'dag', 组: '需求与调度', 图: '图', 地址: '/api/panel/dag',
        说明: '单个需求的工作项依赖图。按依赖深度分层画；成环的工作项单独挑出来标红——环不画进层里，因为它没有深度。',
        渲染: 渲染任务图, 自备取数: true
    },
    {
        键: '详情', 别名: 'detail', 组: '需求与调度', 图: '任务', 地址: '/api/panel/taskdetail',
        说明: '单个需求的全部现状：阶段轴、验收标准、任务状态与工作项清单。从需求池或任务页点 id 跳过来。',
        渲染: 渲染详情, 自备取数: true
    },
    {
        键: '引擎', 别名: 'engine', 组: '需求与调度', 图: '引擎', 地址: '/api/panel/engine',
        说明: '调度引擎的配置与队列：谁能把需求拨到「已确认」、队列里排着谁、各类卡片默认归谁。',
        渲染: 渲染引擎
    },
    {
        键: '资产', 别名: 'assets', 组: '资产与设计', 图: '资产', 地址: '/api/panel/assets',
        说明: '资产请求与变体收敛情况。离风格分数按预览图主色与定稿色板的距离算，点「算」才现算——只报告，不动资产。',
        渲染: 渲染资产
    },
    {
        键: '设计池', 别名: 'designs', 组: '资产与设计', 图: '设计', 地址: '/api/panel/designs',
        说明: '定稿的色板与参考图，以及设计决策的时间线。参考图只列路径不加载——面板不做图片代理。',
        渲染: 渲染设计池
    },
    {
        键: '门禁', 别名: 'gates', 组: '质量与放行', 图: '门禁', 地址: '/api/panel/gates',
        说明: '最近一次门禁报告的逐道结果。没有报告文件就如实写「未跑」，不会把没有的东西说成绿。',
        渲染: 渲染门禁
    },
    {
        键: '审查', 别名: 'review', 组: '质量与放行', 图: '审查', 地址: '/api/panel/review',
        说明: '终审队列。等待时长按状态文件最后修改时间算，不是进关卡时间——等得最久的那条会被顶到最上面并标红。',
        渲染: 渲染审查
    },
    {
        键: '冲突', 别名: 'conflicts', 组: '质量与放行', 图: '冲突', 地址: '/api/panel/conflicts',
        说明: '新旧决策撞车的记录。未决的可以在这里裁决；已裁决的不给按钮——裁决结果不许覆盖。',
        渲染: 渲染冲突
    },
    {
        键: '放行流水', 别名: 'releases', 组: '质量与放行', 图: '放行', 地址: '/api/panel/releases',
        说明: '每一次放行的账。抽查是事后的：未抽查不是错，是还没查；发现问题的整行标红并带回滚提交。',
        渲染: 渲染放行流水
    },
    {
        键: '规范', 别名: 'specifications', 组: '治理与规范', 图: '规范', 地址: '/api/panel/specifications',
        说明: '基线、项目、业务三层规范文件的清单与规则条数。读不出来的文件标红并给原因，不静默跳过。',
        渲染: 渲染规范
    },
    {
        键: '晋升', 别名: 'promotions', 组: '治理与规范', 图: '晋升', 地址: '/api/panel/promotions',
        说明: '同类问题攒够条数后自动浮出来的晋升候选：这些是「该写进规范」的信号，不是待办。',
        渲染: 渲染晋升
    },
    {
        键: '提案待批', 别名: 'proposals', 组: '治理与规范', 图: '提案', 地址: '/api/panel/proposals',
        说明: '晋升提案的账本。待批的可批准或拒绝，已批准的可落地；已拒绝与已落地是终态，不给按钮。',
        渲染: 渲染提案待批
    },
    {
        键: '供给对账', 别名: 'provision', 组: '下游设施', 图: '供给', 地址: '/api/panel/provision',
        说明: '我们推给每个下游 driver 的产物对不对得上账。「未跑」不染绿——没对过账和对上了是两回事。',
        渲染: 渲染供给对账
    },
    {
        键: '下游', 别名: 'bridges', 组: '下游设施', 图: '下游', 地址: '/api/panel/bridges',
        说明: '换一台执行机时照这一页逐项填绿即完成。字段只报「配没配」，值一律不显示——密钥的值永不读取。',
        渲染: 渲染下游
    },
    {
        键: '桥接包', 别名: 'packages', 组: '下游设施', 图: '桥接', 地址: '/api/panel/packages',
        说明: '每个编辑器与每个下游要装什么、装没装、还差什么。「未验」是本机还没探过，不是没有——它既不染绿也不染红。',
        渲染: 渲染桥接包
    },
    {
        键: '路由', 别名: 'routes', 组: '下游设施', 图: '路由', 地址: '/api/panel/routes',
        说明: '每个域挂着哪几个下游、首选是谁、挂了换不换人。候选顺序就是优先级，改完点保存直接写进 downstream.json。',
        渲染: 渲染路由
    }
];

// 侧栏分组顺序：跟页表里的「组」对齐，顺序写死在这里而不是按出现顺序推，
// 免得往页表中间插一页就把分组顺序拧了。
var 分组顺序 = ['纵览', '需求与调度', '资产与设计', '质量与放行', '治理与规范', '下游设施'];

// 命令白名单的放行族。这一行的数组由 CreationPanelPage 装配时从 PanelCommandWhitelist 填进来——
// 真相只有 C# 那一份，前端照抄一份是为了「跑不了的按钮不给」，不是另立一套判定。
var 放行族 = /*白名单占位*/[];

var 当前页 = 0;
var 本页数据 = null;
var 本页取数时刻 = 0;
var 自刷句柄 = null;
var 时间句柄 = null;
var 命令历史 = [];
var 历史位 = -1;
var 徽章表 = {};
var 内容区 = document.getElementById('内容');

// ── 基础工具 ──

function 转义(值) {
    if (值 === null || 值 === undefined || 值 === '') { return ''; }
    return String(值)
        .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
        // 引号写成 \u 转义而不是字面量：这段脚本要过「每行引号成对」那道健全性检查，
        // 一个裸引号会让整行看着像没闭合。顺手把单引号也转掉，属性用单引号包裹的地方才安全。
        .replace(/\u0022/g, '&quot;').replace(/\u0027/g, '&#39;');
}

// 拼进 onclick 属性里的字符串实参。属性外层用双引号包，实参用单引号，
// 两层引号各管各的，谁都不用再转义——把它做成函数，是为了让每一行的引号都自己成对。
function 引(值) {
    return "'" + 值 + "'";
}

function 取(键) {
    try { return window.localStorage.getItem('面板.' + 键); } catch (错误) { return null; }
}

function 存(键, 值) {
    try { window.localStorage.setItem('面板.' + 键, 值); } catch (错误) { return; }
}

function 数字(值) {
    var 数 = Number(值);
    return isFinite(数) ? 数 : 0;
}

function 元素(标识) {
    return document.getElementById(标识);
}

function 吐司(文字, 成) {
    var 盒 = 元素('吐司');
    var 条 = document.createElement('div');
    条.className = 成 ? '成' : '败';
    条.textContent = 文字;
    盒.appendChild(条);
    window.setTimeout(function () { 盒.removeChild(条); }, 4200);
}

// ── 展示组件 ──

function 空态(题, 说) {
    return '<div class="空态">' +
        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6">' +
        '<rect x="3" y="5" width="18" height="15" rx="2"/><path d="M3 10h18M8 15h8"/></svg>' +
        '<div class="题">' + 转义(题) + '</div>' +
        '<div class="说">' + 转义(说) + '</div></div>';
}

function 错态(题, 说) {
    return '<div class="错态"><div class="题">' + 转义(题) + '</div>' +
        '<div class="说">' + 转义(说) + '</div></div>';
}

// 提示态：给「没出错，但缺了点什么才能往下走」的情形用。
// 门禁没跑过、操作人没填——这些拿红色说等于把没出的错说成出了错。
function 提示态(题, 说) {
    return '<div class="提示态"><div class="题">' + 转义(题) + '</div>' +
        '<div class="说">' + 转义(说) + '</div></div>';
}

function 态(文字, 色, 提示) {
    var 提 = 提示 ? ' title="' + 转义(提示) + '"' : '';
    return '<span class="态 ' + (色 || '灰') + '"' + 提 + '>' + 转义(文字) + '</span>';
}

function 指标(数, 名, 选项) {
    var 项 = 选项 || {};
    var 类 = '指标' + (项.边 ? ' ' + 项.边 + '边' : '') + (项.去处 ? ' 可点' : '');
    var 属性 = 项.去处 ? ' onclick="去(\'' + 项.去处 + '\')" title="点开 ' + 转义(项.去处) + ' 页"' : '';
    var 文本 = '<div class="' + 类 + '"' + 属性 + '>' +
        '<div class="头"><div class="名">' + 转义(名) + '</div>' + (项.角 || '') + '</div>' +
        '<div class="数' + (项.色 ? ' ' + 项.色 : '') + '">' + 转义(数) + '</div>';
    if (项.脚) { 文本 += '<div class="脚">' + 转义(项.脚) + '</div>'; }
    if (项.条) { 文本 += 项.条; }
    return 文本 + '</div>';
}

// 进度条：占比按 分子/分母 算，分母为 0 时画空槽而不是除零。
function 进度(分子, 分母, 色) {
    var 比 = 数字(分母) > 0 ? Math.min(100, 数字(分子) / 数字(分母) * 100) : 0;
    return '<div class="条"><i style="width:' + 比.toFixed(1) + '%;background:var(--' + (色 || '主') + ');"></i></div>';
}

// 堆叠条 + 图例：段是 { 名, 数, 色 } 的数组，零值段不画色块但仍进图例（免得图例跟着数据忽隐忽现）。
function 堆条(段们) {
    var 总 = 0;
    var i;
    for (i = 0; i < 段们.length; i++) { 总 += 数字(段们[i].数); }
    if (总 <= 0) { return '<div class="堆条"></div>'; }
    var 条 = '<div class="堆条">';
    for (i = 0; i < 段们.length; i++) {
        var 宽 = 数字(段们[i].数) / 总 * 100;
        if (宽 <= 0) { continue; }
        var 提示 = 转义(段们[i].名 + '：' + 段们[i].数);
        var 样式 = 'width:' + 宽.toFixed(2) + '%;background:var(--' + 段们[i].色 + ');';
        条 += '<i title="' + 提示 + '" style="' + 样式 + '"></i>';
    }
    条 += '</div><div class="图例">';
    for (i = 0; i < 段们.length; i++) {
        条 += '<span><b style="background:var(--' + 段们[i].色 + ')"></b>' +
            转义(段们[i].名) + ' <em>' + 转义(段们[i].数) + '</em></span>';
    }
    return 条 + '</div>';
}

// 环形进度：纯 SVG，靠 stroke-dasharray 画弧，中间写分数。
function 环(分子, 分母, 说明) {
    var 比 = 数字(分母) > 0 ? 数字(分子) / 数字(分母) : 0;
    var 周长 = 2 * Math.PI * 34;
    var 色 = 比 >= 1 ? '绿' : (比 > 0 ? '主' : '线亮');
    return '<div style="display:flex;align-items:center;gap:14px;">' +
        '<svg width="84" height="84" viewBox="0 0 84 84">' +
        '<circle cx="42" cy="42" r="34" fill="none" stroke="var(--线)" stroke-width="8"/>' +
        '<circle cx="42" cy="42" r="34" fill="none" stroke="var(--' + 色 + ')" stroke-width="8" ' +
        'stroke-linecap="round" stroke-dasharray="' + (周长 * 比).toFixed(1) + ' ' + 周长.toFixed(1) + '" ' +
        'transform="rotate(-90 42 42)"/>' +
        '<text x="42" y="46" text-anchor="middle" fill="var(--字)" font-size="17" font-weight="700">' +
        转义(分子) + '/' + 转义(分母) + '</text></svg>' +
        '<div class="小字 次" style="max-width:210px;">' + 转义(说明) + '</div></div>';
}

function 单元格(值, 类) {
    var 文本 = 转义(值);
    if (文本 === '') { return '<td class="空">—</td>'; }
    var 类名 = 类 ? ' class="' + 类 + '"' : '';
    if (文本.length > 46) {
        return '<td' + 类名 + '><span class="截" title="' + 文本 + '">' + 文本 + '</span></td>';
    }
    return '<td' + 类名 + '>' + 文本 + '</td>';
}

// 表格：列名支持字符串或 { 名, 数值 } —— 标了「数值」的列按数字排序，其余按文本。
// 行选项由取值函数返回的第二个位置给：取值返回数组就是纯格子，返回 { 格, 类 } 可以带整行样式。
function 表格(标题, 列名, 行列表, 取值, 选项) {
    var 项 = 选项 || {};
    if (!行列表 || 行列表.length === 0) {
        return (标题 ? '<div class="板题">' + 转义(标题) + '</div>' : '') +
            空态(项.空题 || '这一页现在没有数据', 项.空说 || '不是出错——文件读到了，里面就是空的。');
    }
    var 文本 = '<div class="表壳"><table><thead><tr>';
    var i;
    for (i = 0; i < 列名.length; i++) {
        var 列 = typeof 列名[i] === 'string' ? { 名: 列名[i] } : 列名[i];
        var 数值标 = 列.数值 ? '1' : '';
        文本 += '<th class="可排" data-列="' + i + '" data-数值="' + 数值标 + '" onclick="排序(this)">';
        文本 += 转义(列.名) + '<span class="箭">▲</span></th>';
    }
    文本 += '</tr></thead><tbody>';
    for (i = 0; i < 行列表.length; i++) {
        var 结果 = 取值(行列表[i]);
        var 格子 = 结果 && 结果.格 ? 结果.格 : 结果;
        var 行类 = 结果 && 结果.类 ? 结果.类 : '';
        文本 += '<tr' + (行类 ? ' class="' + 行类 + '"' : '') + '>';
        for (var k = 0; k < 格子.length; k++) {
            文本 += 格子[k] && 格子[k].原样 ? 格子[k].原样 : 单元格(格子[k]);
        }
        文本 += '</tr>';
    }
    文本 += '</tbody></table>';
    if (项.脚) { 文本 += '<div class="表脚">' + 转义(项.脚) + '</div>'; }
    return 文本 + '</div>';
}

// 原样格子：给需要塞按钮、徽章的列用，绕开单元格的转义与截断。
function 原样(内部, 类) {
    return { 原样: '<td' + (类 ? ' class="' + 类 + '"' : '') + '>' + 内部 + '</td>' };
}

// 点表头排序：直接对已渲染的 tr 排，不重新取数——这一页的数据就在眼前，没必要再跑一趟文件。
function 排序(表头) {
    var 表 = 表头.closest('table');
    var 列号 = Number(表头.getAttribute('data-列'));
    var 按数值 = 表头.getAttribute('data-数值') === '1';
    var 降 = 表头.classList.contains('升');
    var 同辈 = 表.querySelectorAll('thead th');
    for (var i = 0; i < 同辈.length; i++) {
        同辈[i].classList.remove('升', '降');
        var 别的箭 = 同辈[i].querySelector('.箭');
        if (别的箭) { 别的箭.textContent = '▲'; }
    }
    表头.classList.add(降 ? '降' : '升');
    var 本箭 = 表头.querySelector('.箭');
    if (本箭) { 本箭.textContent = 降 ? '▼' : '▲'; }
    var 体 = 表.querySelector('tbody');
    var 行们 = Array.prototype.slice.call(体.querySelectorAll('tr'));
    行们.sort(function (甲, 乙) {
        var 甲值 = (甲.children[列号] ? 甲.children[列号].textContent : '').trim();
        var 乙值 = (乙.children[列号] ? 乙.children[列号].textContent : '').trim();
        var 比;
        if (按数值) {
            比 = (parseFloat(甲值.replace(/[^0-9.\-]/g, '')) || 0) - (parseFloat(乙值.replace(/[^0-9.\-]/g, '')) || 0);
        } else {
            比 = 甲值.localeCompare(乙值, 'zh-CN');
        }
        return 降 ? -比 : 比;
    });
    for (var j = 0; j < 行们.length; j++) { 体.appendChild(行们[j]); }
}

// ── 导航与路由 ──

// 角色工作台：按职责过滤侧栏页签。分的是视图不是权限（决策 18 钉死 localhost）——
// 「全部」永远可切回全景；这张表只决定「先看见什么」。
var 工作台表 = {
    '全部': null,
    // 「进度」四个工作台都有：它回答的是「这件事走到哪了」，
    // 而这个问题策划、美术、程序、管理都要问——跟「总览」同一类，不属于谁。
    '策划': ['总览', '进度', '需求池', '详情', '任务', '任务图', '引擎', '冲突'],
    '美术': ['总览', '进度', '资产', '设计池', '需求池', '详情', '下游', '桥接包'],
    '程序': ['总览', '进度', '任务', '详情', '任务图', '门禁', '审查', '放行流水', '供给对账', '下游', '桥接包'],
    '管理': ['总览', '进度', '审查', '冲突', '放行流水', '规范', '晋升', '提案待批', '引擎']
};

function 当前工作台() {
    var 名 = 取('工作台') || '全部';
    return 工作台表.hasOwnProperty(名) ? 名 : '全部';
}

function 换工作台(名) {
    存('工作台', 名);
    var 许 = 工作台表[当前工作台()];
    if (许 && 许.indexOf(页面表[当前页].键) < 0) {
        切换(0);
        return;
    }
    画导航();
}

function 画导航() {
    var 导航 = 元素('导航');
    var 许 = 工作台表[当前工作台()];
    var 文本 = '';
    for (var g = 0; g < 分组顺序.length; g++) {
        var 组名 = 分组顺序[g];
        var 本组 = [];
        for (var i = 0; i < 页面表.length; i++) {
            if (页面表[i].组 !== 组名) { continue; }
            // 当前页即使不在工作台清单里也保留，切工作台不打断正在看的页。
            if (许 && 许.indexOf(页面表[i].键) < 0 && i !== 当前页) { continue; }
            本组.push(i);
        }
        if (本组.length === 0) { continue; }
        文本 += '<div class="组名">' + 转义(组名) + '</div>';
        for (var j = 0; j < 本组.length; j++) {
            var 号 = 本组[j];
            var 页 = 页面表[号];
            var 徽 = 徽章表[页.键];
            文本 += '<button class="页项' + (号 === 当前页 ? ' 当前' : '') + '" onclick="切换(' + 号 + ')" ' +
                'title="' + 转义(页.说明) + '">' +
                '<svg class="图" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" ' +
                'stroke-linecap="round" stroke-linejoin="round"><path d="' + 取图形状(页.图) + '"/></svg>' +
                '<span class="文">' + 转义(页.键) + '</span>' +
                (徽 ? '<span class="徽 ' + (徽.色 || '') + '">' + 转义(徽.文) + '</span>' : '') +
                '</button>';
        }
    }
    导航.innerHTML = 文本;
}

// 徽章：每页拉完数据后把「这一页有多少条 / 红不红」写回侧栏，
// 让导航本身就是一层信息，而不是十七个一样的按钮。
function 记徽章(键, 文, 色) {
    if (文 === null || 文 === undefined || 文 === '') {
        delete 徽章表[键];
    } else {
        徽章表[键] = { 文: 文, 色: 色 || '' };
    }
    画导航();
}

function 去(键) {
    for (var i = 0; i < 页面表.length; i++) {
        if (页面表[i].键 === 键) { 切换(i); return; }
    }
}

function 读地址页号() {
    var 记号 = window.location.hash.replace(/^#\/?/, '');
    if (!记号) { return 0; }
    for (var i = 0; i < 页面表.length; i++) {
        if (页面表[i].别名 === 记号) { return i; }
    }
    return 0;
}

function 切换(序号, 不改地址) {
    当前页 = 序号;
    var 页 = 页面表[序号];
    if (!不改地址) { window.location.hash = '#/' + 页.别名; }
    元素('面包屑').textContent = 页.组 + ' › ' + 页.键;
    元素('页标题').textContent = 页.键;
    元素('页说明').textContent = 页.说明;
    元素('搜索框').value = '';
    画导航();
    刷新();
}

function 上一页() { 切换((当前页 - 1 + 页面表.length) % 页面表.length); }

function 下一页() { 切换((当前页 + 1) % 页面表.length); }

// ── 取数与渲染 ──

function 进度开(开) {
    元素('进度条').className = 开 ? '走' : '';
    内容区.className = 开 ? '载入中' : '';
}

// 取数失败要分清是哪一种：接口回 503 带「错误」字段是「面板没配仓库根」，
// 网络层失败是服务没起来，其它 HTTP 码是接口本身出错。三种给三种文案，
// 别像从前那样一律写「面板未配置仓库根时会是这个结果」——那句话在另外两种情形里是假的。
function 拉(地址) {
    return fetch(地址).then(function (响应) {
        return 响应.json().then(function (数据) {
            if (!响应.ok) {
                var 说 = 数据 && 数据['错误'] ? 数据['错误'] : ('HTTP ' + 响应.status);
                var 错 = new Error(说);
                错.分类 = 响应.status === 503 ? '未配置' : '接口';
                throw 错;
            }
            return 数据;
        }, function () {
            var 错 = new Error('HTTP ' + 响应.status + '：返回的不是 JSON');
            错.分类 = '接口';
            throw 错;
        });
    }, function (原因) {
        var 错 = new Error(原因 && 原因.message ? 原因.message : '连不上看板服务');
        错.分类 = '网络';
        throw 错;
    });
}

function 报错(错误) {
    if (错误.分类 === '未配置') {
        内容区.innerHTML = 错态('面板没配仓库根，这一页读不了文件',
            错误.message + '。起看板时带上 --repository-root <仓库根>，或者在仓库里起它——它靠往上找 .git 认路。');
    } else if (错误.分类 === '网络') {
        内容区.innerHTML = 错态('连不上看板服务', 错误.message + '。看板进程可能已经退了，重开一个再刷新这一页。');
    } else {
        内容区.innerHTML = 错态('这一页的接口出错了', 错误.message);
    }
    元素('页计数').textContent = '';
}

function 刷新() {
    var 页 = 页面表[当前页];
    进度开(true);
    if (页.自备取数) {
        var 完 = function () { 进度开(false); 记时刻(); };
        页.渲染(null, 完);
        return;
    }
    拉(页.地址).then(function (数据) {
        本页数据 = 数据;
        内容区.innerHTML = 页.渲染(数据);
        进度开(false);
        记时刻();
        应用过滤();
    }).catch(function (错误) {
        进度开(false);
        报错(错误);
        记时刻();
    });
}

function 记时刻() {
    本页取数时刻 = new Date().getTime();
    画时刻();
}

function 画时刻() {
    if (!本页取数时刻) { 元素('数据时间').textContent = '—'; return; }
    var 秒 = Math.round((new Date().getTime() - 本页取数时刻) / 1000);
    var 文本;
    if (秒 < 3) { 文本 = '刚刚读的'; }
    else if (秒 < 60) { 文本 = 秒 + ' 秒前读的'; }
    else if (秒 < 3600) { 文本 = Math.floor(秒 / 60) + ' 分钟前读的'; }
    else { 文本 = Math.floor(秒 / 3600) + ' 小时前读的'; }
    元素('数据时间').textContent = '数据 ' + 文本;
}

// 过滤：只藏行不重新取数。命中数写回页头，让人知道自己正看着一个被过滤的视图——
// 这比静悄悄少几行要诚实。
function 应用过滤() {
    var 词 = 元素('搜索框').value.trim().toLowerCase();
    var 行们 = 内容区.querySelectorAll('tbody tr');
    var 卡们 = 内容区.querySelectorAll('.可滤');
    var 总 = 行们.length + 卡们.length;
    var 中 = 0;
    var i;
    for (i = 0; i < 行们.length; i++) {
        var 行中 = !词 || 行们[i].textContent.toLowerCase().indexOf(词) >= 0;
        行们[i].style.display = 行中 ? '' : 'none';
        if (行中) { 中++; }
    }
    // 卡片式的页（下游、设计池定稿）也要能过滤——只过滤表格的话，
    // 这两页的搜索框看着能用、按下去没反应，那比没有搜索框更糟。
    for (i = 0; i < 卡们.length; i++) {
        var 卡中 = !词 || 卡们[i].textContent.toLowerCase().indexOf(词) >= 0;
        卡们[i].style.display = 卡中 ? '' : 'none';
        if (卡中) { 中++; }
    }
    var 计数 = 元素('页计数');
    if (总 === 0) { 计数.textContent = ''; return; }
    var 单位 = 行们.length > 0 ? ' 行' : ' 张卡';
    计数.textContent = 词 ? ('过滤中：' + 中 + ' / ' + 总 + 单位) : (总 + 单位);
}

// ── 总览 ──
// 总览自己并发拉五个接口：光有 overview 那六个数字，看完还是不知道该去哪一页。
// 任何一个接口挂了都不影响其余四块——挂的那块单独写原因，不拖累整页。

function 渲染总览(忽略, 完成) {
    var 桶 = { 概: null, 需: null, 门: null, 下: null, 冲: null, 提: null, 放: null };
    var 待 = 0;
    var 记 = function (键) {
        return function (数据) { 桶[键] = { 成: true, 值: 数据 }; 收(); };
    };
    var 记败 = function (键) {
        return function (错误) { 桶[键] = { 成: false, 因: 错误.message }; 收(); };
    };
    var 收 = function () {
        待--;
        if (待 > 0) { return; }
        内容区.innerHTML = 拼总览(桶);
        算总览徽章(桶);
        if (完成) { 完成(); }
    };
    var 组 = [
        ['概', '/api/panel/overview'], ['需', '/api/panel/requirements'], ['门', '/api/panel/gates'],
        ['下', '/api/panel/bridges'], ['冲', '/api/panel/conflicts'], ['提', '/api/panel/proposals'],
        ['放', '/api/panel/releases']
    ];
    待 = 组.length;
    for (var i = 0; i < 组.length; i++) {
        拉(组[i][1]).then(记(组[i][0]), 记败(组[i][0]));
    }
}

function 拼总览(桶) {
    var 文本 = '';
    if (!桶.概.成) {
        文本 += 错态('总览的六个数字没读成', 桶.概.因);
    } else {
        var 概 = 桶.概.值;
        var 门色 = 概['门禁'] === '绿' ? '绿' : (概['门禁'] === '红' ? '红' : '灰');
        文本 += '<div class="格 自适应" style="margin-bottom:16px;">' +
            指标(概['进行中任务'], '进行中任务', { 边: '蓝', 去处: '任务', 脚: '走在管线上的需求' }) +
            指标(概['停在关卡'], '停在关卡等人', {
                边: 数字(概['停在关卡']) > 0 ? '黄' : '', 去处: '审查',
                色: 数字(概['停在关卡']) > 0 ? '黄' : '', 脚: '有人不点头就不动'
            }) +
            指标(概['待确认需求'], '待确认需求', { 去处: '需求池', 脚: '等确认人拨到已确认' }) +
            指标(概['队列长度'], '执行队列', { 去处: '引擎', 脚: '先进先出' }) +
            指标(概['门禁'], '门禁总状态', { 边: 门色, 色: 门色, 去处: '门禁', 脚: '取自最近一次门禁报告' }) +
            指标(概['已供给'] + ' / ' + 概['下游数'], '下游已供给', {
                边: 数字(概['已供给']) >= 数字(概['下游数']) ? '绿' : '', 去处: '供给对账',
                脚: '推给下游的产物对上账的数', 条: 进度(概['已供给'], 概['下游数'], '绿')
            }) +
            '</div>';
    }

    文本 += '<div class="格 宽卡">';
    文本 += 总览待办块(桶);
    文本 += 总览需求块(桶);
    文本 += 总览门禁块(桶);
    文本 += 总览下游块(桶);
    文本 += '</div>';
    文本 += '<p class="弱 小字">每个数字都是现读文件算出来的，面板自己不存状态；刷新即最新。' +
        '这一页并发拉了七个接口，某一块读不出来只会影响那一块。</p>';
    return 文本;
}

// 待办块：把「现在轮到人做什么」四类事凑在一起，每条可点。
// 这是整个面板里唯一一处按「该谁动手」而不是按数据来源组织的视图。
function 总览待办块(桶) {
    var 条目 = [];
    if (桶.冲.成) {
        var 未决 = 0;
        var 冲 = 桶.冲.值 || [];
        for (var i = 0; i < 冲.length; i++) { if (冲[i]['未决']) { 未决++; } }
        条目.push({ 名: '冲突待裁决', 数: 未决, 去: '冲突', 色: 未决 > 0 ? '红' : '绿' });
    }
    if (桶.提.成 && 桶.提.值 && 桶.提.值['读成']) {
        条目.push({
            名: '提案待批', 数: 数字(桶.提.值['待批数']), 去: '提案待批',
            色: 数字(桶.提.值['待批数']) > 0 ? '黄' : '绿'
        });
    }
    if (桶.放.成 && 桶.放.值 && 桶.放.值['读成']) {
        条目.push({
            名: '放行未抽查', 数: 数字(桶.放.值['未抽查数']), 去: '放行流水',
            色: 数字(桶.放.值['未抽查数']) > 0 ? '黄' : '绿'
        });
        条目.push({
            名: '抽查发现问题', 数: 数字(桶.放.值['问题数']), 去: '放行流水',
            色: 数字(桶.放.值['问题数']) > 0 ? '红' : '绿'
        });
    }
    var 板 = '<div class="板"><div class="板题">轮到人动手的<span class="右">点一条跳过去</span></div>';
    if (条目.length === 0) {
        return 板 + '<p class="弱 小字">冲突、提案、放行三个接口都没读成，这块给不出结论。</p></div>';
    }
    var 合计 = 0;
    for (var n = 0; n < 条目.length; n++) { 合计 += 数字(条目[n].数); }
    if (合计 === 0) {
        板 += '<p class="绿 小字" style="margin:0 0 6px;">现在没有等人动手的事——下面四项都是 0。</p>';
    }
    for (var j = 0; j < 条目.length; j++) {
        var 条 = 条目[j];
        var 样式 = 'display:flex;align-items:center;gap:10px;padding:7px 2px;cursor:pointer;';
        样式 += 'border-bottom:1px solid var(--线);';
        板 += '<div onclick="去(' + 引(条.去) + ')" style="' + 样式 + '">';
        板 += '<span style="flex:1;">' + 转义(条.名) + '</span>' + 态(条.数 + ' 件', 条.色) + '</div>';
    }
    return 板 + '</div>';
}

function 总览需求块(桶) {
    var 板 = '<div class="板"><div class="板题">需求池按状态分布<span class="右">现读 Pools/</span></div>';
    if (!桶.需.成) { return 板 + '<p class="红 小字">' + 转义(桶.需.因) + '</p></div>'; }
    var 行们 = 桶.需.值 || [];
    if (行们.length === 0) { return 板 + '<p class="弱 小字">需求池是空的。</p></div>'; }
    var 计 = {};
    for (var i = 0; i < 行们.length; i++) {
        var 状态 = 行们[i]['状态'] || '（无状态）';
        计[状态] = (计[状态] || 0) + 1;
    }
    var 色轮 = ['主', '绿', '黄', '紫', '青', '橙', '红'];
    var 段 = [];
    var 键们 = Object.keys(计).sort();
    for (var j = 0; j < 键们.length; j++) {
        段.push({ 名: 键们[j], 数: 计[键们[j]], 色: 色轮[j % 色轮.length] });
    }
    return 板 + 堆条(段) + '<p class="弱 小字" style="margin-bottom:0;">共 ' + 行们.length + ' 条需求。</p></div>';
}

function 总览门禁块(桶) {
    var 板 = '<div class="板"><div class="板题">门禁逐道<span class="右">悬停看名字</span></div>';
    if (!桶.门.成) { return 板 + '<p class="红 小字">' + 转义(桶.门.因) + '</p></div>'; }
    var 门 = 桶.门.值;
    var 条目 = (门 && 门['条目']) || [];
    if (门['状态'] === '未跑' || 条目.length === 0) {
        return 板 + '<p class="弱 小字">还没有门禁报告文件。这里如实写「未跑」——没跑过的不染绿。</p></div>';
    }
    var 阵 = '<div class="方阵">';
    var 绿数 = 0;
    for (var i = 0; i < 条目.length; i++) {
        var 绿 = 条目[i]['结果'] === '绿' || 条目[i]['结果'] === '通过';
        var 红 = 条目[i]['结果'] === '红' || 条目[i]['结果'] === '不过';
        if (绿) { 绿数++; }
        var 提示 = 转义(条目[i]['名称'] + '：' + 条目[i]['结果'] + '（问题 ' + 条目[i]['问题数'] + '）');
        var 色类 = 绿 ? '绿' : (红 ? '红' : '');
        阵 += '<div class="方 ' + 色类 + '" title="' + 提示 + '">' + (绿 ? '✓' : (红 ? '✕' : '·')) + '</div>';
    }
    阵 += '</div>';
    return 板 + 阵 + '<p class="弱 小字" style="margin:9px 0 0;">' + 绿数 + ' / ' + 条目.length +
        ' 道绿。总状态 ' + 转义(门['状态']) + '。</p></div>';
}

function 总览下游块(桶) {
    var 板 = '<div class="板"><div class="板题">下游供给<span class="右">换机器照这个填绿</span></div>';
    if (!桶.下.成) { return 板 + '<p class="红 小字">' + 转义(桶.下.因) + '</p></div>'; }
    var 行们 = 桶.下.值 || [];
    if (行们.length === 0) { return 板 + '<p class="弱 小字">Bridges/ 下还没有 driver。</p></div>'; }
    var 齐 = 0;
    for (var i = 0; i < 行们.length; i++) {
        if (行们[i]['读失败']) { continue; }
        var 字段们 = 行们[i]['字段'] || [];
        var 缺 = false;
        for (var j = 0; j < 字段们.length; j++) {
            if (字段们[j]['必填'] && 字段们[j]['状态'] !== '已配') { 缺 = true; }
        }
        if (!缺) { 齐++; }
    }
    return 板 + 环(齐, 行们.length, '必填字段全配齐的 driver 数。缺一项就不算齐——这一页的语义是「配没配」，不是「配了什么」。') +
        '</div>';
}

function 算总览徽章(桶) {
    if (桶.概.成) {
        var 概 = 桶.概.值;
        记徽章('任务', 概['进行中任务'], '');
        记徽章('门禁', 概['门禁'], 概['门禁'] === '绿' ? '绿' : (概['门禁'] === '红' ? '红' : ''));
        记徽章('引擎', 概['队列长度'], '');
        记徽章('供给对账', 概['已供给'] + '/' + 概['下游数'], '');
    }
    if (桶.需.成) { 记徽章('需求池', (桶.需.值 || []).length, ''); }
    if (桶.冲.成) {
        var 未决 = 0;
        var 冲 = 桶.冲.值 || [];
        for (var i = 0; i < 冲.length; i++) { if (冲[i]['未决']) { 未决++; } }
        记徽章('冲突', 未决, 未决 > 0 ? '红' : '');
    }
    if (桶.提.成 && 桶.提.值 && 桶.提.值['读成']) {
        var 待批 = 数字(桶.提.值['待批数']);
        记徽章('提案待批', 待批, 待批 > 0 ? '黄' : '');
    }
    if (桶.放.成 && 桶.放.值 && 桶.放.值['读成']) {
        var 问题 = 数字(桶.放.值['问题数']);
        记徽章('放行流水', 桶.放.值['总数'], 问题 > 0 ? '红' : '');
    }
}

// ── 需求与调度四页 ──

// 需求池的泳道顺序 = 需求状态机的合法状态顺序（Pools/Schema/Baseline/requirement.schema.json）。
var 泳道状态 = ['草稿', '已确认', '进行中', '待验收', '已完成', '已作废'];

function 渲染需求池(数据) {
    记徽章('需求池', (数据 || []).length, '');
    var 建单 = 建需求表单();
    if (!数据 || 数据.length === 0) {
        return 建单 + 空态('需求池是空的', '点上面的「建需求」直接建一条，或从飞书需求编辑端拉一次。');
    }
    var 计 = {};
    for (var i = 0; i < 数据.length; i++) {
        var 状态 = 数据[i]['状态'] || '（无状态）';
        计[状态] = (计[状态] || 0) + 1;
    }
    var 色轮 = ['主', '绿', '黄', '紫', '青', '橙', '红'];
    var 段 = [];
    var 键们 = Object.keys(计).sort();
    for (var j = 0; j < 键们.length; j++) {
        段.push({ 名: 键们[j], 数: 计[键们[j]], 色: 色轮[j % 色轮.length] });
    }
    var 看板视图 = 取('需求池.视图') !== '表格';
    var 切换钮 = '<span class="右"><button class="钮 细" onclick="切需求池视图()">' +
        (看板视图 ? '换表格' : '换看板') + '</button></span>';
    var 文本 = 建单 + '<div class="板"><div class="板题">状态分布' + 切换钮 +
        '<span class="右" style="margin-right:10px;">共 ' + 数据.length + ' 条</span></div>' + 堆条(段) + '</div>';
    return 文本 + (看板视图 ? 需求看板(数据) : 需求表格(数据));
}

// 泳道看板：一条需求一张卡，列 = 状态机的合法状态。卡片可过滤、点 id 进详情。
function 需求看板(数据) {
    var 按状态 = {};
    var i;
    for (i = 0; i < 数据.length; i++) {
        var 状态 = 数据[i]['状态'] || '（无状态）';
        (按状态[状态] = 按状态[状态] || []).push(数据[i]);
    }
    var 列们 = 泳道状态.slice();
    var 键们 = Object.keys(按状态);
    for (i = 0; i < 键们.length; i++) {
        if (列们.indexOf(键们[i]) < 0) { 列们.push(键们[i]); }
    }
    var 文本 = '<div class="泳道排">';
    for (i = 0; i < 列们.length; i++) {
        var 卡们 = 按状态[列们[i]] || [];
        文本 += '<div class="泳道"><div class="泳题">' + 转义(列们[i]) +
            '<span class="右">' + 卡们.length + '</span></div>';
        for (var k = 0; k < 卡们.length; k++) {
            var 卡 = 卡们[k];
            文本 += '<div class="泳卡 可滤" onclick="看详情(\'' + 转义(卡['id']) + '\')" title="点开详情">' +
                '<div class="泳卡头"><span class="等宽">' + 转义(卡['id']) + '</span>' +
                (卡['锁定'] ? 态('已锁', '黄') : '') + '</div>' +
                '<div class="泳卡题">' + 转义(卡['标题'] || '（无标题）') + '</div>' +
                '<div class="泳卡脚">' + 态(卡['类型'] || '—', '灰') +
                (卡['专项'] ? ' ' + 态(卡['专项'], '紫') : '') + '</div></div>';
        }
        if (卡们.length === 0) { 文本 += '<div class="泳空">—</div>'; }
        文本 += '</div>';
    }
    return 文本 + '</div>';
}

function 需求表格(数据) {
    return 表格('', [
        'id', '标题', '类型', '状态', '专项', '锁定'
    ], 数据, function (行) {
        return {
            格: [
                原样('<a href="#/detail" onclick="看详情(\'' + 转义(行['id']) + '\')" ' +
                    'style="color:var(--主);text-decoration:none;">' + 转义(行['id']) + '</a>'),
                行['标题'], 行['类型'],
                原样(态(行['状态'] || '—', 状态色(行['状态']))),
                行['专项'],
                原样(行['锁定'] ? 态('已锁', '黄') : '<span class="弱">否</span>')
            ]
        };
    });
}

function 切需求池视图() {
    存('需求池.视图', 取('需求池.视图') !== '表格' ? '表格' : '看板');
    刷新();
}

// ── 建需求表单 ──
// 提交走 pool.draft：落一份收件箱信封并立刻跑一轮入站，拒收理由原样回显。
// 分类型附加字段跟着 schema 的分类型必填走：系统→目标/玩法，修改→现状/期望，缺陷→复现步骤/期望/实际。

function 建需求表单() {
    return '<div class="板"><div class="板题">建需求' +
        '<span class="右"><button class="钮 细" onclick="开合建单()" id="建单钮">展开</button></span></div>' +
        '<div id="建单体" style="display:none;">' +
        '<div class="表单排"><label>标题 *<input class="输" id="建_标题"></label>' +
        '<label>类型 *<select class="输" id="建_类型" onchange="建单切类型()">' +
        '<option value="系统">系统</option><option value="修改">修改</option><option value="缺陷">缺陷</option>' +
        '</select></label><label>专项<input class="输" id="建_专项" placeholder="EP-0001，可空"></label></div>' +
        '<div class="表单排"><label style="flex:1;">描述<textarea class="输" id="建_描述" rows="2"></textarea></label></div>' +
        '<div class="表单排" data-类型组="系统"><label>目标 *<input class="输" id="建_目标"></label>' +
        '<label>玩法 *<input class="输" id="建_玩法"></label></div>' +
        '<div class="表单排" data-类型组="修改" style="display:none;"><label>现状 *<input class="输" id="建_现状"></label>' +
        '<label>期望 *<input class="输" id="建_期望"></label></div>' +
        '<div class="表单排" data-类型组="缺陷" style="display:none;"><label>复现步骤 *<input class="输" id="建_复现步骤"></label>' +
        '<label>期望 *<input class="输" id="建_缺陷期望"></label><label>实际 *<input class="输" id="建_实际"></label></div>' +
        '<div class="表单排"><label style="flex:1;">验收标准 *（一行一条，每条要能勾）' +
        '<textarea class="输" id="建_验收" rows="3" placeholder="例：主界面能打开背包\n例：背包里能看到全部道具"></textarea></label></div>' +
        '<div class="表单排"><button class="钮 主" onclick="提交建需求()">建进池子</button>' +
        '<span class="弱 小字">提交人用页头的操作人；建完立刻跑一轮入站，拒收理由会显示在这里。</span></div>' +
        '<div id="建单结果" class="小字"></div>' +
        '</div></div>';
}

function 开合建单() {
    var 体 = 元素('建单体');
    var 开 = 体.style.display === 'none';
    体.style.display = 开 ? '' : 'none';
    元素('建单钮').textContent = 开 ? '收起' : '展开';
}

function 建单切类型() {
    var 类型 = 元素('建_类型').value;
    var 组们 = 内容区.querySelectorAll('[data-类型组]');
    for (var i = 0; i < 组们.length; i++) {
        组们[i].style.display = 组们[i].getAttribute('data-类型组') === 类型 ? '' : 'none';
    }
}

function 提交建需求() {
    var 值 = function (id) { var 节 = document.getElementById(id); return 节 ? 节.value.trim() : ''; };
    var 标题 = 值('建_标题');
    var 类型 = 值('建_类型');
    var 验收 = 值('建_验收');
    if (!标题 || !验收) {
        吐司('标题与验收标准是必填的', false);
        return;
    }
    var 参数 = {
        Title: 标题, Kind: 类型, Description: 值('建_描述'),
        AcceptanceCriteria: 验收, Epic: 值('建_专项'),
        Submitter: (元素('操作人').value || '').trim()
    };
    if (类型 === '系统') { 参数.Goal = 值('建_目标'); 参数.Gameplay = 值('建_玩法'); }
    if (类型 === '修改') { 参数.Current = 值('建_现状'); 参数.Expected = 值('建_期望'); }
    if (类型 === '缺陷') { 参数.ReproSteps = 值('建_复现步骤'); 参数.Expected = 值('建_缺陷期望'); 参数.Actual = 值('建_实际'); }
    var 结果区 = 元素('建单结果');
    结果区.textContent = '提交中…';
    发命令JSON('pool.draft', 参数, function (结果) {
        if (结果.成功) {
            结果区.textContent = '';
            吐司('建进池子了', true);
            刷新();
        } else {
            结果区.textContent = 结果.文本;
            吐司('没建成，看表单下面的原因', false);
        }
    });
}

// 发结构化命令：走 /cmd 的 JSON 参数通道（多行文本进不了命令行，只能走这条）。
function 发命令JSON(命令名, 参数, 回调) {
    fetch('/cmd', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json; charset=utf-8' },
        body: JSON.stringify({ '命令': 命令名, '参数': 参数 })
    }).then(function (响应) { return 响应.json(); }).then(function (结果) {
        if (!结果['允许']) {
            回调({ 成功: false, 文本: '被白名单拒绝：' + 结果['原因'] });
            return;
        }
        回调({ 成功: 结果['退出码'] === 0, 文本: 结果['输出'] || '' });
    }).catch(function (错误) {
        回调({ 成功: false, 文本: '请求失败：' + 错误.message });
    });
}

// ── 详情页 ──

function 看详情(需求id) {
    存('详情.需求id', 需求id);
    去('详情');
}

function 渲染详情(忽略, 完成) {
    var 需求id = 取('详情.需求id') || '';
    var 头 = '<div class="板"><div class="板题">选一个需求</div>' +
        '<div style="display:flex;gap:8px;align-items:center;">' +
        '<input class="输" id="详情id输入" placeholder="需求 id，例如 REQ-0042" style="width:230px;" value="' + 转义(需求id) + '">' +
        '<button class="钮 主" onclick="查详情()">看详情</button>' +
        '<span class="弱 小字">从需求池或任务页点 id 也能跳过来。</span></div></div>';
    if (!需求id) {
        内容区.innerHTML = 头 + 空态('先填一个需求 id', '详情一次只看一个需求：阶段轴、验收标准、任务状态与工作项。');
        完成();
        return;
    }
    拉('/api/panel/taskdetail?id=' + encodeURIComponent(需求id)).then(function (数据) {
        内容区.innerHTML = 头 + 详情正文(数据);
        完成();
        应用过滤();
    }).catch(function (错误) {
        内容区.innerHTML = 头 + 错态('这个需求的详情读不出来', 错误.message);
        完成();
    });
}

function 查详情() {
    var 值 = 元素('详情id输入').value.trim();
    if (!值) { 吐司('先填需求 id', false); return; }
    存('详情.需求id', 值);
    刷新();
}

function 详情正文(数据) {
    var 文本 = '';
    // 阶段轴：状态机顺序里高亮当前状态；已作废单独标红。
    var 状态 = 数据['状态'] || '';
    var 轴 = '<div class="板"><div class="板题">' + 转义(数据['id'] || '') +
        '<span class="右">' + (数据['锁定'] ? 态('已锁', '黄') : '') + '</span></div>' +
        '<div class="详题">' + 转义(数据['标题'] || '（无标题）') + '</div><div class="阶段轴">';
    var 主线 = 泳道状态.slice(0, 5);
    var 到过 = true;
    for (var i = 0; i < 主线.length; i++) {
        var 当前 = 主线[i] === 状态;
        if (当前) { 到过 = false; }
        轴 += '<span class="阶 ' + (当前 ? '今' : (到过 ? 'past' : '')) + '">' + 转义(主线[i]) + '</span>';
        if (i < 主线.length - 1) { 轴 += '<i class="阶线"></i>'; }
    }
    if (状态 === '已作废') { 轴 += ' ' + 态('已作废', '红'); }
    轴 += '</div><div class="小字 次">类型 ' + 转义(数据['类型'] || '—') +
        '　专项 ' + 转义(数据['专项'] || '—') + '　描述：' + 转义(数据['描述'] || '—') + '</div></div>';
    文本 += 轴;

    var 标准 = 数据['验收标准'] || [];
    var 单 = '<div class="板"><div class="板题">验收标准<span class="右">' + 标准.length + ' 条</span></div><ol class="验单">';
    for (var k = 0; k < 标准.length; k++) { 单 += '<li>' + 转义(标准[k]) + '</li>'; }
    单 += '</ol></div>';
    文本 += 标准.length > 0 ? 单 : '';

    if (数据['有任务']) {
        文本 += '<div class="格 自适应" style="margin-bottom:16px;">' +
            指标(数据['阶段'] || '—', '阶段', { 边: '蓝' }) +
            指标(数据['子状态'] || '—', '子状态') +
            指标(数据['停在关卡'] || '—', '停在关卡', { 边: 数据['停在关卡'] ? '黄' : '', 色: 数据['停在关卡'] ? '黄' : '' }) +
            指标(数据['当前工作项'] || '—', '当前工作项') +
            '</div>';
        var 项们 = 数据['工作项'] || [];
        文本 += 表格('工作项', ['名称', '状态'], 项们, function (行) {
            return { 格: [行['名称'], 原样(态(行['状态'] || '—', 状态色(行['状态'])))] };
        }, { 空题: '还没有工作项', 空说: '方案阶段过了才会拆出工作项。' });
        文本 += '<div class="小字 次" style="margin-top:8px;">' +
            '<a href="#/dag" onclick="看图(\'' + 转义(数据['id']) + '\')" style="color:var(--主);">看它的工作项依赖图 →</a></div>';
    } else {
        文本 += 提示态('还没进任务', '这个需求还没被引擎接走（_Tasks/ 下没有它的目录）。要先由确认人拨到「已确认」并进队列。');
    }
    return 文本;
}

// 状态染色：只认得出的几个染色，认不出的一律灰。
// 宁可少染也不猜——猜错颜色比不染更误导人。
function 状态色(状态) {
    if (!状态) { return '灰'; }
    if (状态.indexOf('完') >= 0 || 状态.indexOf('已确认') >= 0 || 状态.indexOf('放行') >= 0) { return '绿'; }
    if (状态.indexOf('待') >= 0 || 状态.indexOf('审') >= 0) { return '黄'; }
    if (状态.indexOf('废') >= 0 || 状态.indexOf('失败') >= 0 || 状态.indexOf('冲突') >= 0) { return '红'; }
    if (状态.indexOf('进行') >= 0 || 状态.indexOf('执行') >= 0) { return '蓝'; }
    return '灰';
}

function 渲染任务(数据) {
    记徽章('任务', (数据 || []).length, '');
    if (!数据 || 数据.length === 0) {
        return 空态('现在没有进行中的任务', '需求要先被确认人拨到「已确认」并进执行队列，才会出现在这一页。');
    }
    var 停 = 0;
    for (var i = 0; i < 数据.length; i++) { if (数据[i]['停在关卡']) { 停++; } }
    var 头 = '<div class="格 自适应" style="margin-bottom:16px;">' +
        指标(数据.length, '进行中', { 边: '蓝' }) +
        指标(停, '停在关卡等人', { 边: 停 > 0 ? '黄' : '', 色: 停 > 0 ? '黄' : '', 脚: '有人不点头就不动' }) +
        指标(数据.length - 停, '还在自己跑', { 脚: '不需要人介入' }) +
        '</div>';
    return 头 + 表格('', [
        '需求 id', '标题', '阶段', '子状态', '停在关卡', '当前工作项'
    ], 数据, function (行) {
        return {
            类: 行['停在关卡'] ? '重' : '',
            格: [
                原样('<a href="#/dag" onclick="看图(\'' + 转义(行['需求id']) + '\')" ' +
                    'style="color:var(--主);text-decoration:none;" title="看它的任务依赖图">' +
                    转义(行['需求id']) + '</a>'),
                行['标题'],
                原样(态(行['阶段'] || '—', 状态色(行['阶段']))),
                行['子状态'],
                原样(行['停在关卡'] ? 态(行['停在关卡'], '黄') : '<span class="弱">—</span>'),
                行['当前工作项']
            ]
        };
    }, { 脚: '标红的行是停在关卡等人的——那是现在唯一需要人动手的地方。' });
}

// 从任务页点需求 id 跳过来：先把 id 塞进任务图页的输入框，再切页。
function 看图(需求id) {
    存('任务图.需求id', 需求id);
    去('任务图');
}

function 渲染任务图(忽略, 完成) {
    var 需求id = 存过的需求id();
    var 输入 = '<input class="输" id="需求id输入" placeholder="需求 id，例如 REQ-0042" style="width:230px;"';
    输入 += ' value="' + 转义(需求id) + '">';
    var 头 = '<div class="板"><div class="板题">选一个需求</div>' +
        '<div style="display:flex;gap:8px;align-items:center;">' + 输入 +
        '<button class="钮 主" onclick="查任务图()">画图</button>' +
        '<span class="弱 小字">按依赖深度分层；成环的工作项挑出来单独标红。</span></div></div>';
    if (!需求id) {
        内容区.innerHTML = 头 + 空态('先填一个需求 id', '任务图一次只画一个需求的工作项依赖。从「任务」页点需求 id 也能跳过来。');
        绑需求框();
        if (完成) { 完成(); }
        return;
    }
    拉('/api/panel/dag?requirement=' + encodeURIComponent(需求id)).then(function (数据) {
        if (!数据 || 数据.length === 0) {
            内容区.innerHTML = 头 + 空态('这个需求还没有工作项',
                需求id + ' 在池子里没有拆出工作项；也可能这个 id 拼错了。');
        } else {
            内容区.innerHTML = 头 + 画依赖图(数据) + 任务图表格(数据);
        }
        绑需求框();
        应用过滤();
        if (完成) { 完成(); }
    }).catch(function (错误) {
        报错(错误);
        if (完成) { 完成(); }
    });
}

function 存过的需求id() {
    var 框 = 元素('需求id输入');
    if (框) { return 框.value.trim(); }
    return 取('任务图.需求id') || '';
}

function 绑需求框() {
    var 框 = 元素('需求id输入');
    if (!框) { return; }
    框.onkeydown = function (事件) {
        if (事件.key === 'Enter') { 查任务图(); }
    };
}

function 查任务图() {
    var 框 = 元素('需求id输入');
    存('任务图.需求id', 框 ? 框.value.trim() : '');
    刷新();
}

// 依赖图：按深度分层，一层一列，层内纵向排开。
// 环上的工作项（深度 -1）不进层——它没有深度这个属性，硬塞进第 0 层是编造。
function 画依赖图(行列表) {
    var 层 = {};
    var 环上 = [];
    var 位置 = {};
    var i;
    for (i = 0; i < 行列表.length; i++) {
        var 行 = 行列表[i];
        if (行['深度'] === -1) { 环上.push(行); continue; }
        var 深 = 数字(行['深度']);
        if (!层[深]) { 层[深] = []; }
        层[深].push(行);
    }
    var 层号们 = Object.keys(层).map(Number).sort(function (甲, 乙) { return 甲 - 乙; });
    var 列宽 = 210;
    var 行高 = 62;
    var 框宽 = 168;
    var 框高 = 40;
    var 最高 = 1;
    for (i = 0; i < 层号们.length; i++) { 最高 = Math.max(最高, 层[层号们[i]].length); }
    var 环行 = 环上.length > 0 ? 1 : 0;
    var 宽 = Math.max(360, 层号们.length * 列宽 + 40);
    var 高 = 最高 * 行高 + 44 + 环行 * (行高 + 30);

    var 连线 = '';
    var 节点 = '';
    for (i = 0; i < 层号们.length; i++) {
        var 本层 = 层[层号们[i]];
        var 左 = 20 + i * 列宽;
        节点 += '<text class="层标" x="' + (左 + 4) + '" y="16">第 ' + 层号们[i] + ' 层 · ' + 本层.length + ' 项</text>';
        for (var j = 0; j < 本层.length; j++) {
            var 顶 = 30 + j * 行高;
            位置[本层[j]['id']] = { x: 左, y: 顶, 宽: 框宽, 高: 框高 };
            节点 += 画节点(本层[j], 左, 顶, 框宽, 框高);
        }
    }
    if (环上.length > 0) {
        var 环顶 = 30 + 最高 * 行高 + 20;
        节点 += '<text class="层标" x="24" y="' + (环顶 - 8) + '" fill="var(--红)">成环，无深度可言 · ' +
            环上.length + ' 项</text>';
        for (i = 0; i < 环上.length; i++) {
            var 环左 = 20 + i * (框宽 + 24);
            位置[环上[i]['id']] = { x: 环左, y: 环顶, 宽: 框宽, 高: 框高 };
            节点 += 画节点(环上[i], 环左, 环顶, 框宽, 框高);
            宽 = Math.max(宽, 环左 + 框宽 + 30);
        }
    }
    for (i = 0; i < 行列表.length; i++) {
        var 依赖们 = 行列表[i]['依赖'] || [];
        var 到 = 位置[行列表[i]['id']];
        if (!到) { continue; }
        for (var k = 0; k < 依赖们.length; k++) {
            var 从 = 位置[依赖们[k]];
            if (!从) { continue; }
            var 甲x = 从.x + 从.宽;
            var 甲y = 从.y + 从.高 / 2;
            var 乙x = 到.x;
            var 乙y = 到.y + 到.高 / 2;
            var 中 = (甲x + 乙x) / 2;
            var 路径 = 'M' + 甲x + ' ' + 甲y + ' C' + 中 + ' ' + 甲y + ' ' + 中 + ' ' + 乙y +
                ' ' + 乙x + ' ' + 乙y;
            连线 += '<path class="连线" d="' + 路径 + '"/>';
        }
    }
    return '<div class="板"><div class="板题">依赖图<span class="右">箭头方向：被依赖的在左</span></div>' +
        '<div id="图区"><svg width="' + 宽 + '" height="' + 高 + '" viewBox="0 0 ' + 宽 + ' ' + 高 + '">' +
        连线 + 节点 + '</svg></div></div>';
}

function 画节点(行, 左, 顶, 宽, 高) {
    var 状态 = 行['状态'] || '';
    var 类 = 行['深度'] === -1 ? '环' : (状态.indexOf('完') >= 0 ? '完' : (状态.indexOf('进行') >= 0 ? '做' : ''));
    var 标题 = 行['标题'] || '';
    if (标题.length > 15) { 标题 = 标题.substring(0, 14) + '…'; }
    var 编号 = 行['id'] || '';
    if (编号.length > 20) { 编号 = 编号.substring(0, 19) + '…'; }
    return '<g><title>' + 转义(行['id'] + ' · ' + (行['标题'] || '') + ' · ' + 状态) + '</title>' +
        '<rect class="节点框 ' + 类 + '" x="' + 左 + '" y="' + 顶 + '" width="' + 宽 + '" height="' + 高 + '" rx="7"/>' +
        '<text class="节点标" x="' + (左 + 10) + '" y="' + (顶 + 17) + '">' + 转义(编号) + '</text>' +
        '<text class="节点副" x="' + (左 + 10) + '" y="' + (顶 + 31) + '">' + 转义(标题 || 状态) + '</text></g>';
}

function 任务图表格(数据) {
    return 表格('', ['深度', '工作项', '标题', '状态', '依赖'], 数据, function (行) {
        var 在环上 = 行['深度'] === -1;
        return {
            类: 在环上 ? '重' : '',
            格: [
                原样(在环上 ? 态('环', '红') : '<span class="等宽">' + 转义(行['深度']) + '</span>'),
                行['id'], 行['标题'],
                原样(态(行['状态'] || '—', 状态色(行['状态']))),
                (行['依赖'] || []).join('、')
            ]
        };
    });
}

function 渲染引擎(数据) {
    var 确认人 = 数据['确认人'] || [];
    var 队列 = 数据['队列'] || [];
    记徽章('引擎', 队列.length, '');
    var 文本 = '<div class="格 自适应" style="margin-bottom:16px;">' +
        指标(数据['模式'], '引擎模式', { 边: '蓝' }) +
        指标(确认人.length, '确认人', {
            边: 确认人.length === 0 ? '红' : '', 色: 确认人.length === 0 ? '红' : '',
            脚: 确认人.length === 0 ? '没人能拨到已确认' : '能把需求拨到已确认的人'
        }) +
        指标(队列.length, '队列长度', { 脚: '顺序即先进先出' }) +
        '</div>';
    文本 += '<div class="板"><div class="板题">确认人白名单</div>';
    if (确认人.length === 0) {
        文本 += '<p class="红 小字" style="margin:0;">白名单是空的——现在没人能把需求拨到「已确认」，整条管线卡在入口。</p>';
    } else {
        文本 += '<div style="display:flex;flex-wrap:wrap;gap:6px;">';
        for (var i = 0; i < 确认人.length; i++) { 文本 += 态(确认人[i], '蓝'); }
        文本 += '</div>';
    }
    文本 += '</div>';
    文本 += '<div class="板"><div class="板题">执行队列<span class="右">顺序即先进先出</span></div>' +
        表格('', ['需求 id', '入队时间', '理由'], 队列, function (行) {
            return [行['需求id'], 行['入队时间'], 行['理由']];
        }, { 空题: '队列是空的', 空说: '没有需求在等着被执行——不是出错。' }) + '</div>';
    var 路由 = 数据['卡片路由'] || {};
    var 路由行 = Object.keys(路由).map(function (键) { return { 卡片: 键, 职责: 路由[键] }; });
    文本 += '<div class="板"><div class="板题">卡片路由表<span class="右">哪类卡片默认归谁</span></div>' +
        表格('', ['卡片类型', '默认职责'], 路由行, function (行) {
            return [行['卡片'], 行['职责']];
        }, { 空题: '没有配路由', 空说: '卡片类型到职责的映射是空的。' }) + '</div>';
    return 文本;
}

// ── 资产与设计两页 ──

function 渲染资产(数据) {
    记徽章('资产', (数据 || []).length, '');
    if (!数据 || 数据.length === 0) {
        return 空态('还没有资产请求', '资产请求由需求拆解出来；这一页只显示已经提出请求的资产。');
    }
    var 合格 = 0;
    var 请求 = 0;
    var 有预览 = 0;
    for (var i = 0; i < 数据.length; i++) {
        合格 += 数字(数据[i]['合格变体']);
        请求 += 数字(数据[i]['请求变体']);
        if (数据[i]['预览']) { 有预览++; }
    }
    var 文本 = '<div class="格 自适应" style="margin-bottom:16px;">' +
        指标(数据.length, '资产请求', { 边: '蓝' }) +
        指标(合格 + ' / ' + 请求, '变体合格数', {
            脚: '合格变体 / 请求变体', 条: 进度(合格, 请求, 绿或黄(合格, 请求))
        }) +
        指标(有预览, '有预览图', { 脚: '没预览图就算不了离风格' }) +
        '</div>';
    文本 += '<p class="弱 小字">离风格按预览图主色与定稿色板的距离算，点「算」才现算一次——只报告，不自动处理资产。</p>';
    return 文本 + 表格('', [
        '资产 id', '需求', '类型', '落点', '规格',
        { 名: '变体', 数值: true }, '弃置', '预览', '离风格'
    ], 数据, function (行) {
        return {
            格: [
                行['资产id'], 行['需求'], 行['类型'], 行['落点'], 行['规格'],
                原样('<span class="等宽">' + 转义(行['合格变体'] + ' / ' + 行['请求变体']) + '</span>' +
                    进度(行['合格变体'], 行['请求变体'], 绿或黄(行['合格变体'], 行['请求变体']))),
                行['弃置'],
                原样(行['预览'] ? 态('有', '绿') : '<span class="弱">无</span>'),
                原样(离风格格子(行))
            ]
        };
    });
}

function 绿或黄(分子, 分母) {
    if (数字(分母) <= 0) { return '线亮'; }
    return 数字(分子) >= 数字(分母) ? '绿' : '黄';
}

// 离风格格子：没预览或没锚点的行直接写明原因、不给按钮，省一次注定失败的往返。
function 离风格格子(行) {
    if (!行['预览路径']) { return '<span class="弱">没有预览图</span>'; }
    if (!行['风格锚点定稿']) { return '<span class="弱">没有风格锚点</span>'; }
    var 属性 = 'data-需求="' + 转义(行['需求']) + '" data-资产="' + 转义(行['资产id']) + '"';
    return '<button class="钮 细" ' + 属性 + ' onclick="算离风格(this)">算一次</button>';
}

function 算离风格(按钮) {
    var 需求 = 按钮.getAttribute('data-需求');
    var 资产 = 按钮.getAttribute('data-资产');
    var 格子 = 按钮.parentNode;
    格子.innerHTML = '<span class="弱">算中…</span>';
    拉('/api/panel/deviation?requirement=' + encodeURIComponent(需求) + '&asset=' + encodeURIComponent(资产))
        .then(function (结果) {
            if (!结果['测成']) {
                格子.innerHTML = '<span class="弱" title="' + 转义(结果['原因']) + '">' + 转义(结果['原因']) + '</span>';
                return;
            }
            var 距离 = 结果['距离'];
            格子.innerHTML = 态(距离.toFixed(2), 离风格档(距离)) +
                '<div class="小字 弱" style="margin-top:3px;">主色 ' + 转义((结果['主色'] || []).join(' ')) + '</div>';
        }).catch(function (错误) {
            格子.innerHTML = '<span class="红" title="' + 转义(错误.message) + '">取数失败</span>';
        });
}

// 距离越大越离谱，分三档就够。
function 离风格档(距离) {
    if (距离 < 10) { return '绿'; }
    if (距离 < 25) { return '黄'; }
    return '红';
}

function 渲染设计池(数据) {
    记徽章('设计池', (数据 || []).length, '');
    if (!数据 || 数据.length === 0) {
        return 空态('设计池是空的', '定稿、汇总与设计记录都会落在这里；现在一份都没有。');
    }
    var 定稿们 = [];
    var 时间线 = [];
    var i;
    for (i = 0; i < 数据.length; i++) {
        if (数据[i]['分类'] === '定稿') { 定稿们.push(数据[i]); } else { 时间线.push(数据[i]); }
    }
    var 文本 = '<div class="板"><div class="板题">定稿<span class="右">' + 定稿们.length + ' 份</span></div>';
    if (定稿们.length === 0) {
        文本 += '<p class="弱 小字" style="margin:0;">还没有定稿——离风格算不了，因为没有锚点色板可比。</p>';
    } else {
        文本 += '<div class="格 自适应">';
        for (i = 0; i < 定稿们.length; i++) { 文本 += 定稿卡(定稿们[i]); }
        文本 += '</div>';
    }
    文本 += '</div>';
    文本 += '<div class="板"><div class="板题">时间线<span class="右">汇总与设计记录</span></div>' +
        表格('', ['时间', '分类', '文件名'], 时间线, function (行) {
            return {
                格: [
                    原样('<span class="等宽">' + 转义(行['时间']) + '</span>' +
                        (行['时间取自文件'] ? '<div class="小字 弱">文件时间，不是文档里写的时间</div>' : '')),
                    原样(态(行['分类'] || '—', '灰')),
                    行['名称']
                ]
            };
        }, { 空题: '还没有设计记录', 空说: '除定稿外，设计池里没有其它记录。' }) + '</div>';
    return 文本;
}

function 定稿卡(定稿) {
    var 版本 = 数字(定稿['定稿版本']) > 0 ? ' @v' + 定稿['定稿版本'] : '';
    var 卡 = '<div class="指标 可滤" style="gap:6px;">' +
        '<div class="名" style="font-size:13px;color:var(--字);font-weight:600;">' +
        转义(定稿['名称'] + 版本) + '</div>';
    var 色板 = 定稿['色板'] || [];
    if (色板.length > 0) {
        卡 += '<div>';
        for (var i = 0; i < 色板.length; i++) {
            var 色 = 转义(色板[i]);
            卡 += '<span class="色块" title="' + 色 + '" style="background-color:' + 色 + ';"></span>';
        }
        卡 += '</div><div class="小字 等宽 弱">' + 转义(色板.join(' ')) + '</div>';
    } else {
        卡 += '<div class="小字 弱">没有色板</div>';
    }
    var 参考图 = 定稿['参考图'] || [];
    if (参考图.length > 0) {
        卡 += '<div class="小字 弱" title="' + 转义(参考图.join('、')) + '">参考图 ' + 参考图.length +
            ' 张（只列路径，面板不做图片代理）</div>';
    }
    return 卡 + '</div>';
}

// ── 质量与放行四页 ──

function 渲染门禁(数据) {
    var 状态 = 数据['状态'];
    var 条目 = 数据['条目'] || [];
    记徽章('门禁', 状态, 状态 === '绿' ? '绿' : (状态 === '红' ? '红' : ''));
    var 绿数 = 0;
    var 红数 = 0;
    var 问题合计 = 0;
    for (var i = 0; i < 条目.length; i++) {
        // 「过没过」由服务端算好放在「通过」里，这一页不自己认词。
        // 从前这里认的是「绿 / 通过」，而报告里写的是「成功」——
        // 于是三十道全成功显示成「通过道次 0 / 30」，方格全灰，总状态却是绿。
        if (条目[i]['通过']) { 绿数++; } else { 红数++; }
        问题合计 += 数字(条目[i]['问题数']);
    }
    var 门色 = 状态 === '绿' ? '绿' : (状态 === '红' ? '红' : '灰');
    var 文本 = '<div class="格 自适应" style="margin-bottom:16px;">' +
        指标(状态, '门禁总状态', { 边: 门色, 色: 门色 }) +
        指标(绿数 + ' / ' + 条目.length, '通过道次', { 条: 进度(绿数, 条目.length, '绿') }) +
        指标(红数, '不过的道次', { 边: 红数 > 0 ? '红' : '', 色: 红数 > 0 ? '红' : '' }) +
        指标(问题合计, '问题合计', { 色: 问题合计 > 0 ? '红' : '' }) +
        '</div>';
    if (状态 === '未跑') {
        文本 += 提示态('还没有门禁报告',
            '要读的报告路径是 ' + (数据['报告路径'] || '（接口没给路径）') +
            '，这台机器还没跑过 gate.ps1（每跑一遍它就落一份逐道报告）。' +
            '跑一次 pwsh Tools/Gates/gate.ps1 再刷新这一页。');
    }
    if (条目.length > 0) {
        文本 += '<div class="板"><div class="板题">逐道一览<span class="右">悬停看名字与问题数</span></div>';
        文本 += '<div class="方阵">';
        for (var j = 0; j < 条目.length; j++) {
            var 绿 = !!条目[j]['通过'];
            var 红 = !绿;
            var 提示 = 转义(条目[j]['名称'] + '：' + 条目[j]['结果'] + '（问题 ' + 条目[j]['问题数'] + '）');
            var 色类 = 绿 ? '绿' : (红 ? '红' : '');
            文本 += '<div class="方 ' + 色类 + '" title="' + 提示 + '">' +
                (绿 ? '✓' : (红 ? '✕' : '·')) + '</div>';
        }
        文本 += '</div></div>';
    }
    return 文本 + 表格('', ['名称', '结果', { 名: '问题数', 数值: true }], 条目, function (行) {
        var 红行 = !行['通过'];
        return {
            类: 红行 ? '重' : '',
            格: [
                行['名称'],
                原样(态(行['结果'] || '—', 红行 ? '红' : '绿')),
                行['问题数']
            ]
        };
    }, { 空题: '报告里没有逐道结果', 空说: '门禁报告读到了，但里面一道都没有。' });
}

function 渲染审查(数据) {
    记徽章('审查', (数据 || []).length, (数据 || []).length > 0 ? '黄' : '');
    if (!数据 || 数据.length === 0) {
        return 空态('终审队列是空的', '没有需求在等终审——不是出错，是真没有。');
    }
    var 最早 = null;
    for (var i = 0; i < 数据.length; i++) {
        var 时间 = 数据[i]['最后修改时间'];
        if (!时间) { continue; }
        if (最早 === null || 时间 < 最早) { 最早 = 时间; }
    }
    var 头 = '<div class="格 自适应" style="margin-bottom:16px;">' +
        指标(数据.length, '排队待终审', { 边: '黄', 色: '黄' }) +
        '</div><p class="弱 小字">等待时长按状态文件最后修改时间算，不是进关卡时间。等得最久的那条标红。</p>';
    return 头 + 表格('', [
        '需求', '标题', '阶段·子状态', '关卡', '风险级', '等待'
    ], 数据, function (行) {
        var 阶段子状态 = (行['阶段'] || '') + (行['阶段'] && 行['子状态'] ? ' · ' : '') + (行['子状态'] || '');
        var 格 = [
            行['需求id'], 行['标题'],
            原样(转义(阶段子状态) || '<span class="弱">—</span>'),
            行['关卡待审'],
            原样(行['风险级'] ? 态(行['风险级'], 风险色(行['风险级'])) : '<span class="弱">—</span>'),
            原样('<span class="等宽">' + 转义(行['等待']) + '</span>' +
                (行['状态失败'] ? '<div class="红 小字">' + 转义(行['状态失败原因']) + '</div>' : ''))
        ];
        return { 类: (最早 !== null && 行['最后修改时间'] === 最早) ? '重' : '', 格: 格 };
    });
}

function 风险色(风险级) {
    if (!风险级) { return '灰'; }
    if (风险级.indexOf('高') >= 0) { return '红'; }
    if (风险级.indexOf('中') >= 0) { return '黄'; }
    return '绿';
}

function 渲染进度(数据) {
    if (!数据) { return 空态('拉不到进度', '接口没回东西。'); }
    var 列 = 数据['列'] || [];
    var 行们 = 数据['行'] || [];
    记徽章('进度', 行们.length, '');

    var 头 = '';
    if ((数据['表问题'] || '').length > 0) {
        头 += 提示态('权威侧表有问题，这一页只能显示工程侧算得出来的部分', 数据['表问题']);
    }

    var 全局 = 数据['全局'] || {};
    var 指标们 = '';
    var 键们 = Object.keys(全局).sort();
    for (var k = 0; k < 键们.length; k++) {
        指标们 += 指标(全局[键们[k]], 键们[k], {});
    }
    if (指标们) { 头 += '<div class="格 自适应" style="margin-bottom:16px;">' + 指标们 + '</div>'; }

    var 同步行 = [];
    同步行.push('上次回流：' + ((数据['上次回流'] || '').length > 0 ? 数据['上次回流'] : '还没同步过'));
    if ((数据['文档链接'] || '').length > 0) {
        同步行.push('进度文档：<a href="' + 转义(数据['文档链接']) + '" target="_blank" rel="noreferrer">下游那一份</a>');
    } else {
        同步行.push('进度文档：还没推上去（跑 sync.progress --PushDocument true --DryRun false）');
    }
    头 += '<div class="弱" style="margin-bottom:12px;">' + 同步行.join('　·　') + '</div>';

    // 两个按钮而不是一个：干跑一趟看清「这一轮会动什么」，看清了再真跑。
    // 真跑会写别人的飞书表，所以它带一次确认——那一步会改别人看得见的东西。
    头 += '<div style="margin-bottom:16px;">' +
        '<button class="钮 细" onclick="同步进度(true)">干跑一趟</button> ' +
        '<button class="钮 细 主" onclick="同步进度(false)">真同步（会写飞书任务表）</button>' +
        '</div>';

    if (行们.length === 0) {
        return 头 + 空态('池子里还没有需求', '有了需求这一页才有行。');
    }

    // 表头把权威侧写进列名：人看这一页最常问的就是「这一格我能不能改」。
    var 表头 = ['需求'];
    var 权威 = {};
    for (var c = 0; c < 列.length; c++) { 权威[列[c]] = ''; }
    for (var r = 0; r < 行们.length; r++) {
        var 格们 = 行们[r]['格'] || [];
        for (var g = 0; g < 格们.length; g++) { 权威[格们[g]['字段']] = 格们[g]['权威侧']; }
    }
    for (var c2 = 0; c2 < 列.length; c2++) {
        表头.push(列[c2] + '（' + (权威[列[c2]] || '?') + '）');
    }

    return 头 + 表格('', 表头, 行们, function (行) {
        var 格值 = ['<b>' + 转义(行['id']) + '</b>'];
        var 表 = {};
        var 格们 = 行['格'] || [];
        for (var i = 0; i < 格们.length; i++) { 表[格们[i]['字段']] = 格们[i]['值']; }
        for (var j = 0; j < 列.length; j++) {
            var 值 = 表[列[j]];
            格值.push(值 === undefined || 值 === '' ? '<span class="弱">—</span>' : 转义(值));
        }
        // 逐个包，不用 map(原样)：map 会把下标当成第二个参数塞进「类」，
        // 于是第 1 列往后每个 td 都会挂上一个 class="1" 这样的假类名。
        var 格子 = [];
        for (var n = 0; n < 格值.length; n++) { 格子.push(原样(格值[n])); }
        return { 类: '', 格: 格子 };
    }, { 脚: '标「工程」的格改了也没用，下一轮同步会照仓库的值盖回去；标「策划端」的格请在飞书任务表里改。' });
}

// 面板上的进度同步：干跑不确认，真跑确认一次。
// 命令走白名单里的 sync. 那一族，与终端跑的是同一条命令、同一份代码。
function 同步进度(干跑) {
    if (!干跑 && !window.confirm('这一趟会把工程侧那几格写进飞书任务表，并把人在飞书里改的那几格收回仓库。继续？')) {
        return;
    }
    发命令('sync.progress --RepositoryRoot . --PoolRoot Pools --Direction 双向 --DryRun ' +
        (干跑 ? 'true' : 'false') + ' --PushDocument ' + (干跑 ? 'false' : 'true'));
}

function 渲染冲突(数据) {
    var 未决数 = 0;
    var i;
    for (i = 0; i < (数据 || []).length; i++) { if (数据[i]['未决']) { 未决数++; } }
    记徽章('冲突', 未决数, 未决数 > 0 ? '红' : '');
    if (!数据 || 数据.length === 0) {
        return 空态('冲突列表是空的', '新旧决策没有撞车的记录。');
    }
    var 有操作人 = (元素('操作人').value || '').trim().length > 0;
    var 头 = '<div class="格 自适应" style="margin-bottom:16px;">' +
        指标(数据.length, '冲突记录', {}) +
        指标(未决数, '未裁决', { 边: 未决数 > 0 ? '红' : '绿', 色: 未决数 > 0 ? '红' : '绿' }) +
        '</div>';
    if (!有操作人 && 未决数 > 0) {
        头 += 提示态('裁决按钮是灰的，因为还没填操作人',
            '裁决要署名。在右上角「操作人」里填上名字（不能有空格，也不能以短横线开头），按钮就亮了。');
    }
    return 头 + 表格('', [
        '冲突', '旧', '新', '发现阶段', '状态', '选择', '裁决人', '时间', '操作'
    ], 数据, function (行) {
        var 操作;
        if (行['未决']) {
            操作 = 裁决钮(行['id'], '改新的', '主') + ' ' + 裁决钮(行['id'], '改旧的', '') + ' ' +
                裁决钮(行['id'], '强制推送', '危');
        } else {
            操作 = '<span class="弱">已裁决，不许覆盖</span>';
        }
        return {
            类: 行['未决'] ? '重' : '',
            格: [
                行['id'], 行['旧'], 行['新'], 行['发现阶段'],
                原样(态(行['状态'] || '—', 行['未决'] ? '红' : '绿')),
                行['选择'], 行['裁决人'], 行['时间'],
                原样(操作)
            ]
        };
    }, { 脚: '裁决只写一次，已裁决的行不给按钮——覆盖裁决结果这件事本身就该做不到。' });
}

function 裁决钮(冲突id, 选择, 样式) {
    var 有操作人 = (元素('操作人').value || '').trim().length > 0;
    var 属性 = 'data-冲突="' + 转义(冲突id) + '" data-选择="' + 转义(选择) + '"';
    var 禁 = 有操作人 ? '' : ' disabled';
    return '<button class="钮 细 裁决按钮 ' + 样式 + '" ' + 属性 + 禁 +
        ' onclick="裁决(this)">' + 转义(选择) + '</button>';
}

function 裁决(按钮) {
    var 冲突id = 按钮.getAttribute('data-冲突');
    var 选择 = 按钮.getAttribute('data-选择');
    var 操作人 = 校验操作人();
    if (!操作人) { return; }
    if (选择 === '强制推送' && !window.confirm('强制推送会把新的直接盖上去，不留回旋余地。确定对 ' + 冲突id + ' 这么做？')) {
        return;
    }
    发命令('conflict.resolve --PoolRoot Pools --ConflictIdentifier ' + 冲突id +
        ' --ResolverName ' + 操作人 + ' --Choice ' + 选择);
}

// 操作人姓名直接拼进 /cmd 命令行：空格或制表符会把它拆成多个参数，短横线开头会被当成参数键。
// 这两种名字无法安全表达，明确拒绝而不是静默写错。
function 校验操作人() {
    var 操作人 = (元素('操作人').value || '').trim();
    if (!操作人) {
        吐司('先在右上角填「操作人」——裁决要署名', false);
        元素('操作人').focus();
        return null;
    }
    if (操作人.indexOf(' ') >= 0 || 操作人.indexOf('\t') >= 0 || 操作人.charAt(0) === '-') {
        吐司('操作人姓名不能含空格，也不能以短横线开头', false);
        return null;
    }
    return 操作人;
}

function 渲染放行流水(数据) {
    if (!数据['读成']) {
        记徽章('放行流水', '!', '红');
        return 错态('放行流水没读成，这一页一个统计数字都不给',
            (数据['失败原因'] || '接口没给原因') + '。残缺的流水不能拿来下「零问题」的结论。');
    }
    var 行列表 = 数据['行'] || [];
    var 问题数 = 数字(数据['问题数']);
    记徽章('放行流水', 数据['总数'], 问题数 > 0 ? '红' : '');
    var 已抽查 = 数字(数据['总数']) - 数字(数据['未抽查数']);
    var 文本 = '<div class="格 自适应" style="margin-bottom:16px;">' +
        指标(数据['总数'], '放行总数', { 边: '蓝' }) +
        指标(已抽查 + ' / ' + 数据['总数'], '已抽查', {
            脚: '未抽查不是错，是还没查', 条: 进度(已抽查, 数据['总数'], 绿或黄(已抽查, 数据['总数']))
        }) +
        指标(数据['未抽查数'], '未抽查', { 色: 数字(数据['未抽查数']) > 0 ? '黄' : '' }) +
        指标(问题数, '抽查发现问题', { 边: 问题数 > 0 ? '红' : '绿', 色: 问题数 > 0 ? '红' : '绿' }) +
        '</div>';
    return 文本 + 表格('', [
        '流水 id', '需求', '风险级', '范围', '放行时间', '抽查状态', '合并提交', '抽查结论', '回滚提交', '操作'
    ], 行列表, function (行) {
        var 按钮 = 行['已抽查']
            ? '<button class="钮 细" disabled>已抽查</button>'
            : '<button class="钮 细" data-流水="' + 转义(行['id']) + '" onclick="抽查(this)">抽查</button>';
        return {
            类: 行['发现问题'] ? '重' : (行['已抽查'] ? '' : '淡'),
            格: [
                行['id'], 行['需求id'],
                原样(行['风险级'] ? 态(行['风险级'], 风险色(行['风险级'])) : '<span class="弱">—</span>'),
                行['范围'], 行['放行时间'],
                原样(态(行['抽查状态'] || '—', 行['发现问题'] ? '红' : (行['已抽查'] ? '绿' : '灰'))),
                原样('<span class="等宽">' + 转义(行['合并提交']) + '</span>'),
                行['抽查结论'],
                原样(行['回滚提交'] ? '<span class="等宽">' + 转义(行['回滚提交']) + '</span>' : '<span class="弱">—</span>'),
                原样(按钮)
            ]
        };
    }, { 脚: '标红的行是抽查发现了问题的；灰的是还没抽查——那不是错。' });
}

// 抽查改成行内两个按钮：结论只有两个合法值，弹窗让人手打字符串纯属考验记性。
function 抽查(按钮) {
    var 流水id = 按钮.getAttribute('data-流水');
    var 格 = 按钮.parentNode;
    格.innerHTML = '<button class="钮 细" onclick="抽查定(\'' + 转义(流水id) + '\',\'合格\')">合格</button> ' +
        '<button class="钮 细 危" onclick="抽查定(\'' + 转义(流水id) + '\',\'发现问题\')">发现问题</button>';
}

function 抽查定(流水id, 结论) {
    发命令JSON('task.spotcheck', {
        RepositoryRoot: '.', PoolRoot: 'Pools', LedgerIdentifier: 流水id, Conclusion: 结论
    }, function (结果) {
        吐司(结果.成功 ? ('抽查已记：' + 结论) : ('抽查没记上：' + 结果.文本.substring(0, 120)), 结果.成功);
        刷新();
    });
}

// ── 治理与规范三页 ──

function 渲染规范(数据) {
    记徽章('规范', (数据 || []).length, '');
    if (!数据 || 数据.length === 0) {
        return 空态('还没有规范文件', '基线、项目、业务三层都没有找到规范文件。');
    }
    var 层顺序 = ['基线', '项目', '业务'];
    var 坏 = 0;
    var i;
    for (i = 0; i < 数据.length; i++) { if (!数据[i]['可读']) { 坏++; } }
    var 文本 = '<div class="格 自适应" style="margin-bottom:16px;">';
    for (i = 0; i < 层顺序.length; i++) {
        var 本层数 = 0;
        var 本层规则 = 0;
        for (var j = 0; j < 数据.length; j++) {
            if (数据[j]['层'] !== 层顺序[i]) { continue; }
            本层数++;
            if (数字(数据[j]['规则数']) > 0) { 本层规则 += 数字(数据[j]['规则数']); }
        }
        文本 += 指标(本层数, 层顺序[i] + '层文件', { 脚: 本层规则 > 0 ? (本层规则 + ' 条规则') : '没数出规则条数' });
    }
    文本 += 指标(坏, '读不出来的文件', { 边: 坏 > 0 ? '红' : '绿', 色: 坏 > 0 ? '红' : '绿' });
    文本 += '</div>';

    for (i = 0; i < 层顺序.length; i++) {
        var 层 = 层顺序[i];
        var 本层 = [];
        for (var k = 0; k < 数据.length; k++) {
            if (数据[k]['层'] === 层) { 本层.push(数据[k]); }
        }
        文本 += '<div class="板"><div class="板题">' + 转义(层) + '<span class="右">' + 本层.length + ' 份</span></div>';
        if (本层.length === 0) {
            文本 += '<p class="弱 小字" style="margin:0;">这一层还没有规范文件。</p></div>';
            continue;
        }
        if (层 === '业务') {
            var 模块们 = [];
            for (var m = 0; m < 本层.length; m++) {
                if (模块们.indexOf(本层[m]['模块']) < 0) { 模块们.push(本层[m]['模块']); }
            }
            模块们.sort();
            for (var n = 0; n < 模块们.length; n++) {
                var 本模块 = [];
                for (var p = 0; p < 本层.length; p++) {
                    if (本层[p]['模块'] === 模块们[n]) { 本模块.push(本层[p]); }
                }
                文本 += '<div class="小字 次" style="margin:10px 0 6px;">模块 ' + 转义(模块们[n] || '（未标模块）') + '</div>';
                文本 += 规范表(本模块);
            }
        } else {
            文本 += 规范表(本层);
        }
        文本 += '</div>';
    }
    return 文本;
}

function 规范表(行列表) {
    return 表格('', ['文件名', { 名: '规则条数', 数值: true }, { 名: '字节数', 数值: true }, '相对路径', '读取'],
        行列表, function (行) {
            return {
                类: 行['可读'] ? '' : '重',
                格: [
                    行['文件名'],
                    原样(数字(行['规则数']) === -1 || 行['规则数'] === -1
                        ? '<span class="弱">—</span>'
                        : '<span class="等宽">' + 转义(行['规则数']) + '</span>'),
                    原样('<span class="等宽">' + 转义(行['字节数']) + '</span>'),
                    行['相对路径'],
                    原样(行['可读'] ? 态('可读', '绿') : ('<span class="红">' + 转义(行['失败原因']) + '</span>'))
                ]
            };
        });
}

function 渲染晋升(数据) {
    记徽章('晋升', (数据 || []).length, '');
    if (!数据 || 数据.length === 0) {
        return 空态('还没有达到阈值的晋升候选', '同一类问题攒够条数才会浮出来。现在一类都没攒够——这是好事。');
    }
    return '<p class="弱 小字">这些是「该写进规范」的信号，不是待办。要动手请去「提案待批」。</p>' +
        表格('', ['问题类别', { 名: '条数', 数值: true }, '可规则化性', '晋升去向', '模块', '原文举例'],
            数据, function (行) {
                return [
                    行['问题类别'], 行['条数'], 行['可规则化性'], 行['晋升去向'],
                    (行['模块'] || []).join('、'), (行['原文举例'] || []).join('；')
                ];
            });
}

function 渲染提案待批(数据) {
    if (!数据['读成']) {
        记徽章('提案待批', '!', '红');
        return 错态('晋升提案账本没读成', 数据['失败原因'] || '接口没给原因');
    }
    var 行列表 = 数据['行'] || [];
    var 待批数 = 数字(数据['待批数']);
    记徽章('提案待批', 待批数, 待批数 > 0 ? '黄' : '');
    var 有操作人 = (元素('操作人').value || '').trim().length > 0;
    var 文本 = '<div class="格 自适应" style="margin-bottom:16px;">' +
        指标(数据['总数'], '提案总数', { 边: '蓝' }) +
        指标(待批数, '待批', { 边: 待批数 > 0 ? '黄' : '绿', 色: 待批数 > 0 ? '黄' : '绿' }) +
        指标(数据['未关闭数'], '未关闭', {}) +
        '</div>';
    if (!有操作人 && 待批数 > 0) {
        文本 += 提示态('批准 / 拒绝按钮是灰的，因为还没填操作人',
            '批准与拒绝要署名。在右上角「操作人」里填名字（不能有空格，也不能以短横线开头）。落地不需要署名，始终可点。');
    }
    return 文本 + 表格('', [
        '提案 id', '类别', { 名: '同类条数', 数值: true }, '可规则化性', '去向', '模块', '状态', '提出时间', '裁决人', '原文引用', '操作'
    ], 行列表, function (行) {
        return {
            格: [
                行['id'], 行['问题类别'], 行['同类条数'], 行['可规则化性'], 行['晋升去向'], 行['模块'],
                原样(态(行['状态'] || '—', 提案色(行['状态']))),
                行['提出时间'], 行['裁决人'],
                (行['原文引用'] || []).join('；'),
                原样(提案按钮(行))
            ]
        };
    }, { 脚: '已拒绝与已落地是终态，不给按钮——终态不许覆盖。' });
}

function 提案色(状态) {
    if (状态 === '待批') { return '黄'; }
    if (状态 === '已落地') { return '绿'; }
    if (状态 === '已批准') { return '蓝'; }
    if (状态 === '已拒绝') { return '灰'; }
    return '灰';
}

function 提案按钮(行) {
    var 有操作人 = (元素('操作人').value || '').trim().length > 0;
    var 禁 = 有操作人 ? '' : ' disabled';
    if (行['状态'] === '待批') {
        return '<button class="钮 细 主 提案裁决按钮" data-提案="' + 转义(行['id']) + '" data-动作="批准"' + 禁 +
            ' onclick="裁决提案(this)">批准</button> ' +
            '<button class="钮 细 危 提案裁决按钮" data-提案="' + 转义(行['id']) + '" data-动作="拒绝"' + 禁 +
            ' onclick="裁决提案(this)">拒绝</button>';
    }
    if (行['状态'] === '已批准') {
        return '<button class="钮 细" data-提案="' + 转义(行['id']) + '" onclick="落地提案(this)">落地</button>';
    }
    return '<span class="弱">终态</span>';
}

function 裁决提案(按钮) {
    var 提案id = 按钮.getAttribute('data-提案');
    var 动作 = 按钮.getAttribute('data-动作');
    var 操作人 = 校验操作人();
    if (!操作人) { return; }
    if (动作 === '拒绝' && !window.confirm('拒绝是终态，之后不许覆盖。确定拒绝 ' + 提案id + '？')) { return; }
    发命令('task.promotion.decide --RepositoryRoot . --PoolRoot Pools --ProposalIdentifier ' + 提案id +
        ' --Action ' + 动作 + ' --DeciderName ' + 操作人);
}

function 落地提案(按钮) {
    var 提案id = 按钮.getAttribute('data-提案');
    发命令('task.promotion.decide --RepositoryRoot . --PoolRoot Pools --ProposalIdentifier ' + 提案id + ' --Action 落地');
}

// ── 下游设施两页 ──

function 渲染供给对账(数据) {
    记徽章('供给对账', (数据 || []).length, '');
    if (!数据 || 数据.length === 0) {
        return 空态('Bridges/ 下还没有 driver', '下游 driver 是按目录发现的；这个目录现在是空的。');
    }
    var 一致 = 0;
    var 失配 = 0;
    var 问题合计 = 0;
    for (var i = 0; i < 数据.length; i++) {
        if (数据[i]['对账'] === '一致') { 一致++; }
        if (数据[i]['对账'] === '失配') { 失配++; }
        问题合计 += 数字(数据[i]['问题数']);
    }
    var 文本 = '<div class="格 自适应" style="margin-bottom:16px;">' +
        指标(数据.length, 'driver 数', { 边: '蓝' }) +
        指标(一致, '对账一致', { 边: 一致 === 数据.length ? '绿' : '', 色: 一致 === 数据.length ? '绿' : '' }) +
        指标(失配, '对账失配', { 边: 失配 > 0 ? '红' : '', 色: 失配 > 0 ? '红' : '' }) +
        指标(问题合计, '问题合计', { 色: 问题合计 > 0 ? '红' : '' }) +
        '</div>';
    文本 += '<p class="弱 小字">「未跑」不染绿——没对过账和对上了是两回事。' +
        '「供给」列只说明推过一次，形状跟没跟上要看「对账」列。</p>';
    return 文本 + 表格('', [
        'driver', '形态', '端口', '供给', '对账', '依赖清单', { 名: '配方数', 数值: true }, { 名: '问题数', 数值: true }
    ], 数据, function (行) {
        return {
            类: 行['对账'] === '失配' ? '重' : '',
            格: [
                行['driver'], 行['形态'],
                (行['端口'] || []).join('、'),
                行['供给'],
                原样(态(行['对账'] || '—', 对账色(行['对账']))),
                原样(行['依赖清单'] ? 态('有', '绿') : '<span class="弱">无</span>'),
                行['配方数'], 行['问题数']
            ]
        };
    });
}

function 对账色(状态) {
    if (状态 === '一致') { return '绿'; }
    if (状态 === '失配') { return '红'; }
    return '灰';
}

function 渲染下游(数据) {
    if (!数据 || 数据.length === 0) {
        记徽章('下游', 0, '');
        return 空态('Bridges/ 下还没有 driver', '下游 driver 是按目录发现的；这个目录现在是空的。');
    }
    var 齐 = 0;
    var i;
    for (i = 0; i < 数据.length; i++) {
        if (!数据[i]['读失败'] && 必填齐(数据[i])) { 齐++; }
    }
    记徽章('下游', 齐 + '/' + 数据.length, 齐 === 数据.length ? '绿' : '黄');
    var 文本 = '<div class="板"><div class="板题">装机进度<span class="右">换一台执行机就照这个填绿</span></div>' +
        环(齐, 数据.length, '必填字段全配齐的 driver 数。缺一项就不算齐。字段只报「配没配」，值一律不显示——' +
            '这一页的语义是「配没配」，不是「配了什么」；密钥的值永不读取。') + '</div>';
    文本 += '<div class="格 宽卡">';
    for (i = 0; i < 数据.length; i++) { 文本 += 下游卡(数据[i]); }
    return 文本 + '</div>';
}

function 必填齐(行) {
    var 字段们 = 行['字段'] || [];
    for (var i = 0; i < 字段们.length; i++) {
        if (字段们[i]['必填'] && 字段们[i]['状态'] !== '已配') { return false; }
    }
    return true;
}

// 「已供给」只说明产物往下游推过一次，不说明下游那边的形状跟上了——
// 这两件事分开说，是因为它们真的会分家：推过之后 schema 又加了列，供给目录照样在。
var 已供给的含义 = '只说明产物往下游推过一次。下游那边的形状有没有跟上，看「供给对账」页的对账列。';

function 下游卡(行) {
    if (行['读失败']) {
        return '<div class="板 可滤" style="border-color:var(--红);margin:0;">' +
            '<div class="板题">' + 转义(行['driver']) + 态('自述读不出来', '红') + '</div>' +
            '<p class="红 小字" style="margin:0;">' + 转义(行['读失败']) + '</p></div>';
    }
    var 字段们 = 行['字段'] || [];
    var 已配 = 0;
    var 必填数 = 0;
    var i;
    for (i = 0; i < 字段们.length; i++) {
        if (字段们[i]['必填']) { 必填数++; }
        if (字段们[i]['状态'] === '已配') { 已配++; }
    }
    var 齐 = 必填齐(行);
    var 卡 = '<div class="板 可滤" style="margin:0;border-color:' + (齐 ? 'var(--线)' : 'var(--线亮)') + ';">' +
        '<div class="板题"><span>' + 转义(行['driver']) + '</span>' +
        '<span class="右">' + 转义(行['形态']) + ' · 契约 ' + 转义(行['契约']) + '</span></div>' +
        '<div style="display:flex;gap:6px;flex-wrap:wrap;margin-bottom:9px;">' +
        (行['供给'] ? 态('已供给', '绿', 已供给的含义) : 态('未供给', '灰')) +
        (齐 ? 态('必填齐了', '绿') : 态('还缺必填', '黄')) +
        '</div>';
    if (行['本机配置说明']) {
        卡 += '<p class="红 小字">' + 转义(行['本机配置说明']) + '</p>';
    }
    卡 += '<div class="小字 次" style="margin-bottom:5px;">配置字段 ' + 已配 + ' / ' + 字段们.length +
        '（必填 ' + 必填数 + ' 项）</div>' + 进度(已配, 字段们.length, 齐 ? '绿' : '黄');
    卡 += '<div style="display:flex;flex-wrap:wrap;gap:5px;margin:9px 0;">';
    for (i = 0; i < 字段们.length; i++) {
        var 字段 = 字段们[i];
        var 已 = 字段['状态'] === '已配';
        var 色类 = 已 ? '绿' : (字段['必填'] ? '黄' : '灰');
        var 提示 = 转义(字段['名'] + (字段['必填'] ? '（必填）' : '（选填）') + '：' + (已 ? '已配' : '未配'));
        卡 += '<span class="态 ' + 色类 + '" title="' + 提示 + '">' +
            转义(字段['名']) + (字段['必填'] ? ' *' : '') + '</span>';
    }
    卡 += '</div>';
    // 这句里的路径是**第二个来源**：真相在 CreationPanelReader.LocalConfigFilePath。
    // 目录一改名，这句就把人带到不存在的地方去——已经发生过一次（原文写的是 Config/创作管线/）。
    // 该由后端把路径随数据一起给过来，那要动 PanelBridgeRow 的形状；
    // 去中文化那几批正在改同一个文件，等尘埃落定再收，先记在批次日志的缺口里。
    // 从前这句写的是「要改配置直接编辑 local.json」——而**面板上本来就有编辑器**
    // （桥接包页那四样里的第一样）。把人支去手改文件，等于把面板存在的理由否掉一半：
    // 真发生过——人找了一圈没找到在哪换助手用的模型，以为根本没有入口。
    卡 += '<p class="弱 小字">值一律不显示（密钥的值永不读取）。' +
        '要改地址 / 模型 / 密钥，去 <a href="#" onclick="去(&#39;桥接包&#39;);return false;">桥接包</a> 页' +
        '——那一页可以就地改，改完自动重探模型清单。</p>';
    if (行['对账成']) {
        var 说明们 = 行['对账说明'] || [];
        卡 += '<div class="小字" style="margin-top:8px;">能力对账 ' +
            态('满足 ' + 行['满足数'] + ' / 共 ' + 行['依赖数'],
                数字(行['满足数']) >= 数字(行['依赖数']) ? '绿' : '黄') + '</div>';
        for (i = 0; i < 说明们.length; i++) {
            卡 += '<div class="红 小字">' + 转义(说明们[i]) + '</div>';
        }
    } else {
        var 未对账 = 行['对账说明'] || [];
        for (i = 0; i < 未对账.length; i++) {
            卡 += '<div class="弱 小字" style="margin-top:6px;">' + 转义(未对账[i]) + '</div>';
        }
    }
    if (行['试跑'] && 在放行族里(行['试跑'])) {
        卡 += '<button class="钮 细" style="margin-top:9px;" data-试跑="' + 转义(行['试跑']) + '"';
        卡 += ' onclick="跑试跑(this)">试跑一次</button>';
    } else if (行['试跑']) {
        // 自述里写了试跑命令，但它不在面板的放行族里：点了必然被白名单拒绝。
        // 与其给个注定失败的按钮，不如当场说清楚这条路为什么不通、该改哪一头。
        卡 += '<button class="钮 细" style="margin-top:9px;" disabled>试跑一次</button>';
        卡 += '<div class="黄 小字">试跑命令 ' + 转义(String(行['试跑']).split(' ')[0]) +
            ' 不在面板放行族里，点了会被拒。要么改自述里的试跑命令，要么扩白名单——两头都是人来定。</div>';
    } else {
        卡 += '<div class="弱 小字" style="margin-top:9px;">自述里没有可跑的试跑命令</div>';
    }
    return 卡 + '</div>';
}

// 命令名在不在放行族里。按钮渲染时就判一次：给一个点了必然被拒的按钮，
// 比不给按钮更糟——人点下去、等一下、被拒，才知道这条路不通（决策 48 的精神）。
function 在放行族里(命令行) {
    var 名 = String(命令行 || '').split(' ')[0];
    for (var i = 0; i < 放行族.length; i++) {
        if (名.indexOf(放行族[i]) === 0) { return true; }
    }
    return false;
}

function 跑试跑(按钮) {
    发命令(按钮.getAttribute('data-试跑'));
}

// ── 桥接包页 ──
// 这一页回答的是「这台机器上，每个编辑器与每个下游要装的东西装齐没有」。
// 与下游页分工分明：下游页问「配没配」（local.json 里的字段），这一页问「装没装」（磁盘上的证据）。
// 四种状态里最要紧的是「未验」——本机还没探过、编辑器还没解析过。
// 它既不染绿也不染红：染绿是把没查过说成有，染红是把没查过说成没有，两种都是撒谎。

var 装机色表 = { '已装': '绿', '缺': '红', '未验': '黄', '无需安装': '灰' };

function 装机色(状态) {
    return 装机色表.hasOwnProperty(状态) ? 装机色表[状态] : '灰';
}

// 类别顺序写死在这里，表按它分区渲染，不按数据里出现的先后——
// 数据顺序跟着宿主发现顺序走，每刷新一次分区就换个位置，人会以为自己看错了。
// 清单里冒出没登记过的类别时不丢，排在最后（宁可多一块，也不让一件东西人间蒸发）。
var 类别顺序 = ['编辑器包', '编辑器插件', '节点', '模型', 'lora', '驱动脚本'];

// 一句话说清每类是什么、装没装的判据从哪来。不写这句，六个类别名对外人就是六个黑盒。
var 类别释义 = {
    '编辑器包': '包管理器（UPM）认得的包，判据是 manifest 条目加本地包目录 / PackageCache 解析结果',
    '编辑器插件': '解包进宿主目录的插件，包管理器看不见它；判据是 editor-plugins.json 里声明的标志路径',
    '节点': '下游服务里的扩展节点，判据是上一次能力探测的输出',
    '模型': '下游服务要加载的模型文件，判据同样是能力探测',
    'lora': '下游服务的 lora 权重，判据同样是能力探测',
    '驱动脚本': '随仓库走的脚本，不往宿主里装，判据只有文件在不在'
};

var 桥接包筛选 = '全部';
var 桥接包只看待办 = false;

function 渲染桥接包(数据) {
    if (!数据 || 数据.length === 0) {
        记徽章('桥接包', 0, '');
        return 空态('没有可列的宿主', 'UnityProject/ 与 Bridges/ 都没找到——这一页按这两处现算，不存任何清单。');
    }
    var 本体齐 = 0;
    var 已装 = 0;
    var 缺 = 0;
    var 未验 = 0;
    var 全部包 = [];
    var i;
    var k;
    for (i = 0; i < 数据.length; i++) {
        var 宿 = 数据[i];
        if (宿['本体'] === '已装' || 宿['本体'] === '无需安装') { 本体齐++; }
        var 包们 = 宿['包'] || [];
        for (k = 0; k < 包们.length; k++) {
            var 包 = 包们[k];
            if (包['状态'] === '已装') { 已装++; }
            if (包['状态'] === '缺') { 缺++; }
            if (包['状态'] === '未验') { 未验++; }
            全部包.push({ 宿主: 宿['宿主'], 包: 包 });
        }
    }
    记徽章('桥接包', 缺 > 0 ? ('缺 ' + 缺) : (未验 > 0 ? ('未验 ' + 未验) : '齐'),
        缺 > 0 ? '红' : (未验 > 0 ? '黄' : '绿'));

    var 文本 = '<div class="格 自适应" style="margin-bottom:16px;">' +
        指标(本体齐 + ' / ' + 数据.length, '本体就绪的宿主', {
            边: 本体齐 === 数据.length ? '绿' : '黄',
            脚: '编辑器或服务本身装没装；线上服务本来就不用装',
            条: 进度(本体齐, 数据.length, 本体齐 === 数据.length ? '绿' : '黄')
        }) +
        指标(已装, '桥接包已装', { 边: '绿', 色: '绿', 脚: '磁盘上找得到证据的' }) +
        指标(缺, '桥接包缺', { 边: 缺 > 0 ? '红' : '', 色: 缺 > 0 ? '红' : '', 脚: '查过了，确实不在' }) +
        指标(未验, '未验', { 边: 未验 > 0 ? '黄' : '', 色: 未验 > 0 ? '黄' : '', 脚: '还没探过或编辑器还没解析过，不是「没有」' }) +
        '</div>';
    // 这段自述改过一次：原文写的是「一个字都不会写进 manifest.json、editor-plugins.json 或 local.json，
    // 能在这里点的只有试跑一次」。那句话在卡上长出配置保存、页下长出插件表单之后就已经是假的了，
    // 现在又多了装脚本包这一路。页面自述与页面行为对不上，比没有自述更糟——人会照着假话去别处找。
    文本 += '<p class="弱 小字">这一页能点的有四样：改本机配置（写 local.json）、增删插件声明（写 editor-plugins.json）、' +
        '试跑一次、以及把随仓库走的脚本包装进宿主（往宿主目录里写文件，仓库外，git diff 看不见）。' +
        '其余一律只报告与指路——UPM 包、下游的节点与模型都得照「下一步」那一列自己装，这一页不代劳。' +
        '还有：「未验」不等于「没有」，那是判据还没凑齐，别当成「缺」去补。</p>';

    文本 += '<div class="格 宽卡" style="margin-bottom:16px;">';
    for (i = 0; i < 数据.length; i++) { 文本 += 宿主卡(数据[i]); }
    文本 += '</div>';

    文本 += 插件表单(数据);
    文本 += 类别筛选条(全部包);
    return 文本 + 分类表(全部包);
}

// ── 路由页：改哪个域用哪个下游 ──
// 这一页写的是 downstream.json（进 git，改它是改整个项目的选择），
// 不是 local.json（各人机器上的地址与密钥）。两份别搞混。
//
// 页面自己不存状态：候选顺序改完先放在 DOM 里，点「保存」才发一条 bridge.route.set。
// 没点保存就切页 = 没改过，这跟别的页一致。

var 路由草稿 = {};

// 保留草稿 = true 时不从数据重建草稿：调顺序、加减候选都是先落在草稿上的，
// 重建一次就把人刚点的那几下全冲掉了（第一版就是这么错的，↑↓ 点了没反应）。
function 渲染路由(数据, 保留草稿) {
    if (!数据 || 数据.length === 0) {
        记徽章('路由', 0, '');
        return 空态('域路由表里一个域都没有', '看 Tools/CreationPipeline/Config/downstream.json 的「域路由」那一节。');
    }
    var 坏 = 0;
    var i;
    var k;
    if (!保留草稿) { 路由草稿 = {}; }
    for (i = 0; i < 数据.length; i++) {
        var 候选们 = 数据[i]['候选'] || [];
        var 名单 = [];
        for (k = 0; k < 候选们.length; k++) {
            名单.push(候选们[k]['名']);
            if (候选们[k]['毛病']) { 坏++; }
        }
        if (!路由草稿.hasOwnProperty(数据[i]['域'])) {
            路由草稿[数据[i]['域']] = { 候选: 名单, 策略: 数据[i]['策略'] };
        }
    }

    var 文本 = '<div class="格">' +
        指标(数据.length, '域', { 脚: '域路由表里有几条' }) +
        指标(坏, '坏候选', { 边: 坏 > 0 ? '红' : '', 色: 坏 > 0 ? '红' : '', 脚: 'driver 不存在，或它没声明这个域' }) +
        '</div>';
    文本 += '<p class="弱 小字">候选顺序就是优先级，第一个是首选。' +
        '「首选固定」= 首选挂了就挂；「失败转移」= 顺着往下试，全试完才算失败。' +
        '开转移前请先读一句：候选之间得吃同一份调用参数，而且「超时」也算可转移——' +
        '万一那次其实在下游跑完了，换人会重复计费一次。</p>';

    文本 += '<div class="格 宽卡">';
    for (i = 0; i < 数据.length; i++) { 文本 += 路由卡(数据[i]); }
    记徽章('路由', 坏 > 0 ? String(坏) : '', 坏 > 0 ? '红' : '');
    return 文本 + '</div>';
}

function 路由卡(行) {
    var 域 = 行['域'];
    var 边色 = 行['毛病'] ? 'var(--红)' : 'var(--线)';
    var 卡 = '<div class="板" style="margin:0;border-color:' + 边色 + ';">' +
        '<div class="板题"><span>' + 转义(域) + '</span><span class="右">' + 转义(行['策略'] || '') + '</span></div>';
    if (行['毛病']) {
        return 卡 + '<p class="红 小字">' + 转义(行['毛病']) + '</p></div>';
    }
    if (!可拼实参(域)) {
        return 卡 + '<p class="黄 小字">这个域名里有引号或反斜杠，面板改不了它，手开 downstream.json 改。</p></div>';
    }
    卡 += '<div id="路由候选_' + 转义(域) + '">' + 候选列表(域) + '</div>';
    卡 += 待排候选(域, 行['可选driver'] || []);
    卡 += '<div style="display:flex;gap:5px;align-items:center;margin-top:9px;flex-wrap:wrap;">';
    卡 += '<span class="小字" style="min-width:42px;">策略</span>';
    var 当前策略 = (路由草稿[域] || {}).策略 || 行['策略'];
    卡 += '<select class="输" id="路由策略_' + 转义(域) + '" onchange="记策略(' + 引(域) + ', this.value)" style="flex:1;min-width:120px;">';
    卡 += 策略项('首选固定', 当前策略);
    卡 += 策略项('失败转移', 当前策略);
    卡 += '</select>';
    卡 += '<button class="钮 细" onclick="存路由(' + 引(域) + ')">保存</button>';
    卡 += '</div>';
    return 卡 + '</div>';
}

function 策略项(值, 当前) {
    return '<option value="' + 值 + '"' + (值 === 当前 ? ' selected' : '') + '>' + 值 + '</option>';
}

// 策略也进草稿：不进的话重画一次候选就把下拉打回原样，人以为自己没点中。
function 记策略(域, 值) {
    if (路由草稿[域]) { 路由草稿[域].策略 = 值; }
}

// 候选清单：上下箭头调优先级、× 移出。全是对草稿的操作，不发请求。
function 候选列表(域) {
    var 草 = 路由草稿[域];
    if (!草 || 草.候选.length === 0) {
        return '<p class="黄 小字">一个候选都没有——这个域现在没有下游可用。</p>';
    }
    var 文本 = '';
    for (var i = 0; i < 草.候选.length; i++) {
        var 名 = 草.候选[i];
        文本 += '<div style="display:flex;gap:5px;align-items:center;margin-bottom:4px;">';
        文本 += '<span class="小字" style="min-width:30px;">' + (i === 0 ? '首选' : String(i + 1)) + '</span>';
        文本 += '<span class="等宽" style="flex:1;">' + 转义(名) + '</span>';
        文本 += '<button class="钮 细" onclick="挪候选(' + 引(域) + ', ' + i + ', -1)"' + (i === 0 ? ' disabled' : '') + '>↑</button>';
        文本 += '<button class="钮 细" onclick="挪候选(' + 引(域) + ', ' + i + ', 1)"' + (i === 草.候选.length - 1 ? ' disabled' : '') + '>↓</button>';
        文本 += '<button class="钮 细 危" onclick="删候选(' + 引(域) + ', ' + i + ')">×</button>';
        文本 += '</div>';
    }
    return 文本;
}

// 还没排进来、但声明了这个域的 driver。点一下加到队尾。
function 待排候选(域, 可选们) {
    var 草 = 路由草稿[域];
    var 剩 = [];
    for (var i = 0; i < 可选们.length; i++) {
        if (草.候选.indexOf(可选们[i]) < 0) { 剩.push(可选们[i]); }
    }
    if (剩.length === 0) { return ''; }
    var 文本 = '<div class="弱 小字" style="margin-top:7px;">还能排进来：</div><div style="display:flex;gap:5px;flex-wrap:wrap;margin-top:3px;">';
    for (i = 0; i < 剩.length; i++) {
        文本 += '<button class="钮 细" onclick="加候选(' + 引(域) + ', ' + 引(剩[i]) + ')">+ ' + 转义(剩[i]) + '</button>';
    }
    return 文本 + '</div>';
}

function 挪候选(域, 位置, 方向) {
    var 草 = 路由草稿[域];
    var 目标 = 位置 + 方向;
    if (!草 || 目标 < 0 || 目标 >= 草.候选.length) { return; }
    var 暂 = 草.候选[位置];
    草.候选[位置] = 草.候选[目标];
    草.候选[目标] = 暂;
    重画路由卡(域);
}

function 删候选(域, 位置) {
    var 草 = 路由草稿[域];
    if (!草) { return; }
    草.候选.splice(位置, 1);
    重画路由卡(域);
}

function 加候选(域, 名) {
    var 草 = 路由草稿[域];
    if (!草 || 草.候选.indexOf(名) >= 0) { return; }
    草.候选.push(名);
    重画路由卡(域);
}

// 重画整页但**保留草稿**：不重取数据，也不重建草稿，
// 所以别的卡上没保存的改动也还在。
function 重画路由卡(域) {
    内容区.innerHTML = 渲染路由(本页数据, true);
}

function 存路由(域) {
    var 草 = 路由草稿[域];
    if (!草) { return; }
    if (草.候选.length === 0) {
        吐司('候选是空的，这个域会没有下游可用——先加一个再保存', false);
        return;
    }
    var 策略节 = 元素('路由策略_' + 域);
    var 策略 = 策略节 ? 策略节.value : 草.策略;
    发命令JSON('bridge.route.set', {
        Port: 域,
        Candidates: 草.候选.join(','),
        Strategy: 策略,
        RepositoryRoot: '.'
    }, function (结果) {
        吐司(结果.成功 ? 首行(结果.文本) : ('没存成：' + 首行(结果.文本)), 结果.成功);
        // 存成了才把草稿丢掉：下一次取数会照文件里的新状态重建它。
        if (结果.成功) { delete 路由草稿[域]; 刷新(); }
    });
}

// 页内筛选：按类别切、以及「只看没装好的」。两个都不重新取数——数据就在眼前，
// 再跑一趟文件只会让人以为自己点出了什么副作用。
function 重画桥接包() {
    内容区.innerHTML = 渲染桥接包(本页数据);
    应用过滤();
}

function 换桥接包类别(名) {
    桥接包筛选 = 名;
    重画桥接包();
}

function 切桥接包待办() {
    桥接包只看待办 = !桥接包只看待办;
    重画桥接包();
}

// 类别按「清单里真有的」列，不是把六个类别全摆出来——
// 一个空类别的按钮点下去只会得到一张空表，那是白给一次失望。
function 类别列表(全部包) {
    var 有的 = [];
    var i;
    var k;
    for (i = 0; i < 类别顺序.length; i++) {
        for (k = 0; k < 全部包.length; k++) {
            if (全部包[k].包['类别'] === 类别顺序[i]) { 有的.push(类别顺序[i]); break; }
        }
    }
    for (i = 0; i < 全部包.length; i++) {
        var 类 = 全部包[i].包['类别'] || '未分类';
        if (类别顺序.indexOf(类) < 0 && 有的.indexOf(类) < 0) { 有的.push(类); }
    }
    return 有的;
}

function 类别筛选条(全部包) {
    var 类们 = 类别列表(全部包);
    var 条 = '<div class="板" style="margin-bottom:12px;"><div class="板题">按类别看' +
        '<span class="右 小字 弱">六个类别的判据各不相同，混在一张表里看不出所以然</span></div>' +
        '<div style="display:flex;flex-wrap:wrap;gap:6px;align-items:center;">';
    条 += 类别钮('全部', 全部包.length, 桥接包筛选 === '全部');
    var i;
    for (i = 0; i < 类们.length; i++) {
        条 += 类别钮(类们[i], 类别计数(全部包, 类们[i]), 桥接包筛选 === 类们[i]);
    }
    条 += '<span style="flex:1;"></span>';
    条 += '<button class="钮 细' + (桥接包只看待办 ? ' 主' : '') + '" onclick="切桥接包待办()">';
    条 += 转义(桥接包只看待办 ? '只看没装好的（开）' : '只看没装好的') + '</button>';
    return 条 + '</div></div>';
}

function 类别钮(名, 数, 当前) {
    return '<button class="钮 细' + (当前 ? ' 主' : '') + '" onclick="换桥接包类别(' + 引(名) + ')">' +
        转义(名 + ' ' + 数) + '</button>';
}

function 类别计数(全部包, 类) {
    var 数 = 0;
    for (var i = 0; i < 全部包.length; i++) {
        if (全部包[i].包['类别'] === 类) { 数++; }
    }
    return 数;
}

// 一类一张表。类别当表题，所以表里不再有「类别」列——同一张表里每行都一样的列是纯噪音。
function 分类表(全部包) {
    var 类们 = 类别列表(全部包);
    var 文本 = '';
    var 画了 = 0;
    for (var i = 0; i < 类们.length; i++) {
        if (桥接包筛选 !== '全部' && 桥接包筛选 !== 类们[i]) { continue; }
        var 这类 = [];
        for (var k = 0; k < 全部包.length; k++) {
            var 条 = 全部包[k];
            if (条.包['类别'] !== 类们[i]) { continue; }
            if (桥接包只看待办 && 条.包['状态'] !== '缺' && 条.包['状态'] !== '未验') { continue; }
            这类.push(条);
        }
        if (这类.length === 0) { continue; }
        画了++;
        文本 += 一类表(类们[i], 这类);
    }
    if (画了 === 0) {
        return 提示态('这个筛选下没有东西',
            桥接包只看待办 ? '「只看没装好的」开着，而这些类别全部装好了——这是好消息。' : '这一类现在一件都没有。');
    }
    return 文本;
}

function 一类表(类, 这类) {
    var 释 = 类别释义.hasOwnProperty(类) ? 类别释义[类] : '清单里出现的新类别，还没登记它的判据';
    // 只有「编辑器插件」这一类的东西是我们自己声明出来的，所以只有它能改能删；
    // 其余几类的真相在别处（manifest.json、dependencies.json、磁盘），在这里给按钮是骗人。
    var 可改 = 类 === '编辑器插件';
    var 列 = ['宿主', '名', '版本', '状态', '依据', '下一步 / 安装命令'];
    if (可改) { 列.push('声明'); }
    return '<div style="margin-bottom:16px;">' + 表格(类 + ' · ' + 这类.length + ' 件', 列, 这类, function (条) {
        var 包 = 条.包;
        var 格 = [
            条.宿主, 包['名'], 包['版本'],
            原样(态(包['状态'] || '未验', 装机色(包['状态']))),
            包['依据'],
            原样(装法(包))
        ];
        if (可改) { 格.push(原样(声明按钮(条.宿主, 包['名']))); }
        return { 类: 包['状态'] === '缺' ? '重' : '', 格: 格 };
    }, { 脚: 释 }) + '</div>';
}

function 宿主卡(宿) {
    var 边色 = 宿['本体'] === '缺' ? 'var(--红)' : 'var(--线)';
    var 卡 = '<div class="板 可滤" style="margin:0;border-color:' + 边色 + ';">' +
        '<div class="板题"><span>' + 转义(宿['宿主']) + '</span><span class="右">' +
        转义(宿['种类'] + (宿['版本'] ? ' · ' + 宿['版本'] : '')) + '</span></div>';
    if (宿['读失败']) {
        卡 += '<p class="红 小字">' + 转义(宿['读失败']) + '</p>';
    }
    var 包们 = 宿['包'] || [];
    var 已 = 0;
    var i;
    for (i = 0; i < 包们.length; i++) {
        if (包们[i]['状态'] === '已装' || 包们[i]['状态'] === '无需安装') { 已++; }
    }
    卡 += '<div style="display:flex;gap:6px;flex-wrap:wrap;margin-bottom:9px;">' +
        态('本体 ' + (宿['本体'] || '未验'), 装机色(宿['本体'])) +
        态('桥接包 ' + 已 + ' / ' + 包们.length, 已 === 包们.length ? '绿' : '黄') + '</div>';
    if (宿['本体依据']) {
        卡 += '<p class="次 小字" style="margin:0 0 6px 0;">' + 转义(宿['本体依据']) + '</p>';
    }
    if (宿['本体下一步']) {
        卡 += '<p class="黄 小字" style="margin:0 0 6px 0;">下一步：' + 转义(宿['本体下一步']) + '</p>';
    }
    卡 += 进度(已, 包们.length, 已 === 包们.length ? '绿' : '黄');
    卡 += 卡内类别小计(包们);
    卡 += 配置块(宿);
    卡 += 安装按钮们(包们);
    var 知会们 = 宿['知会'] || [];
    for (i = 0; i < 知会们.length; i++) {
        卡 += '<div class="弱 小字" style="margin-top:6px;">' + 转义(知会们[i]) + '</div>';
    }
    if (宿['试跑'] && 在放行族里(宿['试跑'])) {
        卡 += '<button class="钮 细" style="margin-top:9px;" data-试跑="' + 转义(宿['试跑']) + '"';
        卡 += ' onclick="跑试跑(this)">试跑一次</button>';
    } else if (宿['试跑']) {
        卡 += '<button class="钮 细" style="margin-top:9px;" disabled>试跑一次</button>';
        卡 += '<div class="黄 小字">试跑命令 ' + 转义(String(宿['试跑']).split(' ')[0]) +
            ' 不在面板放行族里，点了会被拒。</div>';
    }
    return 卡 + '</div>';
}

// 卡上的安装按钮：**只给「缺」且清单真给了安装命令的那些包**。
// 三条判据缺一不可——状态是「缺」（「未验」时判据都没凑齐，给按钮等于催人去装一个可能已经装了的东西）、
// 安装命令非空（多数包的真相是「照来源页面自己装」，那种给按钮就是骗人）、
// 命令在放行族里（不在的话点了必被白名单拒，决策 19）。
// 按钮文案带包名：一个宿主底下可能不止一个可装的包，只写「安装」人不知道点的是哪个。
// 这里**不许出现任何具体包名或 driver 名**——全从数据来，加一个新包不该回来改这个函数。
function 安装按钮们(包们) {
    if (!包们 || 包们.length === 0) { return ''; }
    var 文本 = '';
    for (var i = 0; i < 包们.length; i++) {
        var 包 = 包们[i];
        if (包['状态'] !== '缺') { continue; }
        var 命令 = 包['安装命令'] || '';
        if (!命令 || !在放行族里(命令)) { continue; }
        文本 += '<button class="钮 细" style="margin-top:9px;margin-right:6px;" data-试跑="' + 转义(命令) + '"';
        文本 += ' onclick="跑试跑(this)">装：' + 转义(包['名']) + '</button>';
    }
    return 文本;
}

// 卡上的类别小计：一个宿主底下可能同时挂着 UPM 包、手装插件、探测出来的节点，
// 判据完全不同。只给一个「20/21」的总数，人看不出差的那一件是哪一类的。
function 卡内类别小计(包们) {
    if (!包们 || 包们.length === 0) { return ''; }
    var 桶 = {};
    var 序 = [];
    var i;
    for (i = 0; i < 包们.length; i++) {
        var 类 = 包们[i]['类别'] || '未分类';
        if (!桶.hasOwnProperty(类)) { 桶[类] = { 已: 0, 总: 0 }; 序.push(类); }
        桶[类].总++;
        if (包们[i]['状态'] === '已装' || 包们[i]['状态'] === '无需安装') { 桶[类].已++; }
    }
    序.sort(function (甲, 乙) {
        var 甲位 = 类别顺序.indexOf(甲);
        var 乙位 = 类别顺序.indexOf(乙);
        return (甲位 < 0 ? 99 : 甲位) - (乙位 < 0 ? 99 : 乙位);
    });
    var 文本 = '<div style="display:flex;flex-wrap:wrap;gap:5px;margin-top:8px;">';
    for (i = 0; i < 序.length; i++) {
        var 项 = 桶[序[i]];
        var 提示 = 类别释义.hasOwnProperty(序[i]) ? 类别释义[序[i]] : '';
        文本 += 态(序[i] + ' ' + 项.已 + '/' + 项.总, 项.已 === 项.总 ? '绿' : '黄', 提示);
    }
    return 文本 + '</div>';
}

function 装法(包) {
    var 文本 = '';
    if (包['下一步']) {
        文本 += '<div class="小字">' + 转义(包['下一步']) + '</div>';
    }
    if (包['安装命令']) {
        文本 += '<div class="等宽" style="margin-top:4px;word-break:break-all;">' + 转义(包['安装命令']) + '</div>';
        文本 += '<button class="钮 细" style="margin-top:4px;" data-文本="' + 转义(包['安装命令']) + '"';
        文本 += ' onclick="复制装法(this)">复制</button>';
    }
    if (包['来源']) {
        文本 += '<div class="弱 小字" style="margin-top:4px;word-break:break-all;">来源：' + 转义(包['来源']) + '</div>';
    }
    return 文本 === '' ? '<span class="弱 小字">没有可给的装法</span>' : 文本;
}

// 复制到剪贴板。面板固定跑在 localhost，浏览器把它当安全上下文，剪贴板接口可用；
// 万一还是被拒（旧浏览器、权限被关），如实说没复制成——命令本身就摆在按钮上面，手选得到。
function 复制装法(按钮) {
    var 文本 = 按钮.getAttribute('data-文本') || '';
    if (!navigator.clipboard || !navigator.clipboard.writeText) {
        吐司('这个浏览器不给复制，命令就在按钮上面，手选一下', false);
        return;
    }
    navigator.clipboard.writeText(文本).then(function () {
        吐司('安装命令已复制', true);
    }).catch(function (错误) {
        吐司('复制没成：' + 错误.message, false);
    });
}

// 「模型」那一格的哨兵值，与 C# 侧 ModelSelection.AutoSentinel 同一个字面量。
// 选它 = 不把模型钉死，每次调用现挑（助手按需求挑，或人在对话里点名）。
var 自动值 = '自动';

// ── 桥接包页：就地改配置 ──
// 从前这一页只能看，改要人手开 local.json——一个漏掉的逗号就能把整页读成红的。
// 现在字段就地改：非密钥预填当前值，密钥给一个永远空着的框。
//
// 密钥这一条 2026-08-22 由项目主人当面拍板放开（决策 78 原文是「不给输入框」）：
// **写这一侧放开，读这一侧一寸不让**——值不进接口返回、不预填、不回显、不报长度，
// 存完输入框自己清空，页面上永远只有「已配 / 未配」。
// 密钥只走 /cmd 的 JSON 参数通道，绝不走命令行那条：命令行会被原样打进命令台和历史里。

function 配置块(宿) {
    var 字段们 = 宿['字段'] || [];
    if (字段们.length === 0) { return ''; }
    var 文本 = '<div style="margin-top:10px;border-top:1px solid var(--线);padding-top:9px;">';
    文本 += '<div class="小字 次" style="margin-bottom:6px;">配置（就地改，直接写进 local.json）</div>';
    for (var i = 0; i < 字段们.length; i++) {
        文本 += 字段行(宿['宿主'], 字段们[i], i, 宿['探测'] || '');
    }
    return 文本 + '</div>';
}

function 字段行(宿主, 字段, 序号, 探测命令) {
    var 名 = 字段['名'];
    var 密 = 字段['密钥'];
    var 行 = '<div style="display:flex;gap:5px;align-items:center;margin-bottom:5px;flex-wrap:wrap;">';
    行 += '<span class="小字" style="min-width:82px;">' + 转义(名) + (密 ? ' 🔒' : '') + '</span>';
    行 += 态(字段['已配'] ? '已配' : '未配', 字段['已配'] ? '绿' : '黄');
    if (!可拼实参(宿主) || !可拼实参(名)) {
        行 += '<span class="黄 小字">这个字段名里有引号或反斜杠，面板改不了它，手开 local.json 改。</span>';
        return 行 + '</div>';
    }
    var 标识 = '配_' + 宿主 + '_' + 序号;
    var 选项们 = 字段['选项'] || [];
    var 模型格 = !!字段['模型格'];
    // 模型格**永远**给下拉，哪怕清单是空的：那一档「自动」不需要清单也能选
    // （它的意思只是「别把模型钉死」），清单空只影响它当场挑不挑得出来。
    var 可选 = !密 && (选项们.length > 0 || 模型格);

    if (可选) {
        // 有可选清单时给下拉。但**永远留一条自己填的路**：清单是上次探测的快照，
        // 下游随时可能多出一个我们还没探到的模型，把这一格做成只能从快照里挑就成了新的枷锁。
        行 += '<select class="输" id="' + 标识 + '_选" style="flex:1;min-width:160px;"';
        行 += ' title="' + 转义(字段['提示']) + '"';
        行 += ' onchange="切换自填(' + 引(标识) + ')">';
        var 命中 = 命中Of(字段, 选项们, 模型格);
        if (模型格) {
            var 选自动 = String(字段['值']) === 自动值;
            行 += '<option value="' + 转义(自动值) + '"' + (选自动 ? ' selected' : '') + '>' + 转义(自动值) + '（每次调用现挑，不钉死）</option>';
        }
        for (var k = 0; k < 选项们.length; k++) {
            var 选中 = String(选项们[k]) === String(字段['值']);
            行 += '<option value="' + 转义(选项们[k]) + '"' + (选中 ? ' selected' : '') + '>' + 转义(选项们[k]) + '</option>';
        }
        行 += '<option value="__自己填__"' + (命中 ? '' : ' selected') + '>自己填…</option>';
        行 += '</select>';
    }

    var 藏输入 = 可选 && 命中Of(字段, 选项们, 模型格) ? 'display:none;' : '';
    行 += '<input class="输" id="' + 标识 + '" style="flex:1;min-width:160px;' + 藏输入 + '"';
    行 += ' title="' + 转义(字段['提示']) + '"';
    if (密) {
        行 += ' type="password" placeholder="粘贴新值，存完不回显">';
    } else {
        行 += ' value="' + 转义(字段['值']) + '">';
    }
    var 可传探测 = 可拼实参(探测命令) ? 探测命令 : '';
    var 实参 = 引(宿主) + ', ' + 引(名) + ', ' + 引(标识) + ', ' + (密 ? 'true' : 'false') + ', ' + 引(可传探测);
    行 += '<button class="钮 细" onclick="存字段(' + 实参 + ')">保存</button>';
    行 += '<button class="钮 细 危" onclick="清字段(' + 引(宿主) + ', ' + 引(名) + ', ' + (密 ? 'true' : 'false') + ')">清空</button>';

    // 模型那一格旁边给一个「重探」：清单跟着地址走，换了密钥、下游上了新模型，
    // 都得重探一次才看得见。不给这个按钮，人就只能去别处点「试跑」再回来。
    if (模型格 && 可传探测 && 在放行族里(可传探测)) {
        行 += '<button class="钮 细" onclick="发命令(' + 引(可传探测) + ')">重探</button>';
    }

    // 这一格该填什么，摆成一行看得见的小字。
    // 从前它只挂在输入框的 title 上——鼠标不悬停就等于没有，而这几个键
    //（多维表格标识 / 知识空间标识 / 需求文档父节点）摆在一起全是一串 token，
    // 光看键名根本分不出哪个是哪个，认错一个就把需求写进别人的地盘。
    if (字段['提示']) {
        行 += '<div class="次 小字" style="flex-basis:100%;">' + 转义(字段['提示']) + '</div>';
    }
    if (字段['选项说明']) {
        行 += '<div class="弱 小字" style="flex-basis:100%;">' + 转义(字段['选项说明']) + '</div>';
    }
    if (字段['自动说明']) {
        行 += '<div class="次 小字" style="flex-basis:100%;">' + 转义(字段['自动说明']) + '</div>';
    }
    return 行 + '</div>';
}

// 当前值在不在可选清单里（模型格还要算上「自动」那一档）。
// 不在就说明是手填的值，那一格得让输入框露出来。
function 命中Of(字段, 选项们, 模型格) {
    if (模型格 && String(字段['值']) === 自动值) { return true; }
    for (var i = 0; i < 选项们.length; i++) {
        if (String(选项们[i]) === String(字段['值'])) { return true; }
    }
    return false;
}

// 下拉挑到「自己填…」时把输入框放出来；挑到具体一项时把值同步进输入框并收起来。
// 保存那一路只读输入框，不读下拉——两个来源的地方留一个真相，省掉「存的到底是哪个」这类问题。
function 切换自填(标识) {
    var 下拉 = 元素(标识 + '_选');
    var 输入 = 元素(标识);
    if (!下拉 || !输入) { return; }
    if (下拉.value === '__自己填__') {
        输入.style.display = '';
        输入.focus();
        return;
    }
    输入.value = 下拉.value;
    输入.style.display = 'none';
}

// onclick 属性里拼实参：值里带引号或反斜杠就拼不出合法的 JS。
// 与其给一个点下去报语法错的按钮，不如当场说清这个字段面板改不了（决策 48 的精神）。
function 可拼实参(值) {
    // 引号与反斜杠用字符码写，不写字面量：这段脚本要过「每行引号成对」那道健全性检查，
    // 一个裸引号会让整行看着像没闭合（转义那个函数里也是这么写的）。
    var 单引 = String.fromCharCode(39);
    var 双引 = String.fromCharCode(34);
    var 反斜杠 = String.fromCharCode(92);
    var 文 = String(值 === null || 值 === undefined ? '' : 值);
    return 文.indexOf(单引) < 0 && 文.indexOf(双引) < 0 && 文.indexOf(反斜杠) < 0;
}

function 存字段(宿主, 字段名, 输入标识, 密钥, 探测命令) {
    var 节 = 元素(输入标识);
    var 值 = 节 ? 节.value : '';
    if (密钥 && !值) {
        吐司('密钥输入框是空的。要删掉它请点「清空」', false);
        return;
    }
    写配置(宿主, 字段名, 值, 密钥, 节, 探测命令);
}

function 清字段(宿主, 字段名, 密钥) {
    var 问 = '清空 ' + 宿主 + ' 的「' + 字段名 + '」？' +
        (密钥 ? '这会把这个密钥键从 local.json 里删掉。' : '这会把这个键删掉（不是留空串——留空串会被判成「已配」）。');
    if (!window.confirm(问)) { return; }
    写配置(宿主, 字段名, '', 密钥, null);
}

// 密钥与非密钥走两条命令：密钥写顶层（bridge.secret.set），其余写「下游配置」那一节。
// 两条都走 JSON 参数通道，请求体不进命令台、不进命令历史。
function 写配置(宿主, 字段名, 值, 密钥, 输入节, 探测命令) {
    var 命令名 = 密钥 ? 'bridge.secret.set' : 'bridge.config.set';
    var 参数 = 密钥
        ? { Field: 字段名, Value: 值, RepositoryRoot: '.' }
        : { Driver: 宿主, Field: 字段名, Value: 值, RepositoryRoot: '.' };
    发命令JSON(命令名, 参数, function (结果) {
        if (结果.成功 && 输入节 && 密钥) { 输入节.value = ''; }
        吐司(结果.成功 ? 首行(结果.文本) : ('没存成：' + 首行(结果.文本)), 结果.成功);
        if (!结果.成功) { return; }
        // 存完「地址」就自动重探一次：模型清单是**跟着地址走**的，
        // 换了地址不重探，那一格列的还是上一个地址的模型——而这件事平时一点都看不出来。
        // 探测命令自己会刷新页面并把输出摆进命令台，探失败也照样看得见。
        if (字段名 === '地址' && 探测命令 && 在放行族里(探测命令)) {
            吐司('地址存好了，正在按新地址重探一次模型清单', true);
            发命令(探测命令);
            return;
        }
        刷新();
    });
}

// 命令输出是多行的，吐司只放得下一句：取最后一句有内容的（命令的结论在末尾）。
function 首行(文本) {
    var 行们 = String(文本 || '').split('\n');
    for (var i = 行们.length - 1; i >= 0; i--) {
        if (行们[i].trim()) { return 行们[i].trim(); }
    }
    return '（命令没有输出）';
}

// ── 桥接包页：插件声明的增删改 ──
// 声明清单是「我们要用哪些插件」这句话，改它就是改主意，所以给完整的增删改。
// 删声明只删这句话，不卸载任何东西——磁盘上的文件面板一个字节都不动。

function 插件表单(数据) {
    var 宿主们 = [];
    for (var i = 0; i < 数据.length; i++) {
        if (数据[i]['种类'] !== '声明') { 宿主们.push(数据[i]['宿主']); }
    }
    var 文本 = '<div class="板" id="插件表单" style="margin-bottom:12px;">' +
        '<div class="板题">加一条插件声明<span class="右 小字 弱">解包安装的那类；UPM 包不写这里，manifest.json 已经管着</span></div>';
    文本 += '<div style="display:flex;flex-wrap:wrap;gap:6px;align-items:center;margin-bottom:6px;">';
    文本 += '<select class="输" id="插_宿主" style="width:130px;">';
    for (i = 0; i < 宿主们.length; i++) {
        文本 += '<option value="' + 转义(宿主们[i]) + '">' + 转义(宿主们[i]) + '</option>';
    }
    文本 += '</select>';
    文本 += '<input class="输" id="插_名称" placeholder="插件名（必填）" style="width:190px;">';
    文本 += '<input class="输" id="插_版本" placeholder="版本" style="width:110px;">';
    文本 += '<input class="输" id="插_标志路径" placeholder="标志路径（装完之后会出现的目录/文件，仓库相对）" style="flex:1;min-width:230px;">';
    文本 += '</div><div style="display:flex;flex-wrap:wrap;gap:6px;align-items:center;">';
    文本 += '<input class="输" id="插_来源" placeholder="来源（下载页）" style="width:250px;">';
    文本 += '<input class="输" id="插_安装步骤" placeholder="安装步骤：点哪里，一句话" style="flex:1;min-width:230px;">';
    文本 += '<input class="输" id="插_说明" placeholder="这插件是干嘛的" style="width:200px;">';
    文本 += '<button class="钮 主" onclick="存插件()">保存声明</button>';
    文本 += '<button class="钮 细" onclick="清插件表单()">清空表单</button>';
    文本 += '</div>';
    文本 += '<p class="弱 小字" style="margin:7px 0 0 0;">标志路径可以先留空：那样这一条记「未验」，' +
        '装完回来填上就变绿。同名同宿主再存一次就是改它。</p>';
    return 文本 + '</div>';
}

function 存插件() {
    var 值 = function (标识) { var 节 = 元素(标识); return 节 ? 节.value.trim() : ''; };
    var 名称 = 值('插_名称');
    var 宿主 = 值('插_宿主');
    if (!名称 || !宿主) {
        吐司('插件名与宿主是必填的', false);
        return;
    }
    发命令JSON('bridge.plugin.set', {
        Name: 名称,
        Host: 宿主,
        MarkerPath: 值('插_标志路径'),
        Version: 值('插_版本'),
        Source: 值('插_来源'),
        InstallSteps: 值('插_安装步骤'),
        Description: 值('插_说明'),
        RepositoryRoot: '.'
    }, function (结果) {
        吐司(结果.成功 ? 首行(结果.文本) : ('没存成：' + 首行(结果.文本)), 结果.成功);
        if (结果.成功) { 清插件表单(); 刷新(); }
    });
}

function 清插件表单() {
    var 标识们 = ['插_名称', '插_版本', '插_标志路径', '插_来源', '插_安装步骤', '插_说明'];
    for (var i = 0; i < 标识们.length; i++) {
        var 节 = 元素(标识们[i]);
        if (节) { 节.value = ''; }
    }
}

// 「改」= 把那条声明原样填回表单，人改完再存一次（同宿主同名即覆盖）。
// 声明原文走接口带下来，不从表格里反解——表格里的「依据」是判定结果，不是声明本身。
function 改插件(宿主, 名) {
    var 条 = 找声明(宿主, 名);
    if (!条) {
        吐司('找不到这条声明，先刷新一下', false);
        return;
    }
    var 填 = function (标识, 值) { var 节 = 元素(标识); if (节) { 节.value = 值 || ''; } };
    var 宿主框 = 元素('插_宿主');
    if (宿主框) { 宿主框.value = 条['宿主']; }
    填('插_名称', 条['名']);
    填('插_版本', 条['版本']);
    填('插_标志路径', 条['标志路径']);
    填('插_来源', 条['来源']);
    填('插_安装步骤', 条['安装步骤']);
    填('插_说明', 条['说明']);
    var 表单 = 元素('插件表单');
    if (表单 && 表单.scrollIntoView) { 表单.scrollIntoView({ block: 'center' }); }
    吐司('这条声明填回表单了，改完点「保存声明」', true);
}

function 删插件(宿主, 名) {
    if (!window.confirm('删掉声明 ' + 宿主 + ' / ' + 名 + '？只删这句声明，磁盘上装好的东西一个字节都不动。')) {
        return;
    }
    发命令JSON('bridge.plugin.remove', { Host: 宿主, Name: 名, RepositoryRoot: '.' }, function (结果) {
        吐司(结果.成功 ? 首行(结果.文本) : ('没删成：' + 首行(结果.文本)), 结果.成功);
        if (结果.成功) { 刷新(); }
    });
}

function 找声明(宿主, 名) {
    var 数据 = 本页数据 || [];
    for (var i = 0; i < 数据.length; i++) {
        var 声明们 = 数据[i]['声明'] || [];
        for (var k = 0; k < 声明们.length; k++) {
            if (声明们[k]['宿主'] === 宿主 && 声明们[k]['名'] === 名) { return 声明们[k]; }
        }
    }
    return null;
}

// 插件行的「改 / 删」两个按钮。名字里带引号拼不进 onclick，那种给不了按钮，如实说明。
function 声明按钮(宿主, 名) {
    if (!可拼实参(宿主) || !可拼实参(名)) {
        return '<span class="弱 小字">名字里有引号，面板改不了它</span>';
    }
    var 文本 = '<button class="钮 细" onclick="改插件(' + 引(宿主) + ', ' + 引(名) + ')">改</button> ';
    return 文本 + '<button class="钮 细 危" onclick="删插件(' + 引(宿主) + ', ' + 引(名) + ')">删</button>';
}

// ── 命令台 ──

// 常用命令。这七条**逐条走面板真跑过**，命令名与参数名都对得上命令宿主——
// 原来的第五条写的是 spec.list，那个命令根本不存在（spec. 这一族至今一条命令都没有），
// 点下去只会得到「未找到命令」。写死一份清单就得跟真清单对过账，不然它就是七个陷阱。
// 进度同步那条**只放干跑**：真跑会写别人的飞书表，那种事不该出现在「点一下就跑」的清单里。
var 速令表 = [
    'pool.validate --PoolRoot Pools',
    'task.status --RepositoryRoot . --PoolRoot Pools',
    'conflict.list --PoolRoot Pools',
    'engine.queue --RepositoryRoot . --PoolRoot Pools',
    'engine.mode --RepositoryRoot .',
    'bridge.inventory --RepositoryRoot .',
    'sync.progress --RepositoryRoot . --PoolRoot Pools --Direction 双向 --DryRun true'
];

function 画速令() {
    var 盒 = 元素('速令');
    var 文本 = '<span class="弱 小字" style="align-self:center;">常用：</span>';
    for (var i = 0; i < 速令表.length; i++) {
        文本 += '<button onclick="填命令(' + i + ')">' + 转义(速令表[i].split(' ')[0]) + '</button>';
    }
    盒.innerHTML = 文本;
}

function 填命令(序号) {
    元素('命令行').value = 速令表[序号];
    元素('命令行').focus();
}

function 开合抽屉(开) {
    var 抽屉 = 元素('命令抽屉');
    var 要开 = 开 === undefined ? !抽屉.classList.contains('开') : 开;
    抽屉.className = 要开 ? '开' : '';
    元素('开抽屉').textContent = 要开 ? '收起命令台' : '命令台';
    if (要开) { 元素('命令行').focus(); }
}

// 发命令：走 /cmd 白名单通道。跑完自动刷新当前页——命令多半会改文件，
// 不刷新的话人看到的还是命令前的旧数据，那正是「看着像没生效」的来源。
function 发命令(命令行) {
    var 输出区 = 元素('命令输出');
    var 态区 = 元素('命令态');
    开合抽屉(true);
    元素('命令行').value = 命令行;
    输出区.className = '';
    输出区.textContent = '执行中…\n' + 命令行;
    态区.textContent = '执行中：' + 命令行.split(' ')[0];
    if (命令历史[命令历史.length - 1] !== 命令行) { 命令历史.push(命令行); }
    历史位 = 命令历史.length;
    fetch('/cmd', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json; charset=utf-8' },
        body: JSON.stringify({ '命令行': 命令行 })
    }).then(function (响应) {
        return 响应.json();
    }).then(function (结果) {
        if (!结果['允许']) {
            输出区.className = '红';
            输出区.textContent = '被白名单拒绝：' + 结果['原因'] + '\n\n' + 命令行;
            态区.textContent = '上一条被拒绝';
            吐司('命令被白名单拒绝', false);
            return;
        }
        var 码 = 结果['退出码'];
        输出区.className = 码 === 0 ? '' : '红';
        输出区.textContent = '退出码 ' + 码 + '\n' + 结果['输出'];
        态区.textContent = '上一条 ' + 命令行.split(' ')[0] + ' → 退出码 ' + 码;
        吐司(码 === 0 ? '命令跑完了，退出码 0' : ('命令退出码 ' + 码), 码 === 0);
        刷新();
    }).catch(function (错误) {
        输出区.className = '红';
        输出区.textContent = '请求失败：' + 错误.message;
        态区.textContent = '上一条请求失败';
        吐司('命令请求失败：' + 错误.message, false);
    });
}

// ── 外观与自动刷新 ──

function 应用主题(主题) {
    document.documentElement.setAttribute('data-主题', 主题);
    存('主题', 主题);
    元素('主题钮').textContent = 主题 === '暗' ? '◐' : '◑';
    元素('主题钮').title = 主题 === '暗' ? '切到亮色（t）' : '切到暗色（t）';
}

function 切主题() {
    应用主题(document.documentElement.getAttribute('data-主题') === '暗' ? '亮' : '暗');
}

function 应用自刷(秒) {
    存('自刷', String(秒));
    if (自刷句柄) { window.clearInterval(自刷句柄); 自刷句柄 = null; }
    if (数字(秒) > 0) {
        自刷句柄 = window.setInterval(刷新, 数字(秒) * 1000);
    }
}

// ── 键盘 ──

function 装键盘() {
    document.addEventListener('keydown', function (事件) {
        var 焦点 = document.activeElement;
        var 在输入里 = 焦点 && (焦点.tagName === 'INPUT' || 焦点.tagName === 'TEXTAREA' || 焦点.tagName === 'SELECT');
        if (事件.key === 'Escape') {
            if (元素('键帮').classList.contains('开')) { 元素('键帮').className = ''; return; }
            if (在输入里) { 焦点.blur(); }
            if (元素('搜索框').value) { 元素('搜索框').value = ''; 应用过滤(); }
            开合抽屉(false);
            return;
        }
        if (在输入里) { return; }
        if (事件.key === '/') { 事件.preventDefault(); 元素('搜索框').focus(); return; }
        if (事件.key === 'r') { 刷新(); return; }
        if (事件.key === '[') { 上一页(); return; }
        if (事件.key === ']') { 下一页(); return; }
        if (事件.key === 'c') { 事件.preventDefault(); 开合抽屉(); return; }
        if (事件.key === 't') { 切主题(); return; }
        if (事件.key === '?') { 元素('键帮').className = '开'; return; }
    });
    元素('键帮').onclick = function () { 元素('键帮').className = ''; };
}

// ── 启动 ──

function 启动() {
    // 默认亮色：这是给人白天看的工作面板，不是终端。
    // 存过偏好的按偏好来，没存过给亮的；侧栏左下角那个按钮（或按 t）随时切。
    应用主题(取('主题') === '暗' ? '暗' : '亮');
    var 自刷 = 取('自刷') || '0';
    元素('自刷选').value = 自刷;
    应用自刷(自刷);
    元素('自刷选').onchange = function () { 应用自刷(this.value); };
    元素('工作台选').value = 当前工作台();
    元素('主题钮').onclick = 切主题;
    元素('刷新钮').onclick = 刷新;
    元素('搜索框').oninput = 应用过滤;
    元素('操作人').value = 取('操作人') || '';
    元素('操作人').oninput = function () {
        存('操作人', this.value);
        // 裁决类按钮的可用性直接跟着这个框走，不必重画整页。
        var 按钮们 = document.querySelectorAll('.裁决按钮, .提案裁决按钮');
        var 有 = this.value.trim().length > 0;
        for (var i = 0; i < 按钮们.length; i++) { 按钮们[i].disabled = !有; }
    };
    元素('开抽屉').onclick = function () { 开合抽屉(); };
    元素('收抽屉').onclick = function () { 开合抽屉(false); };
    元素('执行').onclick = function () { 发命令(元素('命令行').value); };
    元素('命令行').onkeydown = function (事件) {
        if (事件.key === 'Enter') { 发命令(this.value); return; }
        if (事件.key === 'ArrowUp' && 命令历史.length > 0) {
            历史位 = Math.max(0, 历史位 - 1);
            this.value = 命令历史[历史位] || '';
            事件.preventDefault();
            return;
        }
        if (事件.key === 'ArrowDown' && 命令历史.length > 0) {
            历史位 = Math.min(命令历史.length, 历史位 + 1);
            this.value = 命令历史[历史位] || '';
            事件.preventDefault();
        }
    };
    window.onhashchange = function () {
        var 号 = 读地址页号();
        if (号 !== 当前页) { 切换(号, true); }
    };
    画速令();
    装键盘();
    时间句柄 = window.setInterval(画时刻, 5000);
    切换(读地址页号(), true);
}

启动();

