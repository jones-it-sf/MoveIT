using System.Text;

namespace MoveIT.Core.Util;

/// <summary>A small RFC 4180 CSV reader: quoted fields, embedded commas and quotes, CRLF or LF, and a leading BOM.</summary>
public static class CsvReader
{
    public static List<string[]> Parse(string text)
    {
        var rows = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;
        bool rowHasContent = false;

        if (text.StartsWith('\uFEFF'))
        {
            text = text[1..];
        }

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    rowHasContent = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    rowHasContent = true;
                    break;
                case '\r':
                    break;
                case '\n':
                    EndRow();
                    break;
                default:
                    field.Append(c);
                    rowHasContent = true;
                    break;
            }
        }

        EndRow();
        return rows;

        void EndRow()
        {
            if (!rowHasContent && field.Length == 0 && fields.Count == 0)
            {
                return;
            }

            fields.Add(field.ToString());
            rows.Add([.. fields]);
            fields.Clear();
            field.Clear();
            rowHasContent = false;
        }
    }
}
