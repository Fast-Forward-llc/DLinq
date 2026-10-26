# DLinq: Dapper LINQ-to-SQL for .NET

## Overview
DLinq is a fluent Sql generator (.NET 8+) intended for use with Dapper. Although Dapper is the intended consumer the generated sql will work with ADO.Net or derivatives. It provides a type-safe fluent and composable API for querying and mutating relational databases using LINQ-like expressions, with support for SQL Server and PostgreSQL via database specific dialect classes. Use `SqlQuery` alone for SQL generation with your preferred data access technology or use DlinqConnection as a dapper abstraction.
This package has evolved from a true Linq-to-sql provider to a Linq-like provider to support the fluent syntax I prefer. One of the primary goals is to avoid Database as code. Like Sql the developer defines the entity relationships as part of writing a DLinq query, using a Sql like syntax. 
Dlinq includes dynamic predicate builders that accept typed criteria or Json. Works side by side with EF or plain dapper or most other data access technology.

## Database First or Code First
DLinq is not opinionated about how you define your database schema. You can use it in a database-first approach, where you generate your entity classes from an existing database schema, or in a code-first approach, where you define your entity classes and let DLinq generate the necessary SQL for you.
If choosing a Code First approach use the tool of your choice to define and manage the schema independantly of DLinq. As long as the queries are valid for the schema it'll work. 

## Key Features
- **LINQ-Style Fluent SQL Generation:** Write expressive queries using LINQ style syntax and generate efficient SQL for your database.
- **Mutation Operations:** Easily perform insert, update, and delete operations with automatic SQL generation and parameterization.
- **Advanced Predicate Support:** Use complex predicates with type safety and reducing boilerplate. Queries are reusable.
- **Schema Control:** Use `QueryOptions.Schema` to unconditionally override the schema for any operation, or `QueryOptions.DefaultSchema` to apply a fallback schema only when the entity has no schema defined. Available on all query and mutation operations.
- **Transaction Management:** Implicit transactions. When a transaction is started, all operations using that connection are automatically included in the transaction. No need to pass around transactions or keep track of them.
- **Dapper Integration:** Seamless integration with Dapper for fast data access and mapping.
- **Attribute Based Mapping:** Use attributes to configure table and column mappings, key properties, and more. Override mappings with QueryOptions and hooks.
- **Dialect Abstraction:** Feature parity for SQL Server and PostgreSQL, with injectable dialect-specific SQL generation. 
- **Unit Testing Friendly:** Mockable Dapper provider and dependency injection support for easy unit testing. Assert on query shape and return mock results without using in-memory databases.

## Example Usage
```csharp
using DLinq;
using Microsoft.Data.SqlClient;

var connection = new SqlConnection("your-connection-string");
var dlinq = new DLinqConnection(connection, new SqlServerDialect());

// Query with LINQ predicate
var adults = dlinq.Query<Person>(x => x.Age > 18).ToList();

// Query with SqlQuery
var query = dlinq.QueryBuilder<Person>().OrderBy(x => x.Age).Skip(2).Take(5);
var results = dlinq.Query<Person>(query).ToList();

// Query Person and project to Employee (only matching properties will be mapped)
var query = dlinq.From<Person>().OrderBy(x => x.Age).Skip(2).Take(5);
var results = dlinq.Query<Employee>(query).ToList();

// Query Person and project to Employee specifying projection with 'Select' 
var query = dlinq.From<Person>().OrderBy(x => x.Age).Skip(2).Take(5)
    .Select(p => new Employee { FullName = p.Name, Age = p.Age });
var results = dlinq.Query<Employee>(query).ToList();

// Insert
var inserted = dlinq.Insert(new Person { Name = "Alice", Age = 30 }, new InsertOptions { SelectAfterMutation = true });

// Update with predicate
var updated = dlinq.Update(new Person { Age = 21 }, p => p.Age > 18);

// Delete by entity
int affectedRows = dlinq.Delete(inserted);

// Delete by predicate
int affectedRows2 = dlinq.Delete<Person>(x => x.Age > 100);

// Transaction
using (var tx = dlinq.BeginTransaction())
{
    dlinq.Insert(new Person { Name = "Bob" });
    dlinq.Commit();
}
```

## Getting Started
- See [DLinqConnection.md](./DLinqProj/DLinqConnection.md), [SqlQuery.md](./DLinqProj/SqlQuery.md), and [DataAnnotations.md](./DataAnnotations.md) for API documentation and advanced usage.
- Integration tests for both SQL Server and PostgreSQL are provided in the `DLinqIntegrationTests` project.
- Unit tests and contract tests are available in the `DLinqTests` project.

## License
This project is licensed under the MIT License. See the LICENSE file in the repository for details.
