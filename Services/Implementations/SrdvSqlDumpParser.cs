using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Extensions.Logging;
using PickNBook.Api.Services.Interfaces;

namespace PickNBook.Api.Services.Implementations
{
    public class SrdvSqlDumpParser : ISrdvSqlDumpParser
    {
        private readonly ILogger<SrdvSqlDumpParser> _logger;

        public SrdvSqlDumpParser(ILogger<SrdvSqlDumpParser> logger)
        {
            _logger = logger;
        }

        private static readonly Regex InsertHeaderRegex = new Regex(
            @"INSERT\s+INTO\s+[`""]?(?<tableName>[\w\.\-]+)[`""]?\s*\((?<columns>[^)]+)\)\s*VALUES",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public async IAsyncEnumerable<Dictionary<string, string?>> ParseInsertRowsAsync(
            string sqlFilePath,
            string expectedTableName,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (!File.Exists(sqlFilePath))
            {
                throw new FileNotFoundException("SQL dump file not found", sqlFilePath);
            }

            _logger.LogInformation("Parsing SQL dump for table '{Table}' from {Path}", expectedTableName, sqlFilePath);

            using var reader = new StreamReader(sqlFilePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 65536);

            string? line;
            bool insideTargetInsert = false;
            List<string> columns = new List<string>();
            var tupleBuffer = new StringBuilder();
            bool inString = false;
            bool isEscaped = false;
            bool insideTuple = false;

            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("--") || trimmed.StartsWith("/*"))
                {
                    continue;
                }

                if (trimmed.StartsWith("INSERT INTO", StringComparison.OrdinalIgnoreCase))
                {
                    var match = InsertHeaderRegex.Match(trimmed);
                    if (match.Success)
                    {
                        var tableName = match.Groups["tableName"].Value;
                        if (string.Equals(tableName, expectedTableName, StringComparison.OrdinalIgnoreCase))
                        {
                            insideTargetInsert = true;
                            columns.Clear();
                            var rawCols = match.Groups["columns"].Value;
                            foreach (var col in rawCols.Split(','))
                            {
                                var cleanCol = col.Trim().Trim('`', '"', '\'');
                                columns.Add(cleanCol);
                            }

                            // Everything after VALUES on the same line (if any)
                            var afterValuesIndex = trimmed.IndexOf("VALUES", StringComparison.OrdinalIgnoreCase) + 6;
                            if (afterValuesIndex < trimmed.Length)
                            {
                                line = trimmed.Substring(afterValuesIndex);
                            }
                            else
                            {
                                continue;
                            }
                        }
                        else
                        {
                            insideTargetInsert = false;
                            continue;
                        }
                    }
                    else if (trimmed.IndexOf(expectedTableName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        // Fallback line-spanning check if INSERT was wrapped
                        insideTargetInsert = true;
                    }
                    else
                    {
                        insideTargetInsert = false;
                        continue;
                    }
                }

                if (!insideTargetInsert)
                {
                    continue;
                }

                // Process characters in this line
                for (int i = 0; i < line.Length; i++)
                {
                    char c = line[i];

                    if (inString)
                    {
                        if (isEscaped)
                        {
                            tupleBuffer.Append(c);
                            isEscaped = false;
                        }
                        else if (c == '\\')
                        {
                            isEscaped = true;
                        }
                        else if (c == '\'')
                        {
                            // Check for SQL escaped quote ''
                            if (i + 1 < line.Length && line[i + 1] == '\'')
                            {
                                tupleBuffer.Append('\'');
                                i++; // Skip next quote
                            }
                            else
                            {
                                inString = false;
                            }
                        }
                        else
                        {
                            tupleBuffer.Append(c);
                        }
                        continue;
                    }

                    // Not inside a quoted string
                    if (c == '\'')
                    {
                        inString = true;
                        continue;
                    }

                    if (c == '(' && !insideTuple)
                    {
                        insideTuple = true;
                        tupleBuffer.Clear();
                        continue;
                    }

                    if (c == ')' && insideTuple)
                    {
                        insideTuple = false;
                        var rowValues = ParseTupleValues(tupleBuffer.ToString());
                        tupleBuffer.Clear();

                        if (rowValues.Count == columns.Count)
                        {
                            var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                            for (int colIdx = 0; colIdx < columns.Count; colIdx++)
                            {
                                dict[columns[colIdx]] = rowValues[colIdx];
                            }
                            yield return dict;
                        }
                        continue;
                    }

                    if (c == ';' && !insideTuple)
                    {
                        insideTargetInsert = false;
                        break;
                    }

                    if (insideTuple)
                    {
                        tupleBuffer.Append(c);
                    }
                }

                if (insideTuple)
                {
                    tupleBuffer.Append('\n');
                }
            }
        }

        private static List<string?> ParseTupleValues(string rawTuple)
        {
            var values = new List<string?>();
            var sb = new StringBuilder();
            bool inStr = false;
            bool escaped = false;

            for (int i = 0; i < rawTuple.Length; i++)
            {
                char c = rawTuple[i];

                if (inStr)
                {
                    if (escaped)
                    {
                        sb.Append(c);
                        escaped = false;
                    }
                    else if (c == '\\')
                    {
                        escaped = true;
                    }
                    else if (c == '\'')
                    {
                        if (i + 1 < rawTuple.Length && rawTuple[i + 1] == '\'')
                        {
                            sb.Append('\'');
                            i++;
                        }
                        else
                        {
                            inStr = false;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                else
                {
                    if (c == '\'')
                    {
                        inStr = true;
                    }
                    else if (c == ',')
                    {
                        var rawVal = sb.ToString().Trim();
                        values.Add(NormalizeSqlValue(rawVal));
                        sb.Clear();
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
            }

            var finalVal = sb.ToString().Trim();
            values.Add(NormalizeSqlValue(finalVal));

            return values;
        }

        private static string? NormalizeSqlValue(string raw)
        {
            if (string.Equals(raw, "NULL", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (raw.StartsWith("'") && raw.EndsWith("'") && raw.Length >= 2)
            {
                return raw.Substring(1, raw.Length - 2);
            }

            return raw;
        }
    }
}
