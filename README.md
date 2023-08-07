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

### Example Use:

Suppose you have two `DbContext` instances - `onPremiseContext` and `cloudContext`, and you wish to synchronize data from the on-premise database to the cloud database.

```csharp
using DataHelper;

// Create an instance of the SyncFramework
SyncFramework syncFramework = new SyncFramework();

// Path to the update tracking file (optional)
string updateTrackingFilePath = "path_to_tracking_file.txt";

// Begin synchronization
syncFramework.OnPremiseToCloud(onPremiseContext, cloudContext, updateTrackingFilePath);
