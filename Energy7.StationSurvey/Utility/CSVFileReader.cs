using Microsoft.VisualBasic.FileIO;
using System;
using System.Data;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Energy7.StationSurvey.Utility
{
    public class CSVFileReader
    {
        public DataTable ReadFile(string filePath)
        {
            if (!File.Exists(filePath)) return new DataTable();
            using var reader = new StreamReader(filePath);
            return ReadWithSpecialChar(reader.ReadToEnd(), ",");
        }

        public DataTable ReadWithSpecialChar(string csvString, params string[] delimiters)
        {
            var dataTable = new DataTable();
            using var parser = new TextFieldParser(
                new MemoryStream(Encoding.UTF8.GetBytes(csvString)))
            {
                HasFieldsEnclosedInQuotes = true,
                TrimWhiteSpace = true
            };
            parser.SetDelimiters(delimiters);

            bool firstLine = true;
            try
            {
                while (!parser.EndOfData)
                {
                    var fields = parser.ReadFields();
                    if (fields == null) continue;
                    if (firstLine)
                    {
                        foreach (var col in fields) dataTable.Columns.Add(col);
                        firstLine = false;
                    }
                    else
                    {
                        dataTable.Rows.Add(fields);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("CSV parse error: " + ex.Message, ex);
            }
            return dataTable;
        }

        public DataTable RemoveEscapeSequences(DataTable dataTable)
        {
            if (dataTable?.Columns == null || dataTable.Columns.Count == 0) return dataTable!;
            foreach (DataColumn col in dataTable.Columns)
                col.ColumnName = Regex.Unescape(col.ColumnName);
            return dataTable;
        }
    }
}
