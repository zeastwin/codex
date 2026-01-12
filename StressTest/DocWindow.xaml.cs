using System;
using Markdig;
using System.IO;
using System.Text;
using System.Windows;

namespace StressTest
{
    /// <summary>
    /// 说明文档窗口
    /// </summary>
    public partial class DocWindow : Window
    {
        public DocWindow(string content)
        {
            InitializeComponent();
            var html = BuildHtml(content ?? string.Empty);
            ContentBrowser.NavigateToString(html);
        }

        public static DocWindow FromFile(string path)
        {
            var text = string.Empty;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                text = File.ReadAllText(path, Encoding.UTF8);

            return new DocWindow(text);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private static string BuildHtml(string markdown)
        {
            var pipeline = new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .Build();

            var body = Markdown.ToHtml(markdown ?? string.Empty, pipeline);
            var css = @"
body { font-family: 'HarmonyOS Sans SC','Microsoft YaHei UI',sans-serif; margin:0; padding:0; color:#0F172A; }
h1,h2,h3 { margin: 16px 0 8px; }
p { margin: 6px 0; line-height: 1.6; }
ul,ol { margin: 6px 0 6px 20px; }
code { background: #F1F5F9; padding: 2px 4px; border-radius: 4px; }
pre { background: #0B1220; color: #E2E8F0; padding: 10px; border-radius: 8px; overflow-x: auto; }
table { border-collapse: collapse; margin: 8px 0; }
th,td { border: 1px solid #E2E8F0; padding: 6px 8px; }
blockquote { border-left: 3px solid #CBD5E1; padding-left: 10px; color: #475569; margin: 8px 0; }
";

            var html = $@"<!DOCTYPE html>
<html>
<head>
<meta charset=""utf-8"">
<style>{css}</style>
</head>
<body>{body}</body>
</html>";

            return html;
        }
    }
}
