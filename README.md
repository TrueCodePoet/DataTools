# DataTools
Collection of helpful Data tools


## SyncFramework - Data Synchronization Helper

The `SyncFramework` is a part of the `DataHelper` namespace designed to facilitate data synchronization between two different `DbContext` instances. This can be particularly useful when syncing data between an on-premise database and a cloud-based database.

### Features:

1. **OnPremiseToCloud**: A method to initiate the data synchronization process from a source to a destination context.
2. **ProcessEntities**: A generic method to process and synchronize entities between the source and destination contexts.
3. **GetPrimaryKeyName**: A utility to fetch the primary key name of a given entity type.
4. **GetTableName**: A utility to get the table name of a given entity type.
5. **CopyProperties**: A method to copy properties from a source entity to a target entity.
6. **EntitiesAreEqual**: A utility to check if two entities are equal based on their properties.


### Example JSON Configuration for `SyncConfig`

```json
{
  "OnlyTheseTables": ["Table1", "Table2"],
  "IgnoreTheseTables": ["LogTable", "AuditTable"],
  "NewRecordsOnlyTables": ["RecentChanges"],
  "SetDefaultsIfMissing": {
    "*": {
      "User_ID": ""
    },
    "Table1": {
      "Column1": "DefaultValue1",
      "Column2": "DefaultValue2"
    }
  },
  "TableMappings": [
    {
      "SourceTableName": "LocalName",
      "TargetTableName": "CloudName"
    }
  ]
}
```

### Example Use:

Suppose you have two `DbContext` instances - `onPremiseContext` and `cloudContext`, and you wish to synchronize data from the on-premise database to the cloud database.

```csharp
using DataHelper;

// Create an instance of the SyncFramework
var syncFramework = new SyncFramework();
syncFramework.OnPremiseToCloud(sourceContext, destinationContext, "path/to/config.json");

```

### Example 2: Synchronizing New Records Only

In `config.json`, specify tables in `NewRecordsOnlyTables` to handle new records specifically.

### Example 3: Table Mapping Configuration

Define mappings in `config.json` under `TableMappings` to synchronize data between tables with different names.

### Example 4: Setting Default Values for Missing Fields

Specify `SetDefaultsIfMissing` in `config.json` to provide default values for specific fields when data is missing.

### Example 5: Executing Synchronization with Table Exclusions

Configure `IgnoreTheseTables` in `config.json` to exclude certain tables from the synchronization process.


# `DbContextDifferences` Code Description and Usage Examples

The provided code defines a namespace `TTCMoveFilesToCloud.DataHelper` containing a class `DbContextDiff` with nested classes and methods designed to identify and script differences between two Entity Framework Core database contexts. Here's a detailed breakdown of its components and functionality:

## `DbContextDifferences` Class

This nested class serves as a container for the differences found between two database contexts. It includes properties to store:
- Tables that exist in the source but not in the destination database, and vice versa.
- Missing table descriptions to detail tables not found in one of the contexts.
- Columns that exist only in the source or the destination tables.
- Differences in column types between the two contexts.
- Missing primary keys and default values in tables.
- Columns that are not null in the destination context but are nullable in the source, requiring manual review.

### `TypeDiff` Nested Class

A subclass within `DbContextDifferences`, representing the type differences between matching columns in the source and destination contexts. It includes the source type, destination type, and a description of the difference.

## `GenerateSqlScript` Method

A static method that generates a SQL script to resolve the differences identified by comparing the source and destination contexts. It constructs SQL statements for:
- Creating missing tables in the destination context.
- Adding missing columns with their corresponding data types and default values.
- Adding missing primary keys.
- Altering columns to set them as NOT NULL if they are required in the destination context but not in the source.

## `CompareDbContexts` Method

A static method that compares two Entity Framework Core database contexts (source and destination) to identify differences in schema, such as missing tables, columns, column types, primary keys, and default values. It populates an instance of `DbContextDifferences` with these identified differences.

## Helper Methods for SQL Script Generation

- `GenerateCreateTableScript`: Generates a SQL statement for creating a table based on the schema defined in the source context.
- `GenerateAddColumnScript`: Produces a SQL command to add a missing column to a table in the destination context.
- `GenerateAddPrimaryKeyScript`: Creates a SQL statement to add a primary key constraint to a table.
- `GenerateAddDefaultValueScript`: Generates a SQL statement to add a default value constraint for a column.

## Summary

The `DbContextDiff` class and its methods offer a comprehensive tool for comparing two database contexts to identify schema differences and automatically generate the necessary SQL scripts to align the destination database schema with the source. This tool is particularly useful for database migrations, synchronization, or auditing purposes, where ensuring schema consistency across different environments is critical.

## Examples of Use

1. **Database Migration**: When migrating a database from one environment to another, use `CompareDbContexts` to identify schema differences and `GenerateSqlScript` to create the necessary SQL scripts to update the destination database schema.
   
2. **Schema Synchronization**: In continuous integration and continuous deployment (CI/CD) pipelines, automate schema synchronization between development, testing, and production environments by integrating these methods to detect and resolve schema discrepancies.

3. **Database Auditing**: Periodically audit your database schemas to ensure consistency across different environments. Use the output of `CompareDbContexts` to report on discrepancies and take corrective action using the generated SQL scripts.



### Example 1: Comparing Two Database Contexts

```csharp
using (var sourceContext = new SourceDbContext())
using (var destinationContext = new DestinationDbContext())
{
    var differences = DbContextDiff.CompareDbContexts(sourceContext, destinationContext);
    Console.WriteLine("Differences identified between source and destination contexts.");
}
```

### Example 2: Generating SQL Script for Schema Synchronization

```csharp
using (var sourceContext = new SourceDbContext())
using (var destinationContext = new DestinationDbContext())
{
    var differences = DbContextDiff.CompareDbContexts(sourceContext, destinationContext);
    string sqlScript = DbContextDiff.GenerateSqlScript(sourceContext, differences);
    Console.WriteLine("SQL Script for schema synchronization:");
    Console.WriteLine(sqlScript);
}
```

### Example 3: Handling Missing Tables

```csharp
using (var sourceContext = new SourceDbContext())
{
    var differences = new DbContextDiff.DbContextDifferences
    {
        TablesOnlyInSource = new List<string> { "MissingTableInDestination" }
    };
    
    string sqlScript = DbContextDiff.GenerateSqlScript(sourceContext, differences);
    Console.WriteLine("SQL Script to create missing tables:");
    Console.WriteLine(sqlScript);
}
```

### Example 4: Adding Missing Columns with Default Values

```csharp
using (var sourceContext = new SourceDbContext())
{
    var differences = new DbContextDiff.DbContextDifferences
    {
        ColumnsOnlyInSource = new Dictionary<string, List<string>>
        {
            { "ExistingTable", new List<string> { "NewColumn" } }
        },
        MissingDefaultValues = new Dictionary<string, Dictionary<string, object>>
        {
            { "ExistingTable", new Dictionary<string, object> { { "NewColumn", "DefaultValue" } } }
        }
    };

    string sqlScript = DbContextDiff.GenerateSqlScript(sourceContext, differences);
    Console.WriteLine("SQL Script to add missing columns with default values:");
    Console.WriteLine(sqlScript);
}
```

### Example 5: Updating Column Constraints and Types

```csharp
using (var sourceContext = new SourceDbContext())
{
    var differences = new DbContextDiff.DbContextDifferences
    {
        FieldsNotNullInDestination = new Dictionary<string, List<string>>
        {
            { "ExistingTable", new List<string> { "NullableColumnInSource" } }
        },
        ColumnTypeDifferences = new Dictionary<string, Dictionary<string, DbContextDiff.DbContextDifferences.TypeDiff>>
        {
            {
                "ExistingTable",
                new Dictionary<string, DbContextDiff.DbContextDifferences.TypeDiff>
                {
                    {
                        "ColumnWithDifferentType",
                        new DbContextDiff.DbContextDifferences.TypeDiff
                        {
                            SourceType = typeof(string),
                            DestinationType = typeof(int),
                            Description = "Type changed from string to int."
                        }
                    }
                }
            }
        }
    };

    string sqlScript = DbContextDiff.GenerateSqlScript(sourceContext, differences);
    Console.WriteLine("SQL Script to update column constraints and types:");
    Console.WriteLine(sqlScript);
}
```
