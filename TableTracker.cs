using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace DataTools
{
    internal class TableTracker
    {
        public class TableUpdateInfo
        {
            public string TableName { get; set; }
            public DateTime LastUpdated { get; set; }
        }

        public static void WriteOrUpdateLastUpdated(string tableName, string filePath)
        {
            List<TableUpdateInfo> tableUpdates;

            // Check if file exists
            if (File.Exists(filePath))
            {
                var content = File.ReadAllText(filePath);
                tableUpdates = JsonConvert.DeserializeObject<List<TableUpdateInfo>>(content) ?? new List<TableUpdateInfo>();

                var existingTableUpdate = tableUpdates.Find(t => t.TableName == tableName);
                if (existingTableUpdate != null)
                {
                    existingTableUpdate.LastUpdated = DateTime.UtcNow;
                }
                else
                {
                    tableUpdates.Add(new TableUpdateInfo { TableName = tableName, LastUpdated = DateTime.UtcNow });
                }
            }
            else
            {
                tableUpdates = new List<TableUpdateInfo> { new TableUpdateInfo { TableName = tableName, LastUpdated = DateTime.UtcNow } };
            }

            var serializedData = JsonConvert.SerializeObject(tableUpdates, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(filePath, serializedData);
        }

        public static string GetOldestUpdatedTable(string filePath)
        {
            if (!File.Exists(filePath)) return null;

            var content = File.ReadAllText(filePath);
            var tableUpdates = JsonConvert.DeserializeObject<List<TableUpdateInfo>>(content);

            if (tableUpdates == null || tableUpdates.Count == 0) return null;

            var oldestTable = tableUpdates.OrderBy(t => t.LastUpdated).First();
            return oldestTable.TableName;
        }

        public static List<string> ReorderTablesBasedOnLastUpdated(List<string> tableNames, string filePath)
        {
            // If the file doesn't exist, simply return the table names in alphabetical order
            if (!File.Exists(filePath))
            {
                return tableNames.OrderBy(t => t).ToList();
            }

            List<TableUpdateInfo> tableUpdates = new List<TableUpdateInfo>();
            var content = File.ReadAllText(filePath);
            tableUpdates = JsonConvert.DeserializeObject<List<TableUpdateInfo>>(content) ?? new List<TableUpdateInfo>();

            // Separate tables into "not yet run" and "already run" lists
            var notYetRunTables = tableNames.Except(tableUpdates.Select(t => t.TableName)).OrderBy(t => t).ToList();
            var alreadyRunTables = tableNames.Intersect(tableUpdates.Select(t => t.TableName)).ToList();

            // Order the "already run" tables by their LastUpdated date
            alreadyRunTables = alreadyRunTables.OrderBy(t =>
                tableUpdates.FirstOrDefault(u => u.TableName == t)?.LastUpdated ?? DateTime.MaxValue).ToList();

            // Merge the two lists
            notYetRunTables.AddRange(alreadyRunTables);

            return notYetRunTables;
        }

    }
}
