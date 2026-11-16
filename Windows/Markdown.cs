using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace LLMDuell
{
    /// <summary>
    /// Stellt die Antwort eines Modells zeilenweise dar: fett, kursiv und Code innerhalb einer Zeile,
    /// Überschriften als fetter Text, Codeblöcke in Festbreitenschrift. Mehr braucht ein Vergleich nicht.
    /// </summary>
    static class Markdown
    {
        static readonly FontFamily CodeFont = (FontFamily)Application.Current.Resources["CodeFont"];

        /// <param name="insideCode">Ob die Zeile in einem ```-Block liegt; wird für die nächste Zeile fortgeschrieben.</param>
        public static Paragraph Line(string line, ref bool insideCode)
        {
            var paragraph = new Paragraph { Margin = new Thickness(0) };
            line = line.TrimEnd('\r');
            string trimmed = line.TrimStart(' ');

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                insideCode = !insideCode;
                var fence = new Run(line) { FontFamily = CodeFont };
                fence.SetResourceReference(TextElement.ForegroundProperty, "Text3");
                paragraph.Inlines.Add(fence);
                return paragraph;
            }

            if (insideCode)
            {
                paragraph.Inlines.Add(new Run(line) { FontFamily = CodeFont });
                return paragraph;
            }

            if (trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                string title = trimmed.TrimStart('#').Trim();
                if (title.Length > 0)
                {
                    var bold = new Bold();
                    AddInlines(bold.Inlines, title);
                    paragraph.Inlines.Add(bold);
                    return paragraph;
                }
            }

            if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
            {
                string indent = line.Substring(0, line.Length - trimmed.Length);
                line = indent + "•" + trimmed.Substring(1);
            }

            AddInlines(paragraph.Inlines, line);
            return paragraph;
        }

        static void AddInlines(InlineCollection target, string text)
        {
            int plainStart = 0;
            int i = 0;

            void FlushPlain(int end)
            {
                if (end > plainStart) target.Add(new Run(text.Substring(plainStart, end - plainStart)));
            }

            while (i < text.Length)
            {
                char c = text[i];
                if (c == '`')
                {
                    int close = text.IndexOf('`', i + 1);
                    if (close > i + 1)
                    {
                        FlushPlain(i);
                        var code = new Run(text.Substring(i + 1, close - i - 1)) { FontFamily = CodeFont };
                        code.SetResourceReference(TextElement.BackgroundProperty, "ChipBg");
                        target.Add(code);
                        i = plainStart = close + 1;
                        continue;
                    }
                }
                else if (c == '*' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    int close = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                    if (close > i + 2)
                    {
                        FlushPlain(i);
                        var bold = new Bold();
                        AddInlines(bold.Inlines, text.Substring(i + 2, close - i - 2));
                        target.Add(bold);
                        i = plainStart = close + 2;
                        continue;
                    }
                }
                else if (c == '*' && i + 1 < text.Length && !char.IsWhiteSpace(text[i + 1]))
                {
                    int close = text.IndexOf('*', i + 1);
                    if (close > i + 1 && !char.IsWhiteSpace(text[close - 1]))
                    {
                        FlushPlain(i);
                        var italic = new Italic();
                        AddInlines(italic.Inlines, text.Substring(i + 1, close - i - 1));
                        target.Add(italic);
                        i = plainStart = close + 1;
                        continue;
                    }
                }
                i++;
            }
            FlushPlain(text.Length);
        }
    }
}
