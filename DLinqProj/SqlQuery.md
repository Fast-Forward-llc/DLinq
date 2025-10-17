# SqlQuery Documentation

## Overview
`SqlQuery<T>` is a LINQ-enabled queryable class for building SQL queries and mutation statements in a type-safe manner. It is used with a `QueryProvider` and a SQL dialect to generate SQL statements from LINQ expressions. Supports both SQL Server and PostgreSQL dialects with feature parity.

## Construction
You typically obtain a `SqlQuery<T>` instance from a `QueryProvider`:
```csharp
var provider = new QueryProvider(new SqlServerDialect());
var query = new SqlQuery<Person>(provider);
```

## LINQ Operations
You can use standard LINQ methods to build queries:
```csharp
var query = new SqlQuery<Person>(provider)
    .Where(x => x.Age > 18)
    .OrderBy(x => x.Name)
    .Skip(2)
    .Take(10);
```
- Supported methods: `Where`, `OrderBy`, `OrderByDescending`, `ThenBy`, `ThenByDescending`, `Skip`, `Take`, and joins.

## SQL Generation
To get the SQL and parameters for a query:
```csharp
var query = new SqlQuery<Person>(provider).Where(x => x.Age > 18);
var (sql, parameters) = query.ToSql();
```
- `sql` is the generated SQL statement.
- `parameters` is an object suitable for Dapper parameterization.

## Mutation Operations
`SqlQuery<T>` also provides methods for generating mutation SQL:
```csharp
var (insertSql, insertParams) = query.ToInsertSql(new Person { Name = "Alice", Age = 30 });
var (updateSql, updateParams) = query.ToUpdateSql(new Person { Id = 1, Name = "Alice Smith", Age = 31 });
var (updateSql2, updateParams2) = query.ToUpdateSql(new Person { Age = 21 }, x => x.Age > 18);
var (deleteSql, deleteParams) = query.ToDeleteSql(x => x.Id == 1);
var (deleteSql2, deleteParams2) = query.ToDeleteSql(new Person { Id = 1 });
```
- These methods generate SQL for insert, update, and delete operations.
- Supports advanced predicates for WHERE clause and key-based deletes.

## Example Usage
```csharp
// Setup
var provider = new QueryProvider(new SqlServerDialect());

// Query for people older than 18
var query = new SqlQuery<Person>(provider).Where(x => x.Age > 18);
var (sql, parameters) = query.ToSql();
// Use with Dapper:
// var results = connection.Query<Person>(sql, parameters);

// Insert a new person
var (insertSql, insertParams) = query.ToInsertSql(new Person { Name = "Alice", Age = 30 });
// connection.Execute(insertSql, insertParams);

// Update a person
var (updateSql, updateParams) = query.ToUpdateSql(new Person { Id = 1, Name = "Alice Smith", Age = 31 });
// connection.Execute(updateSql, updateParams);

// Update with predicate
var (updateSql2, updateParams2) = query.ToUpdateSql(new Person { Age = 21 }, x => x.Age > 18);
// connection.Execute(updateSql2, updateParams2);

// Delete by predicate
var (deleteSql, deleteParams) = query.ToDeleteSql(x => x.Id == 1);
// connection.Execute(deleteSql, deleteParams);

// Delete by entity instance (key fields)
var (deleteSql2, deleteParams2) = query.ToDeleteSql(new Person { Id = 1 });
// connection.Execute(deleteSql2, deleteParams2);
```

## Notes
- `SqlQuery<T>` is not directly enumerable; use `.ToSql()` to get SQL for execution.
- It is designed for SQL generation and parameterization, not for in-memory LINQ execution.
- Use with Dapper or other ADO.NET libraries for data access.
- Mutation methods support both predicate-based and key-based operations for update and delete.
- Feature parity across SQL Server and PostgreSQL dialects.

---
For more details, see the source code and unit tests in the repository.

