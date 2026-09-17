
# SqlQuery Documentation

## Overview
`SqlQuery<T>` is a composable, LINQ-style query builder for generating parameterized SQL statements in a type-safe manner. It is used with a `QueryProvider` and a SQL dialect (e.g., SQL Server, PostgreSQL) to translate LINQ-like expressions into SQL. It supports both query and mutation (insert, update, delete) operations, and is designed for use with Dapper or similar data access libraries.

## Construction
You typically create a `SqlQuery<T>` instance using a `QueryProvider`:
```
var provider = new QueryProvider(new SqlServerDialect());
var query = new SqlQuery<Person>(provider);
```

## Query Composition
You can chain LINQ-like methods to build queries:
```
var query = new SqlQuery<Person>(provider)
    .Where(x => x.Age > 18)
    .OrderBy(x => x.Name)
    .Skip(2)
    .Take(10);
```
- Supported methods: `Where`, `AndWhere`, `OrWhere`, `OrderBy`, `OrderByDescending`, `ThenBy`, `ThenByDescending`, `Skip`, `Take`, `Select`, and `Join`.
- `OrderBy` also has an overload accepting `IEnumerable<OrderBy>` for dynamically building sort criteria from column name/direction pairs at runtime — see [Dynamic Sorting with OrderBy(IEnumerable&lt;OrderBy&gt;)](#dynamic-sorting-with-orderbyienumerableorderby).
- All lambda parameters must be expressions (e.g., `Expression<Func<T, TResult>>`), not delegates.

## Filtering with Where, AndWhere, and OrWhere
`Where` sets the base WHERE predicate for the query. `AndWhere` and `OrWhere` append additional predicates, combining them with `AND` and `OR` respectively. Predicates are evaluated in declaration order, without implicit grouping — wrap compound expressions in a single lambda when precedence matters.

### `Where` — set the base predicate
```
var query = new SqlQuery<Person>(provider)
    .Where(x => x.Age > 18);
// WHERE "t1"."Age" > @p0
```

### `AndWhere` — append a predicate with AND
```
var query = new SqlQuery<Person>(provider)
    .Where(x => x.Age > 18)
    .AndWhere(x => x.Name == "John");
// WHERE "t1"."Age" > @p0 AND "t1"."Name" = @p1
```

Multiple `AndWhere` calls can be chained:
```
var query = new SqlQuery<Person>(provider)
    .Where(x => x.Age > 18)
    .AndWhere(x => x.Age < 65)
    .AndWhere(x => x.Name == "John");
// WHERE "t1"."Age" > @p0 AND "t1"."Age" < @p1 AND "t1"."Name" = @p2
```

### `OrWhere` — append a predicate with OR
```
var query = new SqlQuery<Person>(provider)
    .Where(x => x.Age > 18)
    .OrWhere(x => x.Name == "Admin");
// WHERE "t1"."Age" > @p0 OR "t1"."Name" = @p1
```

### Mixing AndWhere and OrWhere
`AndWhere` and `OrWhere` can be freely mixed. Predicates are combined in declaration order without implicit parenthesisation:
```
var query = new SqlQuery<Person>(provider)
    .Where(x => x.Age > 18)
    .AndWhere(x => x.Age < 65)
    .OrWhere(x => x.Name == "Admin");
// WHERE "t1"."Age" > @p0 AND "t1"."Age" < @p1 OR "t1"."Name" = @p2
```

> **Note:** If `AndWhere` or `OrWhere` is called without a prior `Where`, the first additional predicate becomes the sole base fragment and its operator is not emitted:
> ```
> var query = new SqlQuery<Person>(provider)
>     .AndWhere(x => x.Age > 18)
>     .AndWhere(x => x.Name == "John");
> // WHERE "t1"."Age" > @p0 AND "t1"."Name" = @p1
> ```

### AndWhere / OrWhere with Joins
Additional predicates compose naturally with joins:
```
var query = new SqlQuery<Person>(provider)
    .Join<Pet>((person, pet) => person.Id == pet.OwnerId)
    .AndWhere(x => x.Age > 18)
    .Select<Person, Pet>((p, pt) => new { PersonName = p.Name, PetName = pt.Name });
// SELECT ... FROM "Person" AS "t1"
// INNER JOIN "Pet" AS "t2" ON "t1"."Id" = "t2"."OwnerId"
// WHERE "t1"."Age" > @p0
```

## Join Syntax
`SqlQuery<T>` supports several join syntaxes:

### 1. Predicate-based Join
```
var query = new SqlQuery<Person>(provider)
    .Join<Pet>((person, pet) => person.Id == pet.OwnerId && pet.Name != null)
    .Select<Person,Pet>((p, pt) => new { PersonName = p.Name, PetName = pt.Name });
```

### 2. Chained Joins
```
var query = new SqlQuery<Order>(provider)
    .Join<Customer>((o, c) => o.CustomerId == c.Id)
    .Join<Customer,Address>((c, a) => c.AddressId == a.Id)
    .Join<Product>((o, p) => o.ProductId == p.Id)
    .Select<Customer, Product>((c,p) => new { CustomerName = c.Name, ProductName = p.Name });
```

## Projection
- Use `Select` with an expression to project results:
```
var query = new SqlQuery<Person>(provider)
    .Select(p => new { p.Name, p.Age });

var query2 = new SqlQuery<Person>(provider)
    .Join<Pet>((person, pet) => person.Id == pet.OwnerId)
    .Select<Person,Pet>((p, pt) => new { PersonName = p.Name, PetName = pt.Name });

var query3 = new SqlQuery<Person>(provider)
    .Join<Pet>((person, pet) => person.Id == pet.OwnerId && pet.Name != null)
    .Where<Person>(p => p.Age > 18)
    .Select<Pet>(pt => new Pet { Name = pt.Name, Breed="Poodle" });
```

## SQL Generation
To get the SQL and parameters for a query:
```
var (sql, parameters) = query.ToSql();
```
- `sql` is the generated SQL statement.
- `parameters` is an object suitable for Dapper parameterization.

Pass a `QueryOptions` instance to control schema resolution at generation time:
```
var options = new QueryOptions { Schema = "reporting" };
var (sql, parameters) = query.ToSql(options);
// Table references in sql use the "reporting" schema
```

## Dynamic Predicate Generation
`SqlQuery.BuildPredicate()` is a static utility for dynamically building a boolean predicate expression from an array of `FilterCriteria` objects and a boolean operator ("AND" or "OR").

This is useful for constructing dynamic WHERE clauses or JOIN criteria at runtime based on user input or other criteria.

### Usage
```
// Suppose you have:
// class Person { public int Age; }
// class Pet { public string Name; }
var filters = new[]
{
    new FilterCriteria(typeof(Person), nameof(Person.Age), ExpressionType.GreaterThan, 18),
    new FilterCriteria(typeof(Pet), nameof(Pet.Name), ExpressionType.Equal, "Fido")
};
var lambda = SqlQuery<Person>.BuildPredicate(filters, "AND");
// lambda is Expression<Func<Person, Pet, bool>>
```

Each `FilterCriteria` specifies:
- `EntityType`: The type (e.g., `typeof(Person)`) for the left side of the comparison.
- `PropertyName`: The property name on the entity type to compare.
- `Operator`: The comparison operator (e.g., `ExpressionType.Equal`, `ExpressionType.GreaterThan`).
- `RightOperand`: The constant value to compare against.

The boolean operator parameter must be either "AND" or "OR" and determines how the fragments are combined.

**Example:**
```
var filters = new[]
{
    new FilterCriteria(typeof(Person), "Age", ExpressionType.GreaterThan, 18),
    new FilterCriteria(typeof(Pet), "Name", ExpressionType.Equal, "Fido")
};
var predicate = SqlQuery<Person>.BuildPredicate(filters, "AND");
// Produces: (Person p, Pet pet) => (p.Age > 18) && (pet.Name == "Fido")

// Use the Predicate as a dynamic WHERE clause in a query:
var provider = new QueryProvider(new SqlServerDialect());
var query = new SqlQuery<Person>(provider)
    .Join<Pet>((person, pet) => person.Id == pet.OwnerId)
    .Where(predicate);
// Now query.ToSql() will use the dynamically built predicate in the WHERE clause
```

## Dynamic Sorting with OrderBy(IEnumerable<OrderBy>)
In addition to the strongly-typed `OrderBy`, `OrderByDescending`, `ThenBy`, and `ThenByDescending` methods that take lambda expressions, `SqlQuery<T>` supports building sort criteria dynamically at runtime from a list of column names — useful for grid sorting, API query parameters, or other user-driven scenarios where column names arrive as strings rather than compile-time expressions.

```
public SqlQuery<T> OrderBy(IEnumerable<OrderBy> orders)
```

Each `OrderBy` element specifies:
- `Column`: The column to sort by, either as a plain property name (e.g., `"Name"`) or in dotted `"ClassName.PropertyName"` notation (e.g., `"Pet.Name"`).
- `Direction`: A `SortDir` value — `SortDir.Asc` or `SortDir.Desc`.

### Column resolution rules
For each `OrderBy.Column`, the type it applies to is resolved against the generic types used by the query's `Select`, `From`, and `Join` clauses, checked in that precedence order:
1. The generic type parameters of the `Select` expression (if a typed `Select<T1,...>` was used).
2. The query's `From` type (`T`).
3. The generic type arguments of any `Join`/`LeftJoin`/`RightJoin` calls, in the order they were added.

- **Dotted notation** (`"ClassName.PropertyName"`): `ClassName` must match the simple name of one of the candidate types, and `PropertyName` must be a public instance property on that type. This disambiguates the column when multiple joined types could otherwise share a property name.
- **Plain property name**: The candidate types are searched in the precedence order above, and the first type that declares a matching public instance property is used.

If a column cannot be resolved — because the class name doesn't match any candidate type, or the property doesn't exist on the resolved type — an `ArgumentException` is thrown.

### Basic usage
```
var ordering = new List<OrderBy>
{
    new OrderBy { Column = "Name", Direction = SortDir.Asc }
};

var query = new SqlQuery<Person>(provider)
    .OrderBy(ordering);
// ORDER BY "t1"."Name" ASC
```

### Multiple sort columns
```
var ordering = new List<OrderBy>
{
    new OrderBy { Column = "Name", Direction = SortDir.Asc },
    new OrderBy { Column = "Age", Direction = SortDir.Desc }
};

var query = new SqlQuery<Person>(provider)
    .OrderBy(ordering);
// ORDER BY "t1"."Name" ASC, "t1"."Age" DESC
```

### Disambiguating joined columns with dotted notation
When a query joins multiple types, use `"ClassName.PropertyName"` to specify which type's column to sort by:
```
var ordering = new List<OrderBy>
{
    new OrderBy { Column = "Pet.Name", Direction = SortDir.Desc }
};

var query = new SqlQuery<Person>(provider)
    .Join<Pet>((person, pet) => person.Id == pet.OwnerId)
    .OrderBy(ordering);
// ORDER BY "t2"."Name" DESC
```

## Mutation Operations
`SqlQuery<T>` provides methods for generating mutation SQL:
```
var (insertSql, insertParams) = query.ToInsertSql(new Person { Name = "Alice", Age = 30 });
var (updateSql, updateParams) = query.ToUpdateSql(new Person { Id = 1, Name = "Alice Smith", Age = 31 });
var (updateSql2, updateParams2) = query.ToUpdateSql(new Person { Age = 21 }, x => x.Age > 18);
var (deleteSql, deleteParams) = query.ToDeleteSql(x => x.Id == 1);
var (deleteSql2, deleteParams2) = query.ToDeleteSql(new Person { Id = 1 });
```
- These methods generate SQL for insert, update, and delete operations.
- Supports advanced predicates for WHERE clause and key-based deletes.

### Schema control on mutation operations
Pass `InsertOptions` or `UpdateOptions` to apply schema resolution. Both inherit from `QueryOptions` and expose `Schema` and `DefaultSchema`:

| Option | Effect |
|---|---|
| `Schema` | Unconditionally replaces the schema in the resolved table name. |
| `DefaultSchema` | Applies a schema only when the table name has no schema component. |

```
// Force a specific schema
var (sql, p) = query.ToInsertSql(
    new Person { Name = "Alice", Age = 30 },
    new InsertOptions { Schema = "staging" });
// INSERT INTO "staging"."Person" (...) VALUES (...)

// Apply a default schema only if Person has no schema in its mapping
var (sql2, p2) = query.ToInsertSql(
    new Person { Name = "Bob", Age = 25 },
    new InsertOptions { DefaultSchema = "app" });
// INSERT INTO "app"."Person" (...) VALUES (...)

// Schema override on update
var (sql3, p3) = query.ToUpdateSql(
    new Person { Id = 1, Name = "Carol" },
    new UpdateOptions { Schema = "audit", SelectAfterMutation = true });
// UPDATE "audit"."Person" SET ... RETURNING *;
```

## Example Usage
```
// Setup
var provider = new QueryProvider(new SqlServerDialect());

// Query for people older than 18, with an additional name filter
var query = new SqlQuery<Person>(provider)
    .Where(x => x.Age > 18)
    .AndWhere(x => x.Name == "Alice");
var (sql, parameters) = query.ToSql();
// Use with Dapper:
var results = connection.Query<Person>(sql, parameters);

// Query using OrWhere
var orQuery = new SqlQuery<Person>(provider)
    .Where(x => x.Age > 65)
    .OrWhere(x => x.Name == "Admin");
var (orSql, orParams) = orQuery.ToSql();
var orResults = connection.Query<Person>(orSql, orParams);

// Query for people older than 18
var query = new SqlQuery<Person>(provider).Where(x => x.Age > 18);
var (sql, parameters) = query.ToSql();
// Use with Dapper:
var results = connection.Query<Person>(sql, parameters);

// Insert a new person
var (insertSql, insertParams) = query.ToInsertSql(new Person { Name = "Alice", Age = 30 });
connection.Execute(insertSql, insertParams);

// Update a person
var (updateSql, updateParams) = query.ToUpdateSql(new Person { Id = 1, Name = "Alice Smith", Age = 31 });
connection.Execute(updateSql, updateParams);

// Update with predicate
var (updateSql2, updateParams2) = query.ToUpdateSql(new Person { Age = 21 }, x => x.Age > 18);
connection.Execute(updateSql2, updateParams2);

// Delete by predicate
var (deleteSql, deleteParams) = query.ToDeleteSql(x => x.Id == 1);
connection.Execute(deleteSql, deleteParams);

// Delete by entity instance (key fields)
var (deleteSql2, deleteParams2) = query.ToDeleteSql(new Person { Id = 1 });
connection.Execute(deleteSql2, deleteParams2);

// Join example
var joinQuery = new SqlQuery<Person>(provider)
    .Join<Pet>((person, pet) => person.Id == pet.OwnerId)
    .Select<Person,Pet>((p, pt) => new { PersonName = p.Name, PetName = pt.Name });
var (joinSql, joinParams) = joinQuery.ToSql();
connection.Query<PersonPet>(joinSql, joinParams);
```

## Notes
- `SqlQuery<T>` is not directly enumerable; use `.ToSql()` to get SQL for execution.
- All query and projection lambdas must be expressions, not delegates.
- Designed for SQL generation and parameterization, not for in-memory LINQ execution.
- Use with Dapper or other ADO.NET libraries for data access.
- Mutation methods support both predicate-based and key-based operations for update and delete.
- Feature parity across SQL Server and PostgreSQL dialects.

---
For more details, see the source code and unit tests in the repository.
