# DLinq: Dapper LINQ-to-SQL for .NET

## Overview
DLinq is a Dapper LINQ-to-SQL library for .NET 8, designed to simplify and accelerate database access in C# applications. It provides a type-safe, composable API for querying and mutating relational databases using LINQ expressions, with support for SQL Server and PostgreSQL. Use `SqlQuery` alone for SQL generation with your preferred data access technology.

## Key Features
- **LINQ-Enabled SQL Generation:** Write expressive queries using standard LINQ syntax and generate efficient SQL for your database.
- **Mutation Operations:** Easily perform insert, update, and delete operations with automatic SQL generation and parameterization.
- **Advanced Predicate Support:** Use complex predicates for WHERE clauses in update and delete operations, reducing boilerplate and risk of full-table changes.
- **Transaction Management:** Implicit transactions. When a transaction is started, all operations using that connection are automatically included in the transaction. No need to pass around transactions or keep track of them.
- **Dapper Integration:** Seamless integration with Dapper for fast data access and mapping.
- **Dialect Abstraction:** Feature parity for SQL Server and PostgreSQL, with dialect-specific SQL generation and quoting.
- **Unit Testing Friendly:** Mockable Dapper provider and dependency injection support for easy unit testing.

## How DLinq Improves Software Development
- **Productivity:** Write less boilerplate code for data access and mutations. Focus on business logic, not SQL syntax.
- **Safety:** Type-safe queries and mutations reduce runtime errors and SQL injection risks. Advanced predicate support helps prevent accidental mass updates/deletes.
- **Performance:** Efficient SQL generation and Dapper integration deliver high performance for both reads and writes.
- **Portability:** Easily switch between SQL Server and PostgreSQL with minimal code changes, thanks to dialect abstraction.
- **Testability:** Mockable data access and clear separation of concerns make unit testing straightforward.
- **Maintainability:** Centralized, composable data access logic makes code easier to read, maintain, and refactor.

## Example Usage
```csharp
using DLinq;
using Microsoft.Data.SqlClient;

var connection = new SqlConnection("your-connection-string");
var dlinq = new DLinqConnection(connection, new SqlServerDialect());

// Query with LINQ predicate
var adults = dlinq.Query<Person>(x => x.Age > 18).ToList();

// Query with SqlQuery
var query = dlinq.Select<Person>().OrderBy(x => x.Age).Skip(2).Take(5).ToSqlQuery();
var results = dlinq.Query<Person>(query).ToList();

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
