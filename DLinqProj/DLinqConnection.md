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
Returns a LINQ-enabled queryable for an entity type, or executes a LINQ expression and returns results using Dapper.

### Get by Single Key
```csharp
T? GetById<T, TKey>(TKey key)
```
Retrieves an entity by its single key property.

### Get by Composite Key
```csharp
T? GetById<T>(object keyValues)
```
Retrieves an entity by composite key. Pass an anonymous object with properties matching the key fields.

### Insert
```csharp
T? Insert<T>(T entity, Options? options = null)
```
Inserts an entity. If `options.SelectAfterMutation` is true, returns the inserted entity.

### Update
```csharp
T? Update<T>(T entity, Options? options = null)
T? Update<T>(T entity, Expression<Func<T, bool>> predicate, Options? options = null)
```
Updates an entity. If `options.SelectAfterMutation` is true, returns the updated entity. Supports advanced predicates for WHERE clause.

### Delete
```csharp
int Delete<T>(Expression<Func<T, bool>> predicate, Options? options = null)
int Delete<T>(T entity, Options? options = null)
```
Deletes entities matching the predicate or by key fields of the entity instance. Supports advanced predicates for WHERE clause.

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
        dlinq.Insert(new Person { Name = "Alice" });
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
var inserted = dlinq.Insert(new Person { Name = "Bob" }, options);
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
var updated = dlinq.Update(new Person { Id = 42, Name = "Alice Smith", Age = 30 });
```
If the entity has a `[Table]` attribute and `[Key]` attribute(s), DLinq will use those for SQL generation and key matching.

### Update with Predicate Example
```csharp
// Update all people older than 18
var updated = dlinq.Update(new Person { Age = 21 }, p => p.Age > 18);
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
int affectedRows = dlinq.Delete(person);
```
Deletes the `Person` entity matching the key fields of the provided instance. Returns the number of affected rows.

// For composite key entities:
```csharp
var personCK = new PersonCK { Id = 7, LastName = "James", FirstName = "Alice", Age = 38 };
int affectedRows = dlinq.Delete(personCK);
```
Deletes the entity matching all key fields (e.g., `Id` and `LastName`).

### Query with LINQ Expression
```csharp
// Query using a LINQ predicate and get results
var results = dlinq.Query<Person>(x => x.Age > 18).ToList();
```
Generates SQL from the LINQ expression and executes it using Dapper.

### Query with SqlQuery
```csharp
var query = dlinq.Select<Person>().OrderBy(x => x.Age).Skip(2).Take(5).ToSqlQuery();
var results = dlinq.Query<Person>(query).ToList();
```
Generates SQL from the composed query and executes it using Dapper.

---

**Notes:**
- The `Delete` and `Update` methods support advanced predicates and key-based operations to avoid accidental full-table changes.
- SQL generation and mutation features are consistent across SQL Server and PostgreSQL dialects.
- The `Options` type allows specifying table name and mutation behavior.
- Transaction management is built-in.
- Unit testing is supported via dependency injection and mocking.

For more details, see the source code and unit tests in the repository.
