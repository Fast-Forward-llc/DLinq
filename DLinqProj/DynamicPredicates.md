# Dynamic Predicate Generation

`SqlQuery<T>` provides two complementary ways to build WHERE/JOIN predicates dynamically at runtime, when the filter criteria aren't known until the query executes (e.g., user input, API query parameters, grid filters):

- **`SqlQuery<T>.BuildPredicate(FilterCriteria[] filters, string boolOperator)`** — a static utility that builds a predicate from an array of strongly-typed `FilterCriteria` objects.
- **`BuildJsonPredicate(JsonObject filter, string combineOperator, bool ignoreInvalidColumns)`** — builds a predicate from a `System.Text.Json.Nodes.JsonObject`, and backs the `Where(JsonObject)`, `AndWhere(JsonObject)`, and `OrWhere(JsonObject)` overloads.

Both return a `LambdaExpression` that can be passed to `Where`, or (for the JSON variant) are applied automatically by the `Where`/`AndWhere`/`OrWhere` `JsonObject` overloads.

---

## `BuildPredicate` — FilterCriteria-based predicates

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

### Supported operators
`BuildPredicate` supports the following `ExpressionType` values per `FilterCriteria.Operator`:
- `Equal`
- `NotEqual`
- `GreaterThan`
- `GreaterThanOrEqual`
- `LessThan`
- `LessThanOrEqual`

An unsupported `ExpressionType` throws a `NotSupportedException`.

### Example
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

---

## `BuildJsonPredicate` — JsonObject-based predicates

`BuildJsonPredicate` dynamically builds a combined predicate from a `JsonObject`, where each property specifies a column filter. It underlies three fluent overloads on `SqlQuery<T>`:

```
public SqlQuery<T> Where(JsonObject filter, string combineOperator = "and", bool ignoreInvalidColumns = false)
public SqlQuery<T> AndWhere(JsonObject filter, string combineOperator = "and", bool ignoreInvalidColumns = false)
public SqlQuery<T> OrWhere(JsonObject filter, string combineOperator = "and", bool ignoreInvalidColumns = false)
```

- `Where(JsonObject)` sets the base WHERE predicate (equivalent to calling `Where(LambdaExpression)`); it does not chain with any previously set WHERE predicate.
- `AndWhere(JsonObject)` / `OrWhere(JsonObject)` append the JSON-built predicate to the query's chained WHERE predicates, combined with `AND`/`OR` respectively — following the same chaining semantics as the expression-based `AndWhere`/`OrWhere` overloads.

This is useful for building WHERE clauses directly from untyped JSON payloads — e.g., API request bodies, saved filter definitions, or grid filter models — without having to hand-write expression trees.

### Column resolution
Each JSON property key is a column name, resolved using the same rules as dynamic `OrderBy` (see [Dynamic Sorting with OrderBy(IEnumerable&lt;OrderBy&gt;)](SqlQuery.md#dynamic-sorting-with-orderbyienumerableorderby)):
1. The generic type parameters of the query's `Select` expression (if a typed `Select<T1,...>` was used).
2. The query's `From` type (`T`).
3. The generic type arguments of any `Join`/`LeftJoin`/`RightJoin` calls, in the order they were added.

Column names may use **dotted notation** (`"ClassName.PropertyName"`, e.g. `"Pet.Name"`) to disambiguate between joined types, or a **plain property name** to be resolved against the candidate types in precedence order.

If a column cannot be resolved, an `ArgumentException` is thrown — unless `ignoreInvalidColumns` is `true`, in which case that property is silently skipped.

### Value shapes
Each JSON property value can be one of:

| Shape | Example | Meaning |
|---|---|---|
| Primitive (string/number/bool) | `"Name": "John"` | Equality comparison |
| `null` | `"Name": null` | `IS NULL` |
| Array | `"Id": [1, 2, 3]` | `IN` comparison against the array elements |
| Operator object | `"Age": { "op": "gt", "value": 18 }` | Comparison using the specified operator |

### Supported operators (operator-object shape)
| `op` | Meaning |
|---|---|
| `eq` | Equality (`=`); array `value` produces `IN` |
| `ne` | Inequality (`<>`); array `value` produces `NOT IN` |
| `gt` | Greater than (`>`) |
| `gte` | Greater than or equal (`>=`) |
| `lt` | Less than (`<`) |
| `lte` | Less than or equal (`<=`) |
| `~` | LIKE/Contains — column must be `string`; translates to `string.Contains` |
| `sw` | StartsWith — column must be `string`; translates to `string.StartsWith` |
| `ew` | EndsWith — column must be `string`; translates to `string.EndsWith` |

Using `~`, `sw`, or `ew` against a non-`string` column throws an `ArgumentException`.

### Combining filters
All property filters within a single `JsonObject` are combined using `combineOperator` ("and" or "or", case-insensitive; default "and"). This only controls how the filters *within* that `JsonObject` are combined with each other — it is independent of how `AndWhere`/`OrWhere` append the resulting predicate to the query's existing chain.

### Basic usage
```
var filter = new JsonObject
{
    ["Age"] = new JsonObject { ["op"] = "gt", ["value"] = 18 },
    ["Name"] = "John"
};

var query = new SqlQuery<Person>(provider)
    .Where(filter);
// WHERE "t1"."Age" > @p0 AND "t1"."Name" = @p1
```

### Using "or" to combine properties
```
var filter = new JsonObject
{
    ["Name"] = "John",
    ["Age"] = 18
};

var query = new SqlQuery<Person>(provider)
    .Where(filter, combineOperator: "or");
// WHERE "t1"."Name" = @p0 OR "t1"."Age" = @p1
```

### Chaining with AndWhere / OrWhere
```
var filter = new JsonObject { ["Name"] = "Admin" };

var query = new SqlQuery<Person>(provider)
    .Where(x => x.Age > 18)
    .OrWhere(filter);
// WHERE "t1"."Age" > @p0 OR "t1"."Name" = @p1
```

### Arrays (`IN`) and `null` (`IS NULL`)
```
var filter = new JsonObject
{
    ["Id"] = new JsonArray(1, 2, 3)
};
var query = new SqlQuery<Person>(provider).Where(filter);
// WHERE "t1"."Id" IN (1, 2, 3)

var nullFilter = new JsonObject { ["Name"] = null };
var query2 = new SqlQuery<Person>(provider).Where(nullFilter);
// WHERE "t1"."Name" IS NULL
```

### String matching operators
```
var filter = new JsonObject
{
    ["Name"] = new JsonObject { ["op"] = "sw", ["value"] = "Jo" }
};
var query = new SqlQuery<Person>(provider).Where(filter);
// WHERE "t1"."Name" LIKE @p0   (parameter value: "Jo%")
```

### Dotted column names with joins
```
var filter = new JsonObject { ["Pet.Name"] = "Fido" };

var query = new SqlQuery<Person>(provider)
    .Join<Pet>((person, pet) => person.Id == pet.OwnerId)
    .Where(filter);
// WHERE "t2"."Name" = @p0
```

### Ignoring invalid columns
```
var filter = new JsonObject
{
    ["NotAProperty"] = "value",
    ["Name"] = "John"
};
var query = new SqlQuery<Person>(provider)
    .Where(filter, ignoreInvalidColumns: true);
// "NotAProperty" is skipped; WHERE "t1"."Name" = @p0
```

### Error conditions
- `ArgumentNullException` — `filter` is `null`.
- `ArgumentException` — `combineOperator` is not "and"/"or"; a column cannot be resolved and `ignoreInvalidColumns` is `false`; an unsupported `op` value is used; or `~`/`sw`/`ew` is used against a non-`string` column.
