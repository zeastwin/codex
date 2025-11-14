using EW_Assistant.Component.MindMap;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace EW_Assistant.Services
{
    /// <summary>
    /// 文档解析器：支持 .docx / .pdf，输出思维导图节点树。
    /// </summary>
    public sealed class DocumentMindMapParser
    {
        private static readonly Regex HeadingRegex = new Regex(@"heading(?<n>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex NumberingRegex = new Regex(@"^(\d+[\.\)])+", RegexOptions.Compiled);

        public async Task<MindMapNode> ParseAsync(string filePath, CancellationToken token = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("文件路径不能为空", nameof(filePath));
            if (!File.Exists(filePath))
                throw new FileNotFoundException("未找到文件", filePath);

            var ext = Path.GetExtension(filePath)?.ToLowerInvariant();
            return await Task.Run(() =>
            {
                return ext switch
                {
                    ".docx" => ParseDocx(filePath),
                    ".pdf" => ParsePdf(filePath),
                    _ => throw new NotSupportedException("仅支持 .docx 与 .pdf")
                };
            }, token).ConfigureAwait(false);
        }

        private MindMapNode ParseDocx(string path)
        {
            var lines = new List<SectionLine>();
            using (var archive = ZipFile.OpenRead(path))
            {
                var entry = archive.GetEntry("word/document.xml");
                if (entry == null)
                    throw new InvalidDataException("DOCX 缺少 word/document.xml");

                using (var stream = entry.Open())
                {
                    var doc = XDocument.Load(stream);
                    XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

                    foreach (var paragraph in doc.Descendants(w + "p"))
                    {
                        var text = string.Concat(paragraph.Descendants(w + "t").Select(t => (string)t ?? string.Empty));
                        var normalized = NormalizeText(text);
                        if (string.IsNullOrWhiteSpace(normalized)) continue;

                        var style = paragraph.Descendants(w + "pStyle").FirstOrDefault();
                        var styleVal = style != null ? (string)style.Attribute(w + "val") : null;
                        var level = ResolveHeadingLevel(styleVal);
                        if (level > 0)
                            lines.Add(new SectionLine(normalized, level, true));
                        else
                            lines.Add(new SectionLine(normalized, 0, false));
                    }
                }
            }

            return BuildTree(Path.GetFileNameWithoutExtension(path), lines);
        }

        private MindMapNode ParsePdf(string path)
        {
            var lines = new List<SectionLine>();
            foreach (var fragment in ExtractPdfFragments(path))
            {
                var normalized = NormalizeText(fragment);
                if (string.IsNullOrWhiteSpace(normalized)) continue;

                var estimate = EstimatePdfHeading(fragment, normalized);
                lines.Add(new SectionLine(normalized, estimate.Level, estimate.IsHeading));
            }

            if (!lines.Any(l => l.IsHeading))
            {
                foreach (var line in lines)
                {
                    line.IsHeading = true;
                    line.Level = 1;
                }
            }

            return BuildTree(Path.GetFileNameWithoutExtension(path), lines);
        }

        private static int ResolveHeadingLevel(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return 0;

            var match = HeadingRegex.Match(raw);
            if (match.Success && int.TryParse(match.Groups["n"].Value, out var heading))
                return ClampInt(heading, 1, 6);

            var digits = Regex.Match(raw, @"\d+");
            if (digits.Success && int.TryParse(digits.Value, out heading))
                return ClampInt(heading, 1, 6);

            return 0;
        }

        private static (int Level, bool IsHeading) EstimatePdfHeading(string rawLine, string normalized)
        {
            var indent = rawLine.Length - rawLine.TrimStart(' ', '\t').Length;
            var numbering = NumberingRegex.Match(normalized);
            if (numbering.Success)
            {
                var depth = numbering.Value.Count(ch => ch == '.' || ch == ')') + 1;
                return (ClampInt(depth, 1, 6), true);
            }

            if (normalized.Length <= 64 && normalized.EndsWith("：", StringComparison.Ordinal))
                return (Math.Max(1, indent / 4 + 1), true);

            if (normalized.All(ch => char.IsUpper(ch) || char.IsDigit(ch) || char.IsWhiteSpace(ch) || char.IsPunctuation(ch)))
                return (Math.Max(1, indent / 4 + 1), true);

            var isBody = normalized.Length > 120;
            var level = Math.Max(1, indent / 4 + 1);
            return (level, !isBody && indent <= 4);
        }

        private static string NormalizeText(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            var trimmed = input.Replace("\t", " ").Trim();
            return Regex.Replace(trimmed, @"\s{2,}", " ");
        }

        private MindMapNode BuildTree(string title, IList<SectionLine> lines)
        {
            var root = new MindMapNode(title ?? "文档")
            {
                IsExpanded = true
            };

            var stack = new Stack<(int Level, MindMapNode Node)>();
            stack.Push((0, root));
            var currentTarget = root;

            foreach (var line in lines)
            {
                if (line.IsHeading)
                {
                    var level = Math.Max(1, line.Level);
                    while (stack.Count > 0 && stack.Peek().Level >= level)
                        stack.Pop();

                    var parent = stack.Count > 0 ? stack.Peek().Node : root;
                    var node = new MindMapNode(line.Text) { IsExpanded = true };
                    parent.Children.Add(node);
                    stack.Push((level, node));
                    currentTarget = node;
                }
                else
                {
                    currentTarget?.AppendBodyText(line.Text);
                }
            }

            if (!root.Children.Any())
            {
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line.Text)) continue;
                    root.Children.Add(new MindMapNode(line.Text) { IsExpanded = false });
                }
            }

            return root;
        }

        private IEnumerable<string> ExtractPdfFragments(string path)
        {
            string text;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(fs, Encoding.GetEncoding("ISO-8859-1")))
            {
                text = reader.ReadToEnd();
            }

            foreach (Match block in PdfBlockRegex.Matches(text))
            {
                var content = block.Groups["content"].Value;
                foreach (Match literal in PdfLiteralRegex.Matches(content))
                {
                    var decoded = DecodePdfLiteral(literal.Groups["txt"].Value);
                    if (!string.IsNullOrWhiteSpace(decoded))
                        yield return decoded;
                }
            }
        }

        private static string DecodePdfLiteral(string literal)
        {
            if (string.IsNullOrEmpty(literal)) return string.Empty;
            var sb = new StringBuilder(literal.Length);
            for (int i = 0; i < literal.Length; i++)
            {
                var ch = literal[i];
                if (ch == '\\' && i + 1 < literal.Length)
                {
                    i++;
                    var esc = literal[i];
                    switch (esc)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case '\\':
                        case '(':
                        case ')':
                            sb.Append(esc);
                            break;
                        default:
                            if (esc >= '0' && esc <= '7')
                            {
                                var oct = esc.ToString();
                                for (int k = 0; k < 2 && i + 1 < literal.Length; k++)
                                {
                                    var next = literal[i + 1];
                                    if (next < '0' || next > '7') break;
                                    i++;
                                    oct += next;
                                }
                                try
                                {
                                    sb.Append((char)Convert.ToInt32(oct, 8));
                                }
                                catch
                                {
                                    sb.Append(' ');
                                }
                            }
                            else
                            {
                                sb.Append(esc);
                            }
                            break;
                    }
                }
                else
                {
                    sb.Append(ch);
                }
            }
            return sb.ToString();
        }

        private sealed class SectionLine
        {
            public SectionLine(string text, int level, bool isHeading)
            {
                Text = text ?? string.Empty;
                Level = level;
                IsHeading = isHeading;
            }

            public string Text { get; }
            public int Level { get; set; }
            public bool IsHeading { get; set; }
        }

        private static int ClampInt(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static readonly Regex PdfBlockRegex = new Regex(@"BT(?<content>.*?)ET", RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex PdfLiteralRegex = new Regex(@"\((?<txt>(?:\\.|[^\\\)])*)\)", RegexOptions.Singleline | RegexOptions.Compiled);
    }
}
