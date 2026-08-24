using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Template.Toolkit.CreationPipeline
{
    /// <summary>帧序列里的一帧：序号 + 落盘路径 + 尺寸。</summary>
    public sealed class FrameSequenceEntry
    {
        /// <summary>
        /// 构造一帧。
        /// </summary>
        /// <param name="index">序号，从 0 起，就是播放顺序。</param>
        /// <param name="path">这一帧的文件路径。</param>
        /// <param name="width">宽，像素；读不出来给 0。</param>
        /// <param name="height">高，像素；读不出来给 0。</param>
        public FrameSequenceEntry(int index, string path, int width, int height)
        {
            Index = index;
            Path = path ?? "";
            Width = width;
            Height = height;
        }

        /// <summary>序号，从 0 起。</summary>
        public int Index { get; }

        /// <summary>这一帧的文件路径。</summary>
        public string Path { get; }

        /// <summary>宽，像素。</summary>
        public int Width { get; }

        /// <summary>高，像素。</summary>
        public int Height { get; }
    }

    /// <summary>
    /// 帧序列描述：**第一步交给人看的那份东西**（任务书 §4.4「产物两步走，中间留人审」）。
    ///
    /// 它回答人在审这批帧时唯一要问的三件事——**几帧、多快、以哪个点对齐**。
    /// 逐帧 PNG 摆在目录里是看得见的，但「这批图到底是一个动画还是一堆散图」
    /// 只有这份描述说得出来；没有它，第二步拼图集就只能靠文件名猜顺序，
    /// 而猜错的表现是动画顺序乱掉，且没有任何地方会报错。
    ///
    /// **锚点是拼图集时对齐用的**，不是装饰：人物帧动画每帧主体高度不一样，
    /// 按左上角对齐会让角色在原地上下跳。缺省「底边中点」对的是脚。
    /// </summary>
    public sealed class FrameSequence
    {
        /// <summary>描述文件的固定文件名，落在帧所在目录里。</summary>
        public const string FileName = "frames.json";

        /// <summary>当前契约版本。</summary>
        public const string ContractVersion = "1.0.0";

        /// <summary>缺省锚点：底边中点，对的是脚。</summary>
        public const string DefaultAnchor = "底边中点";

        /// <summary>合法的锚点取值。</summary>
        public static readonly string[] AllowedAnchors = { "底边中点", "中心", "左上角" };

        /// <summary>缺省帧率。</summary>
        public const int DefaultFrameRate = 12;

        /// <summary>写盘选项：缩进、中文原样，人要直接读这份文件。</summary>
        private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions(JsonSerializerOptions.Default)
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>
        /// 构造一份帧序列描述。
        /// </summary>
        /// <param name="kind">这批帧是哪一路出来的：帧动画 / 人物帧动画 / 3D动画。</param>
        /// <param name="frameRate">帧率，帧每秒。</param>
        /// <param name="anchor">锚点。</param>
        /// <param name="frames">逐帧，按序号序。</param>
        /// <param name="source">出处一句话：配方名或转台模式，人审时要知道这批是怎么来的。</param>
        public FrameSequence(string kind, int frameRate, string anchor, IReadOnlyList<FrameSequenceEntry> frames, string source)
        {
            Kind = kind ?? "";
            FrameRate = frameRate;
            Anchor = anchor ?? DefaultAnchor;
            Frames = frames ?? Array.Empty<FrameSequenceEntry>();
            Source = source ?? "";
        }

        /// <summary>这批帧是哪一路出来的。</summary>
        public string Kind { get; }

        /// <summary>帧率，帧每秒。</summary>
        public int FrameRate { get; }

        /// <summary>锚点。</summary>
        public string Anchor { get; }

        /// <summary>逐帧，按序号序。</summary>
        public IReadOnlyList<FrameSequenceEntry> Frames { get; }

        /// <summary>出处一句话。</summary>
        public string Source { get; }

        /// <summary>帧数。</summary>
        public int FrameCount => Frames.Count;

        /// <summary>整段时长，秒；帧率非正时给 0。</summary>
        public double DurationSeconds => FrameRate > 0 ? (double)FrameCount / FrameRate : 0d;

        /// <summary>
        /// 从一个目录里的 PNG 扫出一份帧序列。
        ///
        /// **按文件名序数序排**，不按修改时间：一批帧常常是并发写出来的，
        /// 时间戳的先后跟播放顺序毫无关系（真踩过：按时间排出来的走路动画左右脚是乱的）。
        /// </summary>
        /// <param name="directory">帧所在目录。</param>
        /// <param name="kind">这批帧是哪一路出来的。</param>
        /// <param name="frameRate">帧率。</param>
        /// <param name="anchor">锚点。</param>
        /// <param name="source">出处一句话。</param>
        public static FrameSequence Scan(string directory, string kind, int frameRate, string anchor, string source)
        {
            var frames = new List<FrameSequenceEntry>();
            if (!Directory.Exists(directory))
            {
                return new FrameSequence(kind, frameRate, anchor, frames, source);
            }

            var files = Directory.GetFiles(directory, "*.png", SearchOption.TopDirectoryOnly)
                .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
                .ToList();

            var index = 0;
            foreach (var filePath in files)
            {
                var decoded = PngDecoder.DecodeFile(filePath);
                frames.Add(new FrameSequenceEntry(
                    index,
                    filePath,
                    decoded.Succeeded ? decoded.Image.Width : 0,
                    decoded.Succeeded ? decoded.Image.Height : 0));
                index++;
            }

            return new FrameSequence(kind, frameRate, anchor, frames, source);
        }

        /// <summary>描述文件路径：帧目录下的 frames.json。</summary>
        /// <param name="directory">帧所在目录。</param>
        public static string DescriptionFile(string directory)
        {
            return Path.Combine(directory ?? "", FileName);
        }

        /// <summary>写描述文件，返回写到哪。</summary>
        /// <param name="directory">帧所在目录。</param>
        public string Save(string directory)
        {
            var filePath = DescriptionFile(directory);
            Directory.CreateDirectory(directory);

            var frames = new JsonArray();
            foreach (var frame in Frames)
            {
                frames.Add(new JsonObject
                {
                    ["序号"] = frame.Index,
                    ["路径"] = frame.Path.Replace('\\', '/'),
                    ["宽"] = frame.Width,
                    ["高"] = frame.Height
                });
            }

            var payload = new JsonObject
            {
                ["契约版本"] = ContractVersion,
                ["种类"] = Kind,
                ["出处"] = Source,
                ["帧数"] = FrameCount,
                ["帧率"] = FrameRate,
                ["时长秒"] = Math.Round(DurationSeconds, 3),
                ["锚点"] = Anchor,
                ["帧"] = frames
            };

            File.WriteAllText(filePath, payload.ToJsonString(WriteOptions), new UTF8Encoding(false));
            return filePath;
        }

        /// <summary>
        /// 读描述文件。读不了时带原因返回 null——**不许降级成空序列**：
        /// 空序列会让第二步「拼出一张零帧的图集」并报成功，那是任务书里说的假成功。
        /// </summary>
        /// <param name="filePath">描述文件路径。</param>
        /// <param name="failureReason">读失败的原因；成功时空串。</param>
        public static FrameSequence Load(string filePath, out string failureReason)
        {
            failureReason = "";
            if (!File.Exists(filePath))
            {
                failureReason = $"帧序列描述不存在：{filePath}";
                return null;
            }

            try
            {
                var root = JsonNode.Parse(File.ReadAllText(filePath)) as JsonObject;
                if (root == null)
                {
                    failureReason = $"帧序列描述顶层不是对象：{filePath}";
                    return null;
                }

                var frames = new List<FrameSequenceEntry>();
                if (root["帧"] is JsonArray array)
                {
                    foreach (var item in array)
                    {
                        if (item is not JsonObject frame)
                        {
                            continue;
                        }

                        frames.Add(new FrameSequenceEntry(
                            ReadInt(frame, "序号", frames.Count),
                            frame["路径"]?.GetValue<string>() ?? "",
                            ReadInt(frame, "宽", 0),
                            ReadInt(frame, "高", 0)));
                    }
                }

                frames.Sort((left, right) => left.Index.CompareTo(right.Index));
                return new FrameSequence(
                    root["种类"]?.GetValue<string>() ?? "",
                    ReadInt(root, "帧率", DefaultFrameRate),
                    root["锚点"]?.GetValue<string>() ?? DefaultAnchor,
                    frames,
                    root["出处"]?.GetValue<string>() ?? "");
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is JsonException || exception is InvalidOperationException)
            {
                failureReason = $"帧序列描述读不了：{exception.Message}";
                return null;
            }
        }

        /// <summary>给人看的一段话：几帧、多快、多长、以哪个点对齐、从哪来。</summary>
        public string Describe()
        {
            var builder = new StringBuilder();
            builder.Append(Kind.Length > 0 ? Kind : "帧序列")
                .Append("：").Append(FrameCount).Append(" 帧 · ")
                .Append(FrameRate).Append(" 帧每秒 · ")
                .Append(DurationSeconds.ToString("0.##", CultureInfo.InvariantCulture)).Append(" 秒 · 锚点 ")
                .Append(Anchor);
            if (Source.Length > 0)
            {
                builder.Append(" · 出处 ").Append(Source);
            }

            var sizes = Frames.Select(frame => frame.Width + "×" + frame.Height).Distinct(StringComparer.Ordinal).ToList();
            if (sizes.Count == 1)
            {
                builder.Append(" · 尺寸 ").Append(sizes[0]);
            }
            else if (sizes.Count > 1)
            {
                // 尺寸不齐要**说出来**：拼图集会按最大帧留格，尺寸参差的那几帧会在格子里偏。
                builder.Append(" · 尺寸不齐（").Append(string.Join("、", sizes)).Append("）");
            }

            return builder.ToString();
        }

        /// <summary>读一个整数键；缺失或类型不对给缺省值。</summary>
        private static int ReadInt(JsonObject item, string key, int fallback)
        {
            if (item[key] is JsonValue value && value.TryGetValue<int>(out var number))
            {
                return number;
            }

            return fallback;
        }
    }
}
