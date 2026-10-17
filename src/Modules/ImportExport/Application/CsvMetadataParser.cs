using System.ComponentModel.DataAnnotations;
using System.Text;
using FlashAI.Modules.ImportExport.Contracts;

namespace FlashAI.Modules.ImportExport.Application;

public static class CsvMetadataParser
{
    public static ImportPreviewResultVm Parse(string fileName, string content)
    {
        var rows = new List<ImportPreviewRowVm>();
        var rowNumber = 1;

        try
        {
            var records = ParseRecords(content);
            var startIndex = records.Count > 0
                && records[0].FirstOrDefault()?.Trim().Equals("email", StringComparison.OrdinalIgnoreCase) == true
                ? 1
                : 0;

            for (var index = startIndex; index < records.Count; index++)
            {
                var fields = records[index];
                if (fields.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                var email = fields.ElementAtOrDefault(0)?.Trim() ?? string.Empty;
                var displayName = fields.ElementAtOrDefault(1)?.Trim() ?? string.Empty;
                var provider = fields.ElementAtOrDefault(2)?.Trim() ?? "openai";
                var plan = fields.ElementAtOrDefault(3)?.Trim() ?? "Chưa rõ";
                var tags = fields.ElementAtOrDefault(4)?.Trim() ?? string.Empty;
                var isValid = new EmailAddressAttribute().IsValid(email);
                rows.Add(new ImportPreviewRowVm(
                    rowNumber++,
                    email,
                    string.IsNullOrWhiteSpace(displayName) ? email.Split('@').FirstOrDefault() ?? email : displayName,
                    string.IsNullOrWhiteSpace(provider) ? "openai" : provider,
                    string.IsNullOrWhiteSpace(plan) ? "Chưa rõ" : plan,
                    tags,
                    isValid,
                    isValid ? null : "Định dạng email không hợp lệ"));
            }
        }
        catch (FormatException exception)
        {
            rows.Add(new ImportPreviewRowVm(1, string.Empty, string.Empty, "openai", "Chưa rõ", string.Empty, false, exception.Message));
        }

        return new ImportPreviewResultVm(
            fileName,
            rows.Count,
            rows.Count(row => row.IsValid),
            rows.Count(row => !row.IsValid),
            rows);
    }

    private static List<List<string>> ParseRecords(string content)
    {
        var records = new List<List<string>>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var index = 0; index < content.Length; index++)
        {
            var character = content[index];
            if (character == '"')
            {
                if (quoted && index + 1 < content.Length && content[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (character == ',' && !quoted)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (character == '\n' && !quoted)
            {
                fields.Add(field.ToString());
                field.Clear();
                records.Add(fields);
                fields = [];
            }
            else if (character != '\r' || quoted)
            {
                field.Append(character);
            }
        }

        if (quoted)
        {
            throw new FormatException("CSV có dấu ngoặc kép chưa đóng.");
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            records.Add(fields);
        }

        return records;
    }
}
