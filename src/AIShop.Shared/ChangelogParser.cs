using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace AIShop.Shared
{
    public static class ChangelogParser
    {
        private static readonly Regex HeaderRegex = new Regex(
            @"^===\s*(?<version>.+?)\s*\|\s*(?<date>\d{4}-\d{2}-\d{2})\s*===$",
            RegexOptions.Compiled);

        public static List<ChangelogEntry> Parse(string text)
        {
            var result = new List<ChangelogEntry>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return result;
            }

            ChangelogEntry current = null;
            var body = new StringBuilder();

            using (var reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var match = HeaderRegex.Match(line.Trim());
                    if (match.Success)
                    {
                        AddCurrent(result, current, body);
                        current = new ChangelogEntry
                        {
                            Version = match.Groups["version"].Value.Trim(),
                            Date = DateTime.Parse(match.Groups["date"].Value)
                        };
                        body.Clear();
                        continue;
                    }

                    if (current != null)
                    {
                        body.AppendLine(line);
                    }
                }
            }

            AddCurrent(result, current, body);
            return result;
        }

        private static void AddCurrent(ICollection<ChangelogEntry> result, ChangelogEntry current, StringBuilder body)
        {
            if (current == null)
            {
                return;
            }

            current.Body = body.ToString().Trim();
            result.Add(current);
        }
    }
}
