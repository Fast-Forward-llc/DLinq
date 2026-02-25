# CharEnum Attribute

## Overview

The `[CharEnum]` attribute is used to indicate that an enum with integer underlying type should be treated as having character values for database parameter conversion.

## Problem

In C#, enums cannot have `char` as their underlying type. The allowed underlying types are: byte, sbyte, short, ushort, int, uint, long, or ulong. However, it's common to define enums with character literal values for database columns that are stored as char/varchar:

```csharp
enum Status
{
    Active = 'A',      // This is actually stored as int 65
    Inactive = 'I',    // This is actually stored as int 73
    Pending = 'P'      // This is actually stored as int 80
}
```

When the C# compiler encounters this, it converts the char literals to their ASCII integer values. When used in LINQ queries, the parameter will be the integer value (e.g., 65) instead of the character 'A'.

## Solution

Use the `[CharEnum]` attribute to explicitly mark enums that should be converted to char values for database parameters:

```csharp
[CharEnum]
enum Status
{
    Active = 'A',
    Inactive = 'I',
    Pending = 'P'
}
```

## Usage Example

```csharp
[CharEnum]
public enum OrderTypeCode
{
    Standard = 'S',
    Express = 'E',
    International = 'I'
}

public class Order
{
    public Guid Id { get; set; }
    public OrderTypeCode TypeCode { get; set; }
}

// When querying:
var query = new SqlQuery<Order>(provider)
    .Where(x => x.TypeCode == OrderTypeCode.Standard);

var (sql, parameters) = query.ToSql();
// SQL: SELECT * FROM [Order] WHERE [TypeCode] = @p0
// parameters["p0"] will be 'S' (char), not 83 (int)
```

## Without the Attribute

If you don't use the `[CharEnum]` attribute, the enum values will remain as integers:

```csharp
// Regular enum without attribute
public enum ProductStatus
{
    Inactive = 0,
    Active = 1,
    Discontinued = 2
}

var query = new SqlQuery<Product>(provider)
    .Where(x => x.Status == ProductStatus.Active);

var (sql, parameters) = query.ToSql();
// parameters["p0"] will be 1 (int)
```

## How It Works

1. When parsing LINQ expressions, the query translator detects Convert nodes from enum types to int
2. It checks if the enum type has the `[CharEnum]` attribute
3. If the attribute is present, it converts the integer parameter value to a char before adding it to the parameters collection
4. This ensures the database receives the correct character value

## Attribute Definition

```csharp
[AttributeUsage(AttributeTargets.Enum, AllowMultiple = false, Inherited = false)]
public class CharEnumAttribute : Attribute
{
}
```

## Notes

- The attribute only affects parameter conversion for DLinq SQL translation
- It does not change the underlying type of the enum (which remains int)
- The conversion happens automatically when building SQL queries
- This is particularly useful for legacy databases that use char columns for status/code fields
