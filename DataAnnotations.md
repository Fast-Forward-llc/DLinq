# Data Annotations in DLinq

## Overview
DLinq supports standard .NET data annotations to help map C# classes and properties to database tables and columns. These annotations allow you to control table names, column names, primary keys, identity columns, computed columns, and ignored properties directly from your model classes.

## Supported Data Annotations

### [Table]
Specifies the database table name for an entity.
```csharp
[Table("People")]
public class Person { ... }
```

### [Column]
Specifies the database column name for a property.
```csharp
[Column("first_name")]
public string FirstName { get; set; }
```

### [Key]
Marks a property as a primary key. Supports single and composite keys.
```csharp
[Key]
public int Id { get; set; }
```

### [DatabaseGenerated]
Controls identity and computed columns.
```csharp
[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
public int Id { get; set; }

[DatabaseGenerated(DatabaseGeneratedOption.Computed)]
public DateTime LastModified { get; set; }
```

### [NotMapped]
Excludes a property from SQL generation (not persisted in the database).
```csharp
[NotMapped]
public string TempValue { get; set; }
```

## Usage Notes
- DLinq automatically detects and uses these annotations when generating SQL for queries and mutations.
- Composite keys are supported by applying `[Key]` to multiple properties.
- `[Table]` and `[Column]` are optional; if omitted, DLinq uses the class/property name.
- `[DatabaseGenerated]` is used to skip identity/computed columns during insert/update.
- `[NotMapped]` properties are ignored in all SQL operations.

## Example
```csharp
[Table("people")]
public class Person
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Column("first_name")]
    public string FirstName { get; set; }

    [Column("last_name")]
    public string LastName { get; set; }

    public int Age { get; set; }

    [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
    public DateTime LastModified { get; set; }

    [NotMapped]
    public string TempValue { get; set; }
}
```

## Advanced
- DLinq supports `[Key]` on multiple properties for composite keys.
- `[DatabaseGenerated]` can be used with both `Identity` and `Computed` options.

---
For more details, see the source code and integration tests in the repository.
