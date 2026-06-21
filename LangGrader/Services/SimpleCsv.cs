using System.Text;

namespace LangGrader.Services;

public static class SimpleCsv
{
    public static List<string[]> Parse(string csv)
    {
        var rows = new List<string[]>();
        var currentRow = new List<string>();
        var currentField = new StringBuilder();

        var inQuotes = false;

        for (var i = 0; i < csv.Length; i++)
        {
            var c = csv[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < csv.Length && csv[i + 1] == '"')
                    {
                        currentField.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    currentField.Append(c);
                }

                continue;
            }

            if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                currentRow.Add(currentField.ToString());
                currentField.Clear();
            }
            else if (c == '\r')
            {
                // Ignore CR. LF will close the row.
            }
            else if (c == '\n')
            {
                currentRow.Add(currentField.ToString());
                currentField.Clear();

                if (currentRow.Any(v => !string.IsNullOrWhiteSpace(v)))
                {
                    rows.Add(currentRow.ToArray());
                }

                currentRow.Clear();
            }
            else
            {
                currentField.Append(c);
            }
        }

        currentRow.Add(currentField.ToString());

        if (currentRow.Any(v => !string.IsNullOrWhiteSpace(v)))
        {
            rows.Add(currentRow.ToArray());
        }

        return rows;
    }

    public static string Escape(string value)
    {
        value ??= "";

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }
}