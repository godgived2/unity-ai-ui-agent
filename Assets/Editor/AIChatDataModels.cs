using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace MCPForUnity.Editor.AI
{
    public enum Role { User, Assistant, System }
    public enum RunState { Running, Complete, Failed }
    public enum MarkdownKind { Paragraph, Heading, Bullet, Quote, Code, Divider }

    [Serializable]
    public sealed class ChatMessage
    {
        public Role Role;
        public string RawText;
        public readonly List<MarkdownBlock> Blocks = new List<MarkdownBlock>();
        public readonly string Timestamp = DateTime.Now.ToString("HH:mm");
        public bool IsExpanded = true;

        public ChatMessage(Role role, string text)
        {
            Role = role;
            RawText = text ?? string.Empty;
            Blocks.AddRange(MarkdownParser.Parse(RawText));
        }

        public void AppendText(string extraText)
        {
            RawText += extraText;
            Blocks.Clear();
            Blocks.AddRange(MarkdownParser.Parse(RawText));
        }
    }

    [Serializable]
    public sealed class ToolRun
    {
        public string Name;
        public string Summary;
        public string Input;
        public string Output;
        public RunState State;
        public double Duration;
        public bool Expanded;
        public readonly string Time = DateTime.Now.ToString("HH:mm:ss");
    }

    [Serializable]
    public sealed class ChatSession
    {
        public string SessionId = Guid.NewGuid().ToString().Substring(0, 8);
        public string Title = "New Chat";
        public List<ChatMessage> Messages = new List<ChatMessage>();
        public string Model = "qwen3:14b";
        public DateTime CreatedAt = DateTime.Now;
    }

    public sealed class MarkdownBlock
    {
        public MarkdownKind Kind;
        public string Text;
        public string Language;
        public int Level;
    }

    public static class MarkdownParser
    {
        public static List<MarkdownBlock> Parse(string markdown)
        {
            var blocks = new List<MarkdownBlock>();
            string[] lines = (markdown ?? string.Empty).Replace("\r\n", "\n").Split('\n');
            bool inCode = false;
            string language = string.Empty;
            var code = new StringBuilder();
            var paragraph = new StringBuilder();

            Action flushParagraph = () =>
            {
                if (paragraph.Length > 0)
                {
                    blocks.Add(new MarkdownBlock { Kind = MarkdownKind.Paragraph, Text = paragraph.ToString().Trim() });
                    paragraph.Length = 0;
                }
            };

            foreach (string raw in lines)
            {
                string line = raw ?? string.Empty;
                if (line.StartsWith("```"))
                {
                    if (inCode)
                    {
                        blocks.Add(new MarkdownBlock { Kind = MarkdownKind.Code, Text = code.ToString().TrimEnd('\n'), Language = language });
                        code.Length = 0;
                        inCode = false;
                    }
                    else
                    {
                        flushParagraph();
                        inCode = true;
                        language = line.Substring(3).Trim();
                    }
                    continue;
                }

                if (inCode)
                {
                    code.AppendLine(line);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    flushParagraph();
                    continue;
                }

                int hashes = 0;
                while (hashes < line.Length && line[hashes] == '#') hashes++;

                if (hashes > 0 && hashes < line.Length && line[hashes] == ' ')
                {
                    flushParagraph();
                    blocks.Add(new MarkdownBlock { Kind = MarkdownKind.Heading, Level = hashes, Text = line.Substring(hashes + 1).Trim() });
                }
                else if (line == "---" || line == "***")
                {
                    flushParagraph();
                    blocks.Add(new MarkdownBlock { Kind = MarkdownKind.Divider });
                }
                else if (line.StartsWith("> "))
                {
                    flushParagraph();
                    blocks.Add(new MarkdownBlock { Kind = MarkdownKind.Quote, Text = line.Substring(2) });
                }
                else if (line.StartsWith("- ") || line.StartsWith("* "))
                {
                    flushParagraph();
                    blocks.Add(new MarkdownBlock { Kind = MarkdownKind.Bullet, Text = line.Substring(2) });
                }
                else
                {
                    if (paragraph.Length > 0) paragraph.Append('\n');
                    paragraph.Append(line);
                }
            }

            if (inCode)
                blocks.Add(new MarkdownBlock { Kind = MarkdownKind.Code, Text = code.ToString().TrimEnd('\n'), Language = language });

            flushParagraph();
            return blocks;
        }

        public static string RenderInlineMarkdown(string text)
        {
            string escaped = text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
            escaped = Regex.Replace(escaped, @"`([^`]+)`", "<color=#8BE9FD>$1</color>");
            escaped = Regex.Replace(escaped, @"\*\*([^*]+)\*\*", "<b>$1</b>");
            escaped = Regex.Replace(escaped, @"(?<!\*)\*([^*]+)\*(?!\*)", "<i>$1</i>");
            return escaped;
        }
    }
}