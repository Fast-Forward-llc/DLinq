# DLinqConnection Documentation

## Overview
`DLinqConnection` is a wrapper around ADO.NET `IDbConnection` that provides LINQ-style querying, Dapper integration, mutation operations, and robust transaction management. It supports SQL generation, entity retrieval by key(s), and can be easily unit tested using dependency injection and mocking. Supports both SQL Server and PostgreSQL dialects with feature parity.

## Constructor
```csharp
DLinqConnection(IDbConnection connection, ISqlDialect dialect)
DLinqConnection(IDbConnection connection, ISqlDialect dialect, IDapperProvider? dapperProvider)
```
- `connection`: The underlying database connection (e.g., `SqlConnection`, `NpgsqlConnection`).
- `dialect`: The SQL dialect implementation (e.g., `SqlServerDialect`, `PostgresDialect`).
- `dapperProvider` (optional): Wrapper for Dapper extension methods, useful for testing and abstraction.

## Key Methods
### Querying
```csharp
SqlQuery<T> Select<T>()
IEnumerable<T> Query<T>(Expression<Func<T, bool>> predicate)
IEnumerable<T> Query<T>(SqlQuery<T> sqlQuery)
```
Executes a SqlQuery expression based on type T and returns results using Dapper.

### Get by Single Key
```csharp
T? GetById<T, TKey>(TKey key, QueryOptions? options = null)
```
Retrieves an entity by its single key property. Pass `QueryOptions` to control schema resolution.

### Get by Composite Key
```csharp
T? GetById<T>(object keyValues, QueryOptions? options = null)
```
Retrieves an entity by composite key. Pass an anonymous object with properties matching the key fields. Pass `QueryOptions` to control schema resolution.

### Insert
```csharp
T? Insert<T>(T entity, InsertOptions? options = null)
```
Inserts an entity. If `options.SelectAfterMutation` is true, returns the inserted entity.

### Update
```csharp
T? Update<T>(T entity, UpdateOptions? options = null)
T? Update<T>(T entity, Expression<Func<T, bool>> predicate, UpdateOptions? options = null)
```
Updates an entity. If `options.SelectAfterMutation` is true, returns the updated entity.

### Delete
```csharp
int Delete<T>(Expression<Func<T, bool>> predicate)
int Delete<T>(T entity)
```
Deletes entities matching the predicate or by key fields of the entity instance.

## Schema Control with QueryOptions, InsertOptions, and UpdateOptions

`QueryOptions` is the base class for all operation options and provides two properties for controlling the database schema used in generated SQL:

| Property | Type | Description |
|---|---|---|
| `Schema` | `string?` | **Overrides** the schema for the table in all cases, including when the table name already contains a schema. |
| `DefaultSchema` | `string?` | Applies the schema **only when** the table name does not already include one. Has no effect if a schema is already present. |

`InsertOptions` and `UpdateOptions` both inherit from `QueryOptions` and add:

| Property | Type | Description |
|---|---|---|
| `SelectAfterMutation` | `bool` | When `true`, executes a `SELECT` after the mutation and returns the affected row. |
| `TableName` | `string?` | Overrides the table name derived from the entity type or `[Table]` attribute. |

### Schema override — `QueryOptions.Schema`
`Schema` unconditionally replaces any schema present in the resolved table name. Use this when you need to target a specific schema regardless of how the entity is mapped.

```csharp
// Always queries from the "reporting" schema, even if Person is mapped to "dbo.Person"
var person = dlinq.GetById<Person, int>(42, new QueryOptions { Schema = "reporting" });
// SELECT ... FROM "reporting"."Person" WHERE ...
```

### Default schema — `QueryOptions.DefaultSchema`
`DefaultSchema` is applied only when the table name has no schema component. Use this as a fallback schema when entities are not explicitly schema-qualified.

```csharp
// Applies "app" schema only because Person has no schema in its mapping
var inserted = dlinq.Insert(new Person { Name = "Alice" },
    new InsertOptions { DefaultSchema = "app", SelectAfterMutation = true });
// INSERT INTO "app"."Person" (...) VALUES (...) RETURNING *;

// Schema is NOT changed here because "dbo.Person" already has a schema
var updated = dlinq.Update(new Person { Id = 1, Name = "Bob" },
    new UpdateOptions { DefaultSchema = "app", SelectAfterMutation = true });
// UPDATE "dbo"."Person" SET ... (DefaultSchema ignored — schema already present)
```

### Combining Schema and SelectAfterMutation
```csharp
var options = new InsertOptions { Schema = "staging", SelectAfterMutation = true };
var result = dlinq.Insert(new Person { Name = "Carol" }, options);
// INSERT INTO "staging"."Person" (...) VALUES (...) RETURNING *;
```

## Transaction Management
```csharp
IDbTransaction BeginTransaction()
IDbTransaction BeginTransaction(IsolationLevel isolationLevel)
void Commit()
void Rollback()
```
- `BeginTransaction`: Begins a transaction and increments the internal transaction depth counter. Supports nested transactions by counting depth.
- `BeginTransaction(IsolationLevel)`: Begins a transaction with the specified isolation level and increments the depth counter. Also supports nested transactions.
- `Commit`: Commits the current transaction if one exists and decrements the transaction depth counter. Committing a null transaction has no effect.
- `Rollback`: Rolls back the current transaction, resets the transaction depth counter to zero, and nullifies the transaction reference. Rolling back a null transaction throws `InvalidOperationException`.

## Example Usage
### Setup
```csharp
using DLinq;
using Microsoft.Data.SqlClient;

var connection = new SqlConnection("your-connection-string");
var dialect = new SqlServerDialect();
var dlinq = new DLinqConnection(connection, dialect);
```

### Transaction Example
```csharp
using (var dlinq = new DLinqConnection(connection, dialect))
{
    dlinq.BeginTransaction();
    try
    {
        dlinq.Insert<Person>(new Person { Name = "Alice" });
        dlinq.Commit();
    }
    catch
    {
        dlinq.Rollback();
        throw;
    }
}
```

### Insert with SelectAfterMutation
```csharp
var options = new Options { SelectAfterMutation = true };
var inserted = dlinq.Insert<Person>(new Person { Name = "Bob" }, options);
```

### Update Example
```csharp
[Table("People")]
public class Person
{
    [Key]
    public int Id { get; set; }
    public string Name { get; set; }
    public int Age { get; set; }
}

// Update a person's name and age
var updated = dlinq.Update<Person>(new Person { Id = 42, Name = "Alice Smith", Age = 30 });
```
If the entity has a `[Table]` attribute and `[Key]` attribute(s), DLinq will use those for SQL generation and key matching.

### Update with Predicate Example
```csharp
// Update all people older than 18
var updated = dlinq.Update<Person>(new Person { Age = 21 }, p => p.Age > 18);
```
Supports advanced predicates for WHERE clause.

### Delete Examples
#### Delete by Predicate
```csharp
// Delete all people older than 100
dlinq.Delete<Person>(x => x.Age > 100);
```
Deletes all `Person` entities matching the predicate. Returns the number of affected rows.

#### Delete by Key (Single Key)
```csharp
// Delete a person by Id
dlinq.Delete<Person>(x => x.Id == 42);
```
Deletes the `Person` entity with `Id` 42.

#### Delete by Composite Key
```csharp
// For an entity with composite keys (e.g., FirstName and LastName)
dlinq.Delete<Person>(x => x.FirstName == "John" && x.LastName == "Doe");
```
Deletes the `Person` entity with the specified composite key values.

#### Delete by Entity Instance
```csharp
// Delete a person by entity instance (using key fields)
var person = new Person { Id = 42, Name = "Alice Smith", Age = 30 };
int affectedRows = dlinq.Delete<Person>(person);
```
Deletes the `Person` entity matching the key fields of the provided instance. Returns the number of affected rows.

// For composite key entities:
```csharp
var personCK = new PersonCK { Id = 7, LastName = "James", FirstName = "Alice", Age = 38 };
int affectedRows = dlinq.Delete<Person>(personCK);
```
Deletes the entity matching all key fields (e.g., `Id` and `LastName`).

### Query with LINQ-Style Predicate Expression
```csharp
// Query using a LINQ predicate and get results
var results = dlinq.Query<Person>(x => x.Age > 18).ToList();
```
Generates SQL from the LINQ-Style Predicate expression and executes it using Dapper.

### Query with SqlQuery
```csharp
var query = dlinq.From<Person>().OrderBy(x => x.Age).Skip(2).Take(5);
var results = dlinq.Query<Employee>(query).ToList();
```
Generates SQL from the composed query and executes it using Dapper.

---

## Entity2TableMapper

`Entity2TableMapper` is a settable property on `DLinqConnection` that allows custom mapping of entity types to table names at runtime. Assign a function to override the default table name resolution for any entity type.

```csharp
Func<Type, string, string>? Entity2TableMapper { get; set; }
```

- **P1** (`Type`): The entity type being resolved.
- **P2** (`string`): The table name already resolved from the `[Table]` attribute or entity class name, including any schema if present.
- **Returns** (`string`): Your custom table name. Return `null` to fall back to the input table name.

The mapper runs before `Schema` / `DefaultSchema` options are applied, so schema options still take effect after custom mapping.

### Example
```csharp
var dlinq = new DLinqConnection(connection, new PostgresDialect());

// Redirect all queries for Person to a tenant-specific table
dlinq.Entity2TableMapper = (type, tableName) =>
    type == typeof(Person) ? "tenant_42.people" : null;

// Generates: SELECT ... FROM "tenant_42"."people" WHERE ...
var results = dlinq.Query<Person>(x => x.Age > 18).ToList();
```

---

**Notes:**
- The `Delete` and `Update` methods support advanced predicates and key-based operations to avoid accidental full-table changes.
- SQL generation and mutation features are consistent across SQL Server and PostgreSQL dialects.
- `InsertOptions` / `UpdateOptions` allow specifying table name, schema, and mutation return behavior.
- `QueryOptions.Schema` overrides the schema unconditionally; `QueryOptions.DefaultSchema` applies only when no schema is already present.
- Transaction management is built-in and implicit — all operations on the connection automatically participate in the active transaction.
- Unit testing is supported via dependency injection and mocking of `IDapperProvider`.

For more details, see the source code and unit tests in the repository.
