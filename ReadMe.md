# DLinq: Dapper LINQ-to-SQL for .NET

## Overview
DLinq is a Dapper LINQ-to-SQL style library for .NET 8+, designed to simplify and accelerate database access in C# applications. It provides a type-safe, composable API for querying and mutating relational databases using LINQ-like expressions, with support for SQL Server and PostgreSQL. Use `SqlQuery` alone for SQL generation with your preferred data access technology.
This package has evolved from a true Linq-to-sql provider to a Linq-like provider in order to give an improved developer experience. One of the primary goals is to avoid Database as code. The developer defines the entity relationships as part of writing a DLinq query, a lot like in Sql. 
By not predefining the entity relationships queries are not forced to fit a mold and developers can write queries that are more natural and easier to read. This also allows for more flexibility in the types of queries that can be written, as developers are not limited by the constraints of a predefined model.
In essence DLinq queries are more like Sql queries and less like EF and Linq. 

If your looking for a library that feels more like Sql and less like defining DDL as code this might be the right fit for you.

## Key Features
- **LINQ-Style Fluent SQL Generation:** Write expressive queries using LINQ style syntax and generate efficient SQL for your database.
- **Mutation Operations:** Easily perform insert, update, and delete operations with automatic SQL generation and parameterization.
- **Advanced Predicate Support:** Use complex predicates reducing boilerplate and risk of full-table changes.
- **Transaction Management:** Implicit transactions. When a transaction is started, all operations using that connection are automatically included in the transaction. No need to pass around transactions or keep track of them.
- **Dapper Integration:** Seamless integration with Dapper for fast data access and mapping.
- **Attribute Based Mapping:** Use attributes to configure table and column mappings, key properties, and more.
- **Dialect Abstraction:** Feature parity for SQL Server and PostgreSQL, with dialect-specific SQL generation and quoting.
- **Unit Testing Friendly:** Mockable Dapper provider and dependency injection support for easy unit testing.

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
var inserted = dlinq.Insert(new Person { Name = "Alice", Age = 30 }, new Options { SelectAfterMutation = true });

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
