using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;


namespace DataHelper
{
    public class SyncFramework
    {
        public void OnPremiseToCloud(
            DbContext sourceContext,
            DbContext destinationContext,
            string UpdateTracking_FilePath = "")
        {
            // Get all DbSet properties from source context
            var sourceDbSets = sourceContext.GetType().GetProperties()
                .Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>));

            // Get all DbSet properties from destination context for checking
            var destinationDbSets = destinationContext.GetType().GetProperties()
                .Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
                .Select(p => p.PropertyType.GetGenericArguments()[0].Name)
                .ToList();

            //  Order the list based on the last updated date this will focus on the oldest data  being refreshed first
            destinationDbSets = TableTracker.ReorderTablesBasedOnLastUpdated(destinationDbSets, UpdateTracking_FilePath);

            // Iterate through each DbSet in source context
            foreach (var sourceDbSet in sourceDbSets)
            {
                var sourceType = sourceDbSet.PropertyType.GetGenericArguments()[0];
                var sourceTypeName = sourceDbSet.PropertyType.GetGenericArguments()[0].Name;

                // Check if the type name exists in destination context
                if (destinationDbSets.Contains(sourceTypeName))
                {
                    // Find the corresponding target type in the destination context based on the name.
                    var targetType = destinationContext.GetType().GetProperties()
                        .Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
                        .Select(p => p.PropertyType.GetGenericArguments()[0])
                        .FirstOrDefault(t => t.Name == sourceTypeName);


                    // Use reflection to call ProcessEntities with the sourceType and targetType
                    MethodInfo method = GetType().GetMethod(nameof(ProcessEntities), BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
                    MethodInfo generic = method.MakeGenericMethod(sourceType, targetType);

                    
                    generic.Invoke(this, new object[] { sourceContext, destinationContext });
                    TableTracker.WriteOrUpdateLastUpdated(sourceTypeName, UpdateTracking_FilePath);
                }

            }
        }
        public static void ProcessEntities<TSource, TTarget>(
            DbContext sourceContext,
            DbContext destinationContext)
            where TSource : class
            where TTarget : class
        {
            int pageSize = 500;
            int totalEntities = 0;

            totalEntities = sourceContext.Set<TSource>().Count();

            int totalPages = (totalEntities + pageSize - 1) / pageSize;

            Stopwatch stopwatch = new Stopwatch();
            Stopwatch saveStopwatch = new Stopwatch();

            // Create a dynamic lambda expression for ordering
            var parameter = Expression.Parameter(typeof(TSource), "x");
            var primaryKeyPropertyName = GetPrimaryKeyName(sourceContext, typeof(TSource));

            var property = Expression.Property(parameter, primaryKeyPropertyName);
            Expression convertedProperty = Expression.Convert(property, typeof(object));
            var lambda = Expression.Lambda<Func<TSource, object>>(convertedProperty, parameter);

            string targetTableName = GetTableName<TTarget>(destinationContext);

            var entityTypeOfTTarget = destinationContext.Model.FindEntityType(typeof(TTarget));
            bool hasIdentityColumn = entityTypeOfTTarget.GetProperties()
                               .Any(p => p.ClrType == typeof(int) && p.IsKey() && p.ValueGenerated == ValueGenerated.OnAdd);

            destinationContext.Database.OpenConnection();
            try
            {
                if (hasIdentityColumn)
                {
                    destinationContext.Database.ExecuteSqlRaw($"SET IDENTITY_INSERT {targetTableName} ON");
                }

                for (int page = 0; page < totalPages; page++)
                {
                    stopwatch.Restart();

                    var sourceBatch = sourceContext.Set<TSource>().AsNoTracking()
                                      .OrderBy(lambda)
                                      .Skip(page * pageSize)
                                      .Take(pageSize)
                                      .ToList();

                    // Define the key selector lambda for TTarget
                    var targetParameter = Expression.Parameter(typeof(TTarget), "y");
                    var targetProperty = Expression.Property(targetParameter, primaryKeyPropertyName);
                    var targetLambda = Expression.Lambda<Func<TTarget, object>>(Expression.Convert(targetProperty, typeof(object)), targetParameter);

                    // Fetch the corresponding records from the target database based on the primary key
                    // Convert the primary key values to strings

                    //var targetKeys = sourceBatch.Select(e => Convert.ToString(typeof(TSource).GetProperty(primaryKeyPropertyName).GetValue(e))).ToList();
                    var targetKeys = sourceBatch.Select(e => typeof(TSource).GetProperty(primaryKeyPropertyName).GetValue(e)).ToList();

                    // Create an expression for accessing the primary key property on TTarget and converting it to a string
                    var targetKeyAccess = Expression.Call(Expression.Property(targetParameter, primaryKeyPropertyName), "ToString", null);

                    // Use the Contains method for string comparison
                    //var containsExpression = Expression.Call(typeof(Enumerable), "Contains", new Type[] { typeof(string) }, Expression.Constant(targetKeys), targetKeyAccess);

                    int batchSize = 100;  // You can adjust this value based on your needs
                    var targetRecords = new Dictionary<string, TTarget>();

                    foreach (var batch in SplitList(targetKeys, batchSize))
                    {
                        string inClause;

                        // Check the type of the first key in the batch to determine if it's string or integer
                        if (batch[0] is string)
                        {
                            var stringKeys = batch.Select(k => $"'{k.ToString().Replace("'", "''")}'");
                            inClause = string.Join(", ", stringKeys);
                        }
                        else if (batch[0] is int)
                        {
                            var intKeys = batch.Select(k => Convert.ToString(k));
                            inClause = string.Join(", ", intKeys);
                        }
                        else
                        {
                            throw new InvalidOperationException("Unsupported primary key type.");
                        }

                        // Construct raw SQL query ;  I don't like this but due to errors this performs the fastest
                        var rawSql = $"SELECT * FROM {GetTableName<TTarget>(destinationContext)} WHERE {primaryKeyPropertyName} IN ({inClause})";

                        var currentBatchRecords = destinationContext.Set<TTarget>().FromSqlRaw(rawSql)
                            .ToDictionary(e => Convert.ToString(typeof(TTarget).GetProperty(primaryKeyPropertyName).GetValue(e)));

                        foreach (var record in currentBatchRecords)
                        {
                            targetRecords[record.Key] = record.Value;
                        }
                    }

                    int saveCount = 0;
                    int SaveOn = 20;
                    int updateCount = 0;
                    int insertCount = 0;

                    foreach (var sourceEntity in sourceBatch)
                    {
                        dynamic key = typeof(TSource).GetProperty(primaryKeyPropertyName).GetValue(sourceEntity);
                        if (targetRecords.ContainsKey($"{key}"))
                        {
                            var targetEntity = targetRecords[$"{key}"];
                            if (!EntitiesAreEqual<TSource, TTarget>(sourceEntity, targetEntity))
                            {
                                CopyProperties(sourceEntity, targetEntity);
                                destinationContext.Entry(targetEntity).State = EntityState.Modified;
                                updateCount++;
                                saveCount++;
                            }
                        }
                        else
                        {
                            var newEntity = Activator.CreateInstance<TTarget>();
                            CopyProperties(sourceEntity, newEntity);
                            destinationContext.Set<TTarget>().Add(newEntity);
                            insertCount++;
                            saveCount++;
                        }

                        if (SaveOn == 0 || saveCount >= SaveOn)
                        {
                            saveStopwatch.Restart();
                            destinationContext.SaveChanges();
                            saveCount = 0;
                            Console.WriteLine($"    Save Execution Time {TimeSpan.FromTicks(saveStopwatch.Elapsed.Ticks):h\\:mm\\:ss\\.fff}");
                        }
                    }
                    if (SaveOn != 0 && saveCount > 0)
                    {
                        saveStopwatch.Restart();
                        destinationContext.SaveChanges();
                        saveCount = 0;

                        Console.WriteLine($"    Save Execution Time {TimeSpan.FromTicks(saveStopwatch.Elapsed.Ticks):h\\:mm\\:ss\\.fff}");
                    }

                    stopwatch.Stop();

                    int batchesRemaining = totalPages - page - 1;
                    TimeSpan estimatedTimeRemaining = TimeSpan.FromTicks(stopwatch.Elapsed.Ticks * batchesRemaining);

                    Console.WriteLine($"{targetTableName} Processed batch {page + 1} of {totalPages} : Inserted:{insertCount} Updated: {updateCount} Estimated time remaining: {estimatedTimeRemaining:h\\:mm\\:ss}");
                }

                if (hasIdentityColumn)
                {
                    destinationContext.Database.ExecuteSqlRaw($"SET IDENTITY_INSERT {targetTableName} OFF");
                }

            }
            finally
            {
                destinationContext.Database.CloseConnection();
            }
        }

        public static string GetPrimaryKeyName(DbContext context, Type entityType)
        {
            var model = context.Model;
            var entity = model.FindEntityType(entityType);
            var primaryKey = entity.FindPrimaryKey();
            return primaryKey.Properties.Select(x => x.Name).FirstOrDefault();
        }

        public static string GetTableName<T>(DbContext context) where T : class
        {
            var model = context.Model;
            var entityTypes = model.GetEntityTypes();
            var entityTypeOfT = entityTypes.First(t => t.ClrType.Name == typeof(T).Name);
            return entityTypeOfT.GetTableName();
        }

        // Helper method to split the list into batches
        public static IEnumerable<List<T>> SplitList<T>(List<T> locations, int nSize = 30)
        {
            for (int i = 0; i < locations.Count; i += nSize)
            {
                yield return locations.GetRange(i, Math.Min(nSize, locations.Count - i));
            }
        }

        public static void CopyProperties<TSource, TTarget>(TSource source, TTarget target)
            where TSource : class
            where TTarget : class
        {
            if (source == null || target == null)
            {
                throw new ArgumentNullException("Source / Destination Objects are null");
            }

            // Get the type of each object
            Type sourceType = typeof(TSource);
            Type targetType = typeof(TTarget);

            // Loop through the properties of the target object
            foreach (PropertyInfo targetProperty in targetType.GetProperties())
            {
                // Get the source property that matches the target property based on property name and is readable
                PropertyInfo sourceProperty = sourceType.GetProperty(targetProperty.Name);

                // If a matching source property was found and the property is writeable
                if (sourceProperty != null && targetProperty.CanWrite)
                {
                    // Check that the source property can be read and both properties have the same type
                    if (sourceProperty.CanRead && sourceProperty.PropertyType == targetProperty.PropertyType)
                    {
                        // Copy the value from the source property to the target property
                        targetProperty.SetValue(target, sourceProperty.GetValue(source, null), null);
                    }
                }
            }
        }

        // method to check if two entities are equal based on their properties
        public static bool EntitiesAreEqual<TSource, TTarget>(TSource source, TTarget target)
            where TSource : class
            where TTarget : class
        {
            var sourceProperties = typeof(TSource).GetProperties();
            var targetProperties = typeof(TTarget).GetProperties();

            foreach (var sourceProperty in sourceProperties)
            {
                var targetProperty = targetProperties.SingleOrDefault(p => p.Name == sourceProperty.Name);
                if (targetProperty == null) continue;

                var sourceValue = sourceProperty.GetValue(source);
                var targetValue = targetProperty.GetValue(target);

                if (sourceValue is byte[] sourceBytes && targetValue is byte[] targetBytes)
                {
                    if (!sourceBytes.SequenceEqual(targetBytes))
                    {
                        return false;
                    }
                }
                else if (sourceValue != null && !sourceValue.Equals(targetValue))
                {
                    return false;
                }
                else if (sourceValue == null && targetValue != null)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
