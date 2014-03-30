using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

using Bind.Structures;

namespace Bind
{
    class DocProcessor
    {
        static readonly Regex remove_mathml = new Regex(
            @"<(mml:math|inlineequation)[^>]*?>(?:.|\n)*?</\s*\1\s*>",
            RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnorePatternWhitespace);
        static readonly Regex remove_doctype = new Regex(
            @"<!DOCTYPE[^>\[]*(\[.*\])?>", RegexOptions.Compiled | RegexOptions.Multiline);
        static readonly Regex remove_xmlns = new Regex(
            "xmlns=\".+\"", RegexOptions.Compiled);

        Documentation Cached;
        string LastFile;

        // Strips MathML tags from the source and replaces the equations with the content
        // found in the <!-- eqn: :--> comments in the docs.
        // Todo: Some simple MathML tags do not include comments, find a solution.
        // Todo: Some files include more than 1 function - find a way to map these extra functions.
        public Documentation ProcessFile(string file)
        {
            string text;

            if (LastFile == file)
                return Cached;

            LastFile = file;
            text = File.ReadAllText(file);

            text = text
                .Replace("&epsi;", "epsilon") // Fix unrecognized &epsi; entities
                .Replace("xml:", String.Empty); // Remove namespaces
            text = remove_doctype.Replace(text, String.Empty);
            text = remove_xmlns.Replace(text, string.Empty);

            Match m = remove_mathml.Match(text);
            while (m.Length > 0)
            {
                string removed = text.Substring(m.Index, m.Length);
                text = text.Remove(m.Index, m.Length);
                int equation = removed.IndexOf("eqn");
                if (equation > 0)
                {
                    // Find the start and end of the equation string
                    int eqn_start = equation + 4;
                    int eqn_end = removed.IndexOf(":-->") - equation - 4;
                    if (eqn_end < 0)
                    {
                        // Note: a few docs from man4 delimit eqn end with ": -->"
                        eqn_end = removed.IndexOf(": -->") - equation - 4;
                    }
                    if (eqn_end < 0)
                    {
                        Console.WriteLine("[Warning] Failed to find equation for mml.");
                        goto next;
                    }

                    string eqn_substring = removed.Substring(eqn_start, eqn_end);
                    text = text.Insert(m.Index, "<![CDATA[" + eqn_substring + "]]>");
                }

            next:
                m = remove_mathml.Match(text);
            }

            XDocument doc = null;
            try
            {
                doc = XDocument.Parse(text);
                Cached = ToInlineDocs(doc);
                return Cached;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                Console.WriteLine(doc.ToString());
                return null;
            }
        }

        Documentation ToInlineDocs(XDocument doc)
        {
            var inline = new Documentation
            {
                Summary =
                    Cleanup(
                        ((IEnumerable)doc.XPathEvaluate("/refentry/refnamediv/refpurpose"))
                        .Cast<XElement>().First().Value),
                Parameters =
                    ((IEnumerable)doc.XPathEvaluate("/refentry/refsect1[@id='parameters']/variablelist/varlistentry"))
                    .Cast<XNode>()
                    .Select(p =>
                        new DocumentationParameter(
                                p.XPathSelectElement("term/parameter").Value.Trim(),
                            Cleanup(p.XPathSelectElement("listitem").Value)))
                    .ToList()
            };

            inline.Summary = Char.ToUpper(inline.Summary[0]) + inline.Summary.Substring(1);
            return inline;
        }

        static readonly char[] newline = new char[] { '\n' };
        static string Cleanup(string text)
        {
            return
                String.Join(" ", text
                    .Replace("\r", "\n")
                    .Split(newline, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim()).ToArray());
        }
    }
}
