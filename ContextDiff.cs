using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using System;
using System.Buffers.Text;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.SqlTypes;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection.Metadata;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

namespace DataHelper
{

    public class DbContextDiff
    {
        public class DbContextDifferences
        {
            public List<string> TablesOnlyInSource { get; set; } = new List<string>();
            public List<string> TablesOnlyInDestination { get; set; } = new List<string>();
            public Dictionary<string, string> MissingTableDescriptions { get; set; } = new Dictionary<string, string>();
            public Dictionary<string, List<string>> ColumnsOnlyInSource { get; set; } = new Dictionary<string, List<string>>();
            public Dictionary<string, List<string>> ColumnsOnlyInDestination { get; set; } = new Dictionary<string, List<string>>();
            public Dictionary<string, Dictionary<string, TypeDiff>> ColumnTypeDifferences { get; set; } = new Dictionary<string, Dictionary<string, TypeDiff>>();

            public Dictionary<string, string> MissingPrimaryKeys { get; set; } = new Dictionary<string, string>();
            public Dictionary<string, Dictionary<string, object>> MissingDefaultValues { get; set; } = new Dictionary<string, Dictionary<string, object>>();
            public Dictionary<string, List<string>> FieldsNotNullInDestination { get; set; } = new Dictionary<string, List<string>>();
            public Dictionary<string, List<string>> FieldsNeedManualReview { get; set; } = new Dictionary<string, List<string>>();

            public class TypeDiff
            {
                public Type SourceType { get; set; }
                public Type DestinationType { get; set; }
                public string Description { get; set; }
            }
        }

        public static string GenerateSqlScript(DbContext sourceContext, DbContextDifferences differences)
        {
            var sqlBuilder = new StringBuilder();

            foreach (var table in differences.TablesOnlyInSource)
            {
                sqlBuilder.AppendLine(GenerateCreateTableScript(sourceContext, table));
            }

            foreach (var table in differences.ColumnsOnlyInSource.Keys)
            {
                foreach (var column in differences.ColumnsOnlyInSource[table])
                {
                    var entityType = sourceContext.Model.GetEntityTypes().FirstOrDefault(e => e.GetTableName() == table);
                    var prop = entityType?.GetProperties().FirstOrDefault(p => p.Name == column);
                    if (prop != null)
                    {
                        sqlBuilder.AppendLine(GenerateAddColumnScript(table, column, prop.GetColumnType()));

                        // Handle default values
                        var defaultValue = prop.GetDefaultValueSql() ?? prop.GetDefaultValue()?.ToString();
                        if (defaultValue != null)
                        {
                            sqlBuilder.AppendLine(GenerateAddDefaultValueScript(table, column, defaultValue));
                        }
                    }
                }
            }

            foreach (var table in differences.MissingPrimaryKeys.Keys)
            {
                sqlBuilder.AppendLine(GenerateAddPrimaryKeyScript(table, differences.MissingPrimaryKeys[table]));
            }

            foreach (var table in differences.FieldsNotNullInDestination.Keys)
            {
                foreach (var column in differences.FieldsNotNullInDestination[table])
                {
                    var entityType = sourceContext.Model.GetEntityTypes().FirstOrDefault(e => e.GetTableName() == table);
                    var prop = entityType?.GetProperties().FirstOrDefault(p => p.Name == column);
                    if (prop != null)
                    {
                        var defaultValue = prop.GetDefaultValueSql() ?? prop.GetDefaultValue()?.ToString();
                        //if (defaultValue != null)
                        //{
                        //    // If a default value is available, set it
                        //    sqlBuilder.AppendLine(GenerateAddDefaultValueScript(table, column, defaultValue));
                        //}

                        // Add the NOT NULL constraint
                        sqlBuilder.AppendLine($"ALTER TABLE {table} ALTER COLUMN {column} {prop.GetColumnType()} NOT NULL;");

                        if (defaultValue == null)
                        {
                            // Append a comment to the SQL script notifying the user of the potential risks
                            sqlBuilder.AppendLine($"-- WARNING: {table}.{column} is set to NOT NULL. Ensure no existing records have NULL values for this column before running the script.");
                        }
                    }
                }
            }

            return sqlBuilder.ToString();
        }


        public static DbContextDifferences CompareDbContexts(DbContext sourceContext, DbContext destinationContext)
        {
            var result = new DbContextDifferences();

            var sourceEntityTypes = sourceContext.Model.GetEntityTypes()
                                                      .Where(e => !string.IsNullOrEmpty(e.GetTableName()))
                                                      .ToList();

            var destinationEntityTypes = destinationContext.Model.GetEntityTypes()
                                                              .Where(e => !string.IsNullOrEmpty(e.GetTableName()))
                                                              .ToList();


            // Check for tables and columns only in the source context
            foreach (var sourceEntityType in sourceEntityTypes)
            {
                var sourceTableName = sourceEntityType.GetTableName();
                var destinationEntityType = destinationEntityTypes.FirstOrDefault(e => e.GetTableName() == sourceTableName);

                if (destinationEntityType == null)
                {
                    result.TablesOnlyInSource.Add(sourceTableName);
                    result.MissingTableDescriptions[sourceTableName] = $"Table '{sourceTableName}' exists in source context but not in destination context.";
                }
                else
                {

                    foreach (var sourceColumn in sourceEntityType.GetProperties())
                    {
                        var destinationColumn = destinationEntityType.GetProperties().FirstOrDefault(p => p.Name == sourceColumn.Name);

                        if (destinationColumn == null)
                        {
                            if (!result.ColumnsOnlyInSource.ContainsKey(sourceTableName))
                            {
                                result.ColumnsOnlyInSource[sourceTableName] = new List<string>();
                            }

                            result.ColumnsOnlyInSource[sourceTableName].Add(sourceColumn.Name);
                        }
                        else if (sourceColumn.ClrType != destinationColumn.ClrType)
                        {
                            if (!result.ColumnTypeDifferences.ContainsKey(sourceTableName))
                            {
                                result.ColumnTypeDifferences[sourceTableName] = new Dictionary<string, DbContextDifferences.TypeDiff>();
                            }

                            result.ColumnTypeDifferences[sourceTableName][sourceColumn.Name] = new DbContextDifferences.TypeDiff
                            {
                                SourceType = sourceColumn.ClrType,
                                DestinationType = destinationColumn.ClrType,
                                Description = $"Column '{sourceColumn.Name}' in table '{sourceTableName}' has type '{sourceColumn.ClrType.Name}' in source but '{destinationColumn.ClrType.Name}' in destination."
                            };
                        }
                        else
                        {
                            if (!sourceColumn.IsNullable && destinationColumn.IsNullable)
                            {
                                if (!result.FieldsNotNullInDestination.ContainsKey(sourceTableName))
                                {
                                    result.FieldsNotNullInDestination[sourceTableName] = new List<string>();
                                }
                                result.FieldsNotNullInDestination[sourceTableName].Add(sourceColumn.Name);
                            }
                        }
                    }


                }
            }

            // Check for tables and columns only in the destination context
            foreach (var destinationEntityType in destinationEntityTypes)
            {
                var destinationTableName = destinationEntityType.GetTableName();
                var sourceEntityType = sourceEntityTypes.FirstOrDefault(e => e.GetTableName() == destinationTableName);

                if (sourceEntityType == null)
                {
                    result.TablesOnlyInDestination.Add(destinationTableName);
                    result.MissingTableDescriptions[destinationTableName] = $"Table '{destinationTableName}' exists in destination context but not in source context.";
                }
                else
                {
                    foreach (var destinationColumn in destinationEntityType.GetProperties())
                    {
                        var sourceColumn = sourceEntityType.GetProperties().FirstOrDefault(p => p.Name == destinationColumn.Name);

                        if (sourceColumn == null)
                        {
                            if (!result.ColumnsOnlyInDestination.ContainsKey(destinationTableName))
                            {
                                result.ColumnsOnlyInDestination[destinationTableName] = new List<string>();
                            }

                            result.ColumnsOnlyInDestination[destinationTableName].Add(destinationColumn.Name);
                        }
                    }
                }
            }

            return result;
        }


        private static string GenerateCreateTableScript(DbContext sourceContext, string tableName)
        {
            var entityType = sourceContext.Model.GetEntityTypes().FirstOrDefault(e => e.GetTableName() == tableName);
            if (entityType == null)
                return null; // or throw exception

            // Start building the SQL script
            var sqlBuilder = new StringBuilder($"CREATE TABLE {tableName} (");

            foreach (var prop in entityType.GetProperties())
            {
                sqlBuilder.Append($"{prop.Name} {prop.GetColumnType()}, ");
            }

            // Remove trailing comma and space
            sqlBuilder.Remove(sqlBuilder.Length - 2, 2);

            // Add primary key if exists
            var primaryKey = entityType.FindPrimaryKey();
            if (primaryKey != null)
            {
                var primaryKeyColumns = string.Join(", ", primaryKey.Properties.Select(p => p.Name));
                sqlBuilder.Append($", PRIMARY KEY ({primaryKeyColumns})");
            }

            sqlBuilder.Append(");");



            return sqlBuilder.ToString();
        }

        private static string GenerateAddColumnScript(string tableName, string columnName, string columnType)
        {
            return $"ALTER TABLE {tableName} ADD {columnName} {columnType};";
        }

        public static string GenerateAddPrimaryKeyScript(string tableName, string primaryKeyColumn)
        {
            string constraintName = $"PK_{tableName}";
            return $"ALTER TABLE {tableName} ADD CONSTRAINT {constraintName} PRIMARY KEY ({primaryKeyColumn});";
        }


        public static string GenerateAddDefaultValueScript(string tableName, string columnName, object defaultValue)
        {
            // Assuming the default value is a string or number; adjust if other data types need special handling.
            string constraintName = $"DF_{tableName}_{columnName}";
            return $"ALTER TABLE {tableName} ADD CONSTRAINT {constraintName} DEFAULT {defaultValue} FOR {columnName};";
        }
    }
}
