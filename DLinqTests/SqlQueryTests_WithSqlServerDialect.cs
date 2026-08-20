using DLinq;
using Microsoft.VisualStudio.TestPlatform.Utilities;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Dynamic;
using System.Text.Json;
using System.Xml.Linq;

namespace DLinqTests
{
    [TestClass]
    public class SqlQueryTests_WithSqlServerDialect
    {
        private QueryProvider GetProvider() => new QueryProvider(new SqlServerDialect());

        private class Person
        {
            [Key]
            [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
            public int Id { get; set; }
            public string Name { get; set; }
            public int Age { get; set; }
        }

        [Table("Person", Schema ="xyz")]
        private class Person3
        {
            [Key]
            [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
            public int Id { get; set; }
            public string Name { get; set; }
            public int Age { get; set; }
        }

        private class Pet
        {
            [Key]
            [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
            public int Id { get; set; }
            public int OwnerId { get; set; }
            public string Name { get; set; }
        }

        [Table("Pet", Schema ="zz1")]
        private class Pet2 : Pet
        {
        }

        private class PersonPet
        {
            public string PersonName { get; set; }
            public string PetName { get; set; }
        }

        private class TestDialect : DummyDialect
        {
        }

        // Base class with a column ("Name") that will also be shared (by name) with the joined entity below.
        private abstract class AnimalBase
        {
            [Key]
            [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
            public int Id { get; set; }
            public string Name { get; set; }
        }

        // Derived "From" class - inherits Id/Name from AnimalBase and adds its own column.
        private class Pet3 : AnimalBase
        {
            public int OwnerId { get; set; }
        }

        private class PersonWithNotMapped
        {
            [Key]
            [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
            public int Id { get; set; }
            public string Name { get; set; }

            [NotMapped]
            public string FullDisplayName { get; set; }
        }

        private class PersonWithColumnAttribute
        {
            [Key]
            [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
            [Column("PersonId")]
            public int Id { get; set; }
            [Column("FullName")]
            public string Name { get; set; }
            public int Age { get; set; }
        }

        [TestInitialize]
        public void TestInitialize() {
        }

        [TestMethod]
        public void Select_All()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider);
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual("SELECT * FROM [Person] AS [t1]", sql);
        }

        [TestMethod]
        public void Select_All_OrderByDescending()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider)
                .OrderByDescending(x => x.Name)
                .ThenBy(x => x.Age);
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual("SELECT * FROM [Person] AS [t1] ORDER BY [t1].[Name] DESC, [t1].[Age] ASC", sql);
        }

        [TestMethod]
        public void Select_All_Projection()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider).Select(p => new { p.Name, p.Age, DepartmentId = 7 });
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual("SELECT [t1].[Name] AS [Name], [t1].[Age] AS [Age], @p0 AS [DepartmentId] FROM [Person] AS [t1]", sql);
        }

        [TestMethod]
        public void Select_Generic_T1_ProjectsAllColumnsOfT1()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider).Select<Person>();
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual("SELECT [t1].[Id] AS [Id], [t1].[Name] AS [Name], [t1].[Age] AS [Age] FROM [Person] AS [t1]", sql);
        }

        [TestMethod]
        public void Select_Generic_T1_WithInheritedBaseColumns_And_JoinedEntityWithSameColumnNames_UsesCorrectAliases()
        {
            var provider = GetProvider();
            // Pet3 inherits Id/Name from AnimalBase; joined Person also has Id/Name columns.
            var query = new SqlQuery<Pet3>(provider)
                .Join<Person>((pet, person) => pet.OwnerId == person.Id)
                .Select<Pet3>();
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual(
                "SELECT [t1].[OwnerId] AS [OwnerId], [t1].[Id] AS [Id], [t1].[Name] AS [Name] FROM [Pet3] AS [t1]"
                + " INNER JOIN [Person] AS [t2] ON [t1].[OwnerId] = [t2].[Id]"
                , sql);
        }

        [TestMethod]
        public void Select_Generic_T1_ExcludesNotMappedProperties()
        {
            var provider = GetProvider();
            var query = new SqlQuery<PersonWithNotMapped>(provider).Select<PersonWithNotMapped>();
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual("SELECT [t1].[Id] AS [Id], [t1].[Name] AS [Name] FROM [PersonWithNotMapped] AS [t1]", sql);
        }

        [TestMethod]
        public void Select_Generic_T1_UsesColumnAttributeName_ButAliasesToPropertyName()
        {
            var provider = GetProvider();
            var query = new SqlQuery<PersonWithColumnAttribute>(provider).Select<PersonWithColumnAttribute>();
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual(
                "SELECT [t1].[PersonId] AS [Id], [t1].[FullName] AS [Name], [t1].[Age] AS [Age] FROM [PersonWithColumnAttribute] AS [t1]",
                sql);
        }

        [TestMethod]
        public void Where()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider).Where(x => x.Age > 18);
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.IsTrue(sql.Contains("WHERE [t1].[Age] > @p0"));
            Assert.AreEqual(1, ((IDictionary<string, object>)parameters).Count);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual(18, paramDict["p0"]);
        }

        [TestMethod]
        public void Where_WithParameters()
        {
            var minAge = 18;
            var maxAge = 80;
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider).Where(x => x.Age > minAge && x.Name != null && x.Age < maxAge);
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual("SELECT * FROM [Person] AS [t1]\r\nWHERE ([t1].[Age] > @p0 AND [t1].[Name] IS NOT NULL AND [t1].[Age] < @p1)", sql);
            Assert.AreEqual(2, ((IDictionary<string, object>)parameters).Count);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual(18, paramDict["p0"]);
            Assert.AreEqual(80, paramDict["p1"]);
        }

        [TestMethod]
        public void Where_WithParameters_FromObject()
        {
            var ageLimits = new { Min = 18, Max = 80 };
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider).Where(x => x.Age > ageLimits.Min && x.Name != null && x.Age < ageLimits.Max);
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual("SELECT * FROM [Person] AS [t1]\r\nWHERE ([t1].[Age] > @p0 AND [t1].[Name] IS NOT NULL AND [t1].[Age] < @p1)", sql);
            Assert.AreEqual(2, ((IDictionary<string, object>)parameters).Count);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual(18, paramDict["p0"]);
            Assert.AreEqual(80, paramDict["p1"]);
        }

        [TestMethod]
        public void OrderBy()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider).OrderBy(x => x.Name);
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.IsTrue(sql.Contains("SELECT"));
        }

        [TestMethod]
        public void SkipTake()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider).Skip(5).Take(10);
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.IsTrue(sql.Contains("SELECT"));
        }

        [TestMethod]
        public void ToInsertSql()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider);
            var (sql, parameters) = query.ToInsertSql(new Person { Name = "Test", Age = 20 });
            Console.WriteLine(sql);
            Assert.AreEqual("INSERT INTO [Person] ([Name], [Age]) VALUES (@Name, @Age)", sql);
        }

        [TestMethod]
        public void ToInsertSql_MapSchema()
        {
            var provider = GetProvider();
            provider.Translator.Entity2TableMapper = (entityType, tableName) =>
            {
                return "schema." + tableName;
            };
            var query = new SqlQuery<Person>(provider);
            var (sql, parameters) = query.ToInsertSql(new Person { Name = "Test", Age = 20 });
            Console.WriteLine(sql);
            Assert.AreEqual("INSERT INTO [schema].[Person] ([Name], [Age]) VALUES (@Name, @Age)", sql);
        }

        [TestMethod]
        public void ToInsertSql_Anonymous()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider);
            var (sql, parameters) = query.ToInsertSql(new { Name = "Test", Age = 20 });
            Console.WriteLine(sql);
            Assert.AreEqual("INSERT INTO [Person] ([Name], [Age]) VALUES (@Name, @Age)",sql);
        }

        [TestMethod]
        public void ToInsertSql_TableAttr_Schema()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person3>(provider);
            var (sql, parameters) = query.ToInsertSql(new { Name = "Test", Age = 20 });
            Console.WriteLine(sql);
            Assert.AreEqual("INSERT INTO [xyz].[Person] ([Name], [Age]) VALUES (@Name, @Age)", sql);
        }

        [TestMethod]
        public void ToInsertSql_DynamicSchemaAndTable()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider);
            var options = new DLinq.InsertOptions { TableName = "customschema.CustomPerson" };
            var (sql, parameters) = query.ToInsertSql(new Person { Name = "Dynamic", Age = 42 }, options);
            Console.WriteLine(sql);
            Assert.IsTrue(sql.Contains("customschema"));
            Assert.IsTrue(sql.Contains("CustomPerson"));
            Assert.IsTrue(sql.StartsWith("INSERT INTO"));
        }

        [TestMethod]
        public void ToUpdateSql()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider);
            var (sql, parameters) = query.ToUpdateSql(new Person { Id = 1, Name = "Test", Age = 21 });
            Console.WriteLine(sql);
            Assert.AreEqual("UPDATE [Person] SET [Name] = @Name, [Age] = @Age\r\nWHERE [Id] = @p0",sql);
        }

        [TestMethod]
        public void ToUpdateSql_MapSchema()
        {
            var provider = GetProvider();
            provider.Translator.Entity2TableMapper = (entityType, tableName) =>
            {
                return "schema." + tableName;
            };
            var query = new SqlQuery<Person>(provider);
            var (sql, parameters) = query.ToUpdateSql(new Person { Id = 1, Name = "Test", Age = 21 });
            Console.WriteLine(sql);
            Assert.AreEqual("UPDATE [schema].[Person] SET [Name] = @Name, [Age] = @Age\r\nWHERE [Id] = @p0", sql);
        }
        
        [TestMethod]
        public void ToUpdateSql_WithPredicate()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider);
            var (sql, parameters) = query.ToUpdateSql(new Person { Id = 1, Name = "Test", Age = 21 }, _ => _.Name.StartsWith("X"));
            Console.WriteLine(sql);
            Assert.AreEqual("UPDATE [Person] SET [Name] = @Name, [Age] = @Age\r\nWHERE [Person].[Name] LIKE @p0",sql);
        }

        [TestMethod]
        public void ToUpdateSql_Anonymous_WithPredicate()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider);
            var (sql, parameters) = query.ToUpdateSql(new { Id = 1, Name = "Test", Age = 21 }, _ => _.Name.StartsWith("X"));
            Console.WriteLine(sql);
            Assert.AreEqual("UPDATE [Person] SET [Name] = @Name, [Age] = @Age\r\nWHERE [Person].[Name] LIKE @p0", sql);
        }

        [TestMethod]
        public void ToDeleteSql()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider);
            var (sql, parameters) = query.ToDeleteSql(x => x.Id == 1);
            Console.WriteLine(sql);
            Assert.IsTrue(sql.StartsWith("DELETE FROM"));
        }

        [TestMethod]
        public void ToInsertSql_GeneratesFullSql()
        {
            var query = new SqlQuery<TestEntity>(GetProvider());
            var (sql, parameters) = query.ToInsertSql(new TestEntity { Id = 1, Name = "abc" });
            Console.WriteLine($"SQL: {sql} \r\nParameters: {JsonSerializer.Serialize(parameters)}");
            StringAssert.Contains(sql, "INSERT INTO [DummyTable]");
            StringAssert.Contains(sql, "[Id]");
            StringAssert.Contains(sql, "[Name]");
            Assert.IsTrue(parameters is ExpandoObject);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual(1, paramDict["@Id"]);
            Assert.AreEqual("abc", paramDict["@Name"]);
        }

        [TestMethod]
        public void ToInsertSql_GeneratesFullSql_DefaultSchema()
        {
            var query = new SqlQuery<TestEntity>(GetProvider());
            var (sql, parameters) = query.ToInsertSql(new TestEntity { Id = 1, Name = "abc" }, new InsertOptions { DefaultSchema = "schema" });
            Console.WriteLine($"SQL: {sql} \r\nParameters: {JsonSerializer.Serialize(parameters)}");
            StringAssert.Contains(sql, "INSERT INTO [schema].[DummyTable]");
            StringAssert.Contains(sql, "[Id]");
            StringAssert.Contains(sql, "[Name]");
            Assert.IsTrue(parameters is ExpandoObject);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual(1, paramDict["@Id"]);
            Assert.AreEqual("abc", paramDict["@Name"]);
        }

        [TestMethod]
        public void ToInsertSql_GeneratesFullSql_WithSchema()
        {
            var query = new SqlQuery<TestEntity2>(GetProvider());
            var (sql, parameters) = query.ToInsertSql(new TestEntity2 { Id = 1, Name = "abc" });
            Console.WriteLine($"SQL: {sql} \r\nParameters: {JsonSerializer.Serialize(parameters)}");
            StringAssert.Contains(sql, "INSERT INTO [dbo].[DummyTable]");
            StringAssert.Contains(sql, "[Id]");
            StringAssert.Contains(sql, "[Name]");
            Assert.IsTrue(parameters is ExpandoObject);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual(1, paramDict["@Id"]);
            Assert.AreEqual("abc", paramDict["@Name"]);
        }

        [TestMethod]
        public void ToInsertSql_GeneratesFullSql_OverrideSchema()
        {
            var query = new SqlQuery<TestEntity2>(GetProvider());
            var (sql, parameters) = query.ToInsertSql(new TestEntity2 { Id = 1, Name = "abc" }, new InsertOptions { Schema = "abc" });
            Console.WriteLine($"SQL: {sql} \r\nParameters: {JsonSerializer.Serialize(parameters)}");
            StringAssert.Contains(sql, "INSERT INTO [abc].[DummyTable]");
            StringAssert.Contains(sql, "[Id]");
            StringAssert.Contains(sql, "[Name]");
            Assert.IsTrue(parameters is ExpandoObject);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual(1, paramDict["@Id"]);
            Assert.AreEqual("abc", paramDict["@Name"]);
        }

        [TestMethod]
        public void ToUpdateSql_GeneratesFullSql_DefaultSchema()
        {
            var query = new SqlQuery<TestEntity>(GetProvider());
            var (sql, parameters) = query.ToUpdateSql(new TestEntity { Id = 1, Name = "abc" }, new UpdateOptions() { DefaultSchema = "abc"});
            Console.WriteLine($"SQL: {sql} \r\nParameters: {JsonSerializer.Serialize(parameters)}");
            StringAssert.Contains(sql, "UPDATE [abc].[DummyTable]");
            StringAssert.Contains(sql, "SET [Name] = @Name");
            StringAssert.Contains(sql, "WHERE [Id] = @p0");
            Assert.IsTrue(parameters is ExpandoObject);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual(1, paramDict["@p0"]);
            Assert.AreEqual("abc", paramDict["@Name"]);
        }

        [TestMethod]
        public void ToUpdateSql_GeneratesFullSql_WithSchema()
        {
            var query = new SqlQuery<TestEntity2>(GetProvider());
            var (sql, parameters) = query.ToUpdateSql(new TestEntity2 { Id = 1, Name = "abc" }, new UpdateOptions() { DefaultSchema = "abc" });
            Console.WriteLine($"SQL: {sql} \r\nParameters: {JsonSerializer.Serialize(parameters)}");
            StringAssert.Contains(sql, "UPDATE [dbo].[DummyTable]");
            StringAssert.Contains(sql, "SET [Name] = @Name");
            StringAssert.Contains(sql, "WHERE [Id] = @p0");
            Assert.IsTrue(parameters is ExpandoObject);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual(1, paramDict["@p0"]);
            Assert.AreEqual("abc", paramDict["@Name"]);
        }

        [TestMethod]
        public void ToUpdateSql_GeneratesFullSql_OverrideSchema()
        {
            var query = new SqlQuery<TestEntity2>(GetProvider());
            var (sql, parameters) = query.ToUpdateSql(new TestEntity2 { Id = 1, Name = "abc" }, new UpdateOptions() { Schema = "abc" });
            Console.WriteLine($"SQL: {sql} \r\nParameters: {JsonSerializer.Serialize(parameters)}");
            StringAssert.Contains(sql, "UPDATE [abc].[DummyTable]");
            StringAssert.Contains(sql, "SET [Name] = @Name");
            StringAssert.Contains(sql, "WHERE [Id] = @p0");
            Assert.IsTrue(parameters is ExpandoObject);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual(1, paramDict["@p0"]);
            Assert.AreEqual("abc", paramDict["@Name"]);
        }

        [TestMethod]
        public void ToUpdateSql_GeneratesFullSql()
        {
            var query = new SqlQuery<TestEntity>(GetProvider());
            var (sql, parameters) = query.ToUpdateSql(new TestEntity { Id = 1, Name = "abc" });
            Console.WriteLine($"SQL: {sql} \r\nParameters: {JsonSerializer.Serialize(parameters)}");
            StringAssert.Contains(sql, "UPDATE [DummyTable]");
            StringAssert.Contains(sql, "SET [Name] = @Name");
            StringAssert.Contains(sql, "WHERE [Id] = @p0");
            Assert.IsTrue(parameters is ExpandoObject);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual(1, paramDict["@p0"]);
            Assert.AreEqual("abc", paramDict["@Name"]);
        }

        [TestMethod]
        public void ToUpdateSql_GeneratesFullSql_WithSelectAfterMutation()
        {
            var query = new SqlQuery<TestEntity>(GetProvider());
            var (sql, parameters) = query.ToUpdateSql(new TestEntity { Id = 1, Name = "abc" },new UpdateOptions() { SelectAfterMutation = true});
            Console.WriteLine($"SQL: {sql} \r\nParameters: {JsonSerializer.Serialize(parameters)}");
            StringAssert.Contains(sql, "UPDATE [DummyTable]");
            StringAssert.Contains(sql, "SET [Name] = @Name");
            StringAssert.Contains(sql, "WHERE [Id] = @p0");
            Assert.IsTrue(parameters is ExpandoObject);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual(1, paramDict["@p0"]);
            Assert.AreEqual("abc", paramDict["@Name"]);
        }

        [TestMethod]
        public void ToUpdateSql_GeneratesFullSql_WithSelect()
        {
            var query = new SqlQuery<TestEntity>(GetProvider());
            var (sql, parameters) = query.ToUpdateSql(new TestEntity { Id = 1, Name = "abc" },new UpdateOptions() { SelectAfterMutation = true });
            Console.WriteLine($"SQL: {sql} \r\nParameters: {JsonSerializer.Serialize(parameters)}");
            StringAssert.Contains(sql, "UPDATE [DummyTable]");
            StringAssert.Contains(sql, "SET [Name] = @Name");
            StringAssert.Contains(sql, "OUTPUT inserted.*");
            StringAssert.Contains(sql, "WHERE [Id] = @p0");
            Assert.IsTrue(parameters is ExpandoObject);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual(1, paramDict["@p0"]);
            Assert.AreEqual("abc", paramDict["@Name"]);
        }

        [TestMethod]
        public void ToDeleteSql_GeneratesFullSql_WithPredicate()
        {
            var query = new SqlQuery<TestEntity>(GetProvider());
            var (sql, parameters) = query.ToDeleteSql(x => x.Id == 1);
            Console.WriteLine($"SQL: {sql} \r\nParameters: {JsonSerializer.Serialize(parameters)}");
            StringAssert.Contains(sql, "DELETE FROM [DummyTable]");
            StringAssert.Contains(sql, "WHERE [Id] = @p0");
            Assert.IsTrue(parameters is ExpandoObject);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual(1, paramDict["@p0"]);
        }

        [TestMethod]
        public void Join()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider)
                .Join<Pet>((person, pet) => person.Id == pet.OwnerId)
                .Select<Person, Pet>((p, pt) => new { PersonName = p.Name, PetName = pt.Name });
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual(
                "SELECT [t1].[Name] AS [PersonName], [t2].[Name] AS [PetName] FROM [Person] AS [t1]"
                + " INNER JOIN [Pet] AS [t2] ON [t1].[Id] = [t2].[OwnerId]"
                , sql);
        }

        [TestMethod]
        public void JoinAndProject()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider)
                .Join<Pet>((person, pet) => person.Id == pet.OwnerId)
                .Select<Person, Pet>((p, pt) => new { Id = p.Id, OId = pt.Id, PersonName = p.Name, PetName = pt.Name });
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual(
                "SELECT [t1].[Id] AS [Id], [t2].[Id] AS [OId], [t1].[Name] AS [PersonName], [t2].[Name] AS [PetName] FROM [Person] AS [t1]"
                + " INNER JOIN [Pet] AS [t2] ON [t1].[Id] = [t2].[OwnerId]"
                , sql);
        }

        [TestMethod]
        public void LeftJoin()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider)
                .LeftJoin<Pet>((person, pet) => person.Id == pet.OwnerId)
                .Select<Person, Pet>((p, pt) => new { PersonName = p.Name, PetName = pt.Name });
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual(
                "SELECT [t1].[Name] AS [PersonName], [t2].[Name] AS [PetName] FROM [Person] AS [t1]"
                + " LEFT JOIN [Pet] AS [t2] ON [t1].[Id] = [t2].[OwnerId]"
                , sql);
        }

        [TestMethod]
        public void RightJoin()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider)
                .RightJoin<Pet>((person, pet) => person.Id == pet.OwnerId)
                .Select<Person, Pet>((p, pt) => new { PersonName = p.Name, PetName = pt.Name });
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual(
                "SELECT [t1].[Name] AS [PersonName], [t2].[Name] AS [PetName] FROM [Person] AS [t1]"
                + " RIGHT JOIN [Pet] AS [t2] ON [t1].[Id] = [t2].[OwnerId]"
                , sql);
        }

        [TestMethod]
        public void Join_WithMultipleConditions_TableAttr_Schema()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person3>(provider)
                .Where(person => person.Age > 18)
                .Join<Pet2>((person, pet) => person.Id == pet.OwnerId && pet.Name != null)
                .Select<Person3, Pet2>((p, pt) => new { PersonName = p.Name, PetName = pt.Name });
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual(
                "SELECT [t1].[Name] AS [PersonName], [t2].[Name] AS [PetName] FROM [xyz].[Person] AS [t1] "
                + "INNER JOIN [zz1].[Pet] AS [t2] ON ([t1].[Id] = [t2].[OwnerId] AND [t2].[Name] IS NOT NULL)\r\n"
                + "WHERE [t1].[Age] > @p0"
                , sql);
            Assert.IsTrue(sql.Contains("ON"));
            Assert.IsTrue(sql.Contains("AND"));
        }

        [TestMethod]
        public void Join_WithMultipleConditions_Projection()
        {
            var petDoB = DateTime.Now.AddYears(-4).Date;
            var petAge = 4;
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider)
                .Join<Pet>((person, pet) => person.Id == pet.OwnerId && pet.Name != null)
                .Where(p => p.Age > 18)
                .Select<Pet>((p) => new { Name = p.Name, Breed = "Poodle", Age = petAge, DOB = DateTime.Now });
            var (sql, parameters) = query.ToSql();
            Console.WriteLine($"SQL: {sql} \r\nParameters: {JsonSerializer.Serialize(parameters)}");
            Assert.AreEqual(
                "SELECT [t2].[Name] AS [Name], @p1 AS [Breed], @p2 AS [Age], @p3 AS [DOB] FROM [Person] AS [t1] "
                + "INNER JOIN [Pet] AS [t2] ON ([t1].[Id] = [t2].[OwnerId] AND [t2].[Name] IS NOT NULL)\r\n"
                + "WHERE [t1].[Age] > @p0"
                , sql);
        }

        [TestMethod]
        public void Join_WithMultipleConditions_ToPerson()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider)
                .Join<Pet>((person, pet) => person.Id == pet.OwnerId && pet.Name != null)
                .Select((p) => new Person { Name = p.Name, Age = 4 });
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.IsTrue(sql.Contains("JOIN"));
            Assert.IsTrue(sql.Contains("ON"));
            Assert.IsTrue(sql.Contains("AND"));
        }

        [TestMethod]
        public void Join_ChainedWithWhereAndSelect()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider)
                .Join<Pet>((person, pet) => person.Id == pet.OwnerId)
                .Where<Pet>((p, pt) => p.Age > 18 && pt.Name != null)
                .Select<Person, Pet>((p, pt) => new { PersonName = p.Name, PetName = pt.Name });
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual(
                "SELECT [t1].[Name] AS [PersonName], [t2].[Name] AS [PetName] FROM [Person] AS [t1] "
                + "INNER JOIN [Pet] AS [t2] ON [t1].[Id] = [t2].[OwnerId]\r\n"
                + "WHERE ([t1].[Age] > @p0 AND [t2].[Name] IS NOT NULL)"
                , sql);
        }

        [TestMethod]
        public void Where_CharEnum_Parameter()
        {
            var provider = GetProvider();
            //OrderTypeCode should be translated to its underlying char value in the generated SQL and parameters
            var query = new SqlQuery<OrderUUID>(provider).Where(x => x.TypeCode == OrderTypeCode.Standard);
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual("SELECT * FROM [OrderUUID] AS [t1]\r\nWHERE [t1].[TypeCode] = @p0", sql);
            Assert.IsTrue(parameters is ExpandoObject);
            //verify that the parameter value is the char 'S' corresponding to OrderTypeCode.Standard
            Assert.AreEqual('S', ((IDictionary<string, object>)parameters)["p0"]);
        }

        [TestMethod]
        public void Where_CharEnum_Parameter2()
        {
            var ot = OrderTypeCode.Standard;
            var provider = GetProvider();
            //OrderTypeCode should be translated to its underlying char value in the generated SQL and parameters
            var query = new SqlQuery<OrderUUID>(provider).Where(x => x.TypeCode == ot);
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual("SELECT * FROM [OrderUUID] AS [t1]\r\nWHERE [t1].[TypeCode] = @p0", sql);
            Assert.IsTrue(parameters is ExpandoObject);
            //verify that the parameter value is the char 'S' corresponding to OrderTypeCode.Standard
            Assert.AreEqual('S', ((IDictionary<string, object>)parameters)["p0"]);
        }

        [TestMethod]
        public void Where_IntEnum_Parameter()
        {
            var provider = GetProvider();
            // Regular enums without [CharEnum] should remain as integers
            var query = new SqlQuery<ProductWithStatus>(provider).Where(x => x.Status == ProductStatus.Active);
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual("SELECT * FROM [ProductWithStatus] AS [t1]\r\nWHERE [t1].[Status] = @p0", sql);
            Assert.IsTrue(parameters is ExpandoObject);
            // Verify that the parameter value is the int 1, not char
            Assert.AreEqual(1, ((IDictionary<string, object>)parameters)["p0"]);
            Assert.IsInstanceOfType(((IDictionary<string, object>)parameters)["p0"], typeof(int));
        }

        [TestMethod]
        public void Where_Contains()
        {
            var ids = new[] { Guid.Parse("0E3A8BCA-B633-430F-973F-A2DF9308E475"), Guid.Parse("5D79478E-1551-48FC-86E8-82E121015D6E"), Guid.Parse("EE6C6537-42DC-41B0-A192-BA54FB489038") };
            var provider = GetProvider();
            var query = new SqlQuery<OrderUUID>(provider).Where(x => ids.Contains(x.Id));
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual("SELECT * FROM [OrderUUID] AS [t1]\r\nWHERE [t1].[Id] IN ('0e3a8bca-b633-430f-973f-a2df9308e475', '5d79478e-1551-48fc-86e8-82e121015d6e', 'ee6c6537-42dc-41b0-a192-ba54fb489038')", sql);
        }

        [TestMethod]
        public void Where_Contains_WithTypeCast()
        {
            var ids = new[] { Guid.Parse("0E3A8BCA-B633-430F-973F-A2DF9308E475"), Guid.Parse("5D79478E-1551-48FC-86E8-82E121015D6E"), Guid.Parse("EE6C6537-42DC-41B0-A192-BA54FB489038") };
            var provider = GetProvider();
            var query = new SqlQuery<OrderUUIDNullable>(provider).Where(x => ids.Contains((Guid)x.Id!));
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual("SELECT * FROM [OrderUUIDNullable] AS [t1]\r\nWHERE [t1].[Id] IN ('0e3a8bca-b633-430f-973f-a2df9308e475', '5d79478e-1551-48fc-86e8-82e121015d6e', 'ee6c6537-42dc-41b0-a192-ba54fb489038')", sql);
        }

        [TestMethod]
        public void Where_NotContains()
        {
            var ids = new[] { "A", "B", "C" };
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider).Where(x => !ids.Contains(x.Name));
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual("SELECT * FROM [Person] AS [t1]\r\nWHERE [t1].[Name] NOT IN ('A', 'B', 'C')", sql);
        }

        [TestMethod]
        public void Where_StartsWith()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider).Where(x => x.Name.StartsWith("John"));
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual("SELECT * FROM [Person] AS [t1]\r\nWHERE [t1].[Name] LIKE @p0", sql);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual("John%", paramDict["p0"]);
        }

        [TestMethod]
        public void Where_StartsWith_WithParameter()
        {
            var prefix = "John";
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider).Where(x => x.Name.StartsWith(prefix));
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual("SELECT * FROM [Person] AS [t1]\r\nWHERE [t1].[Name] LIKE @p0", sql);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual("John%", paramDict["p0"]);
        }

        [TestMethod]
        public void Where_EndsWith()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider).Where(x => x.Name.EndsWith("son"));
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual("SELECT * FROM [Person] AS [t1]\r\nWHERE [t1].[Name] LIKE @p0", sql);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual("%son", paramDict["p0"]);
        }

        [TestMethod]
        public void Where_StringContains()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider).Where(x => x.Name.Contains("oh"));
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual("SELECT * FROM [Person] AS [t1]\r\nWHERE [t1].[Name] LIKE @p0", sql);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual("%oh%", paramDict["p0"]);
        }

        [TestMethod]
        public void Where_NotStartsWith()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider).Where(x => !x.Name.StartsWith("Test"));
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual("SELECT * FROM [Person] AS [t1]\r\nWHERE NOT ([t1].[Name] LIKE @p0)", sql);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual("Test%", paramDict["p0"]);
        }

        [TestMethod]
        public void Where_NotEndsWith()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider).Where(x => !x.Name.EndsWith("ing"));
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual("SELECT * FROM [Person] AS [t1]\r\nWHERE NOT ([t1].[Name] LIKE @p0)", sql);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual("%ing", paramDict["p0"]);
        }

        [TestMethod]
        public void Where_NotStringContains()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider).Where(x => !x.Name.Contains("xyz"));
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual("SELECT * FROM [Person] AS [t1]\r\nWHERE NOT ([t1].[Name] LIKE @p0)", sql);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual("%xyz%", paramDict["p0"]);
        }

        [TestMethod]
        public void Where_StartsWith_Combined()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider).Where(x => x.Name.StartsWith("John") && x.Age > 18);
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual("SELECT * FROM [Person] AS [t1]\r\nWHERE ([t1].[Name] LIKE @p0 AND [t1].[Age] > @p1)", sql);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual("John%", paramDict["p0"]);
            Assert.AreEqual(18, paramDict["p1"]);
        }

        [TestMethod]
        public void FromFunction()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider).FromFunction("GetPeople");
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual("SELECT * FROM [GetPeople]() AS [t1]", sql);
        }

        [TestMethod]
        public void FromFunction_WithPositionalParams()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider).FromFunction("GetPeople","John",19);
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual("SELECT * FROM [GetPeople](@p0,@p1) AS [t1]", sql);
        }

        [TestMethod]
        public void FromFunction_WithPositionalParams_AndPredicate()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider).FromFunction("GetPeople", "John", 19)
                .Where(x => x.Age > 18);
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual("SELECT * FROM [GetPeople](@p0,@p1) AS [t1]\r\nWHERE [t1].[Age] > @p2", sql);
        }

        [TestMethod]
        public void FromFunction_JoinWithMultipleConditions_ToPersonProjection()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider)
                .FromFunction("GetPeople","John")
                .Join<Pet>((person, pet) => person.Id == pet.OwnerId && pet.Name != null)
                .Select((p) => new Person { Name = p.Name, Age = 4 });
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual("SELECT [t1].[Name] AS [Name], @p1 AS [Age] FROM [GetPeople](@p0) AS [t1] INNER JOIN [Pet] AS [t2] ON ([t1].[Id] = [t2].[OwnerId] AND [t2].[Name] IS NOT NULL)",sql);
            Assert.IsTrue(parameters is ExpandoObject);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual("John", paramDict["p0"]);
        }

        [TestMethod]
        public void FromFunction_JoinWithMultipleConditions()
        {
            var firstName = "John";
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider)
                .FromFunction("GetPeopleByFirstName", firstName)
                .Join<Pet>((person, pet) => person.Id == pet.OwnerId && pet.Name != null);
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual("SELECT * FROM [GetPeopleByFirstName](@p0) AS [t1] INNER JOIN [Pet] AS [t2] ON ([t1].[Id] = [t2].[OwnerId] AND [t2].[Name] IS NOT NULL)", sql);
            Assert.IsTrue(parameters is ExpandoObject);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual("John", paramDict["p0"]);
        }

        [Table("DummyTable")]
        private class TestEntity
        {
            [Key]
            public int Id { get; set; }
            public string Name { get; set; }
        }

        [Table("DummyTable", Schema ="dbo")]
        private class TestEntity2
        {
            [Key]
            public int Id { get; set; }
            public string Name { get; set; }
        }

        [TestMethod]
        public void AndWhere_WithoutBaseWhere_GeneratesSingleWhereClause()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider)
                .AndWhere(x => x.Age > 18);
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual("SELECT * FROM [Person] AS [t1]\r\nWHERE [t1].[Age] > @p0", sql);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual(18, paramDict["p0"]);
        }

        [TestMethod]
        public void OrWhere_WithoutBaseWhere_GeneratesSingleWhereClause()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider)
                .OrWhere(x => x.Name == "John");
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual("SELECT * FROM [Person] AS [t1]\r\nWHERE [t1].[Name] = @p0", sql);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual("John", paramDict["p0"]);
        }

        [TestMethod]
        public void Where_AndWhere_CombinesWithAndOperator()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider)
                .Where(x => x.Age > 18)
                .AndWhere(x => x.Name == "John");
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual("SELECT * FROM [Person] AS [t1]\r\nWHERE [t1].[Age] > @p0 AND [t1].[Name] = @p1", sql);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual(18, paramDict["p0"]);
            Assert.AreEqual("John", paramDict["p1"]);
        }

        [TestMethod]
        public void Where_MultiAndWhere_CombinesWithAndOperator()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider)
                .Where(x => x.Age > 18)
                .AndWhere(x => x.Name == "John" && x.Name != "Paul" && x.Id > 0);
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual("SELECT * FROM [Person] AS [t1]\r\nWHERE [t1].[Age] > @p0 AND ([t1].[Name] = @p1 AND [t1].[Name] <> @p2 AND [t1].[Id] > @p3)", sql);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual(18, paramDict["p0"]);
            Assert.AreEqual("John", paramDict["p1"]);
        }

        [TestMethod]
        public void Where_OrWhere_CombinesWithOrOperator()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider)
                .Where(x => x.Age > 18)
                .OrWhere(x => x.Name == "John");
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual("SELECT * FROM [Person] AS [t1]\r\nWHERE [t1].[Age] > @p0 OR [t1].[Name] = @p1", sql);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual(18, paramDict["p0"]);
            Assert.AreEqual("John", paramDict["p1"]);
        }

        [TestMethod]
        public void Where_AndWhere_OrWhere_CombinesInOrder_ChainedSingularPredicates()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider)
                .Where(x => x.Age > 18)
                .AndWhere(x => x.Age < 65)
                .OrWhere(x => x.Name == "Admin");
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual("SELECT * FROM [Person] AS [t1]\r\nWHERE [t1].[Age] > @p0 AND [t1].[Age] < @p1 OR [t1].[Name] = @p2", sql);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual(18, paramDict["p0"]);
            Assert.AreEqual(65, paramDict["p1"]);
            Assert.AreEqual("Admin", paramDict["p2"]);
        }

        [TestMethod]
        public void AndWhere_ChainedMultiple_CombinesAllWithAnd_ChainedSingularPredicates()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider)
                .AndWhere(x => x.Age > 18)
                .AndWhere(x => x.Age < 65)
                .AndWhere(x => x.Name == "John");
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual("SELECT * FROM [Person] AS [t1]\r\nWHERE [t1].[Age] > @p0 AND [t1].[Age] < @p1 AND [t1].[Name] = @p2", sql);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual(18, paramDict["p0"]);
            Assert.AreEqual(65, paramDict["p1"]);
            Assert.AreEqual("John", paramDict["p2"]);
        }

        [TestMethod]
        public void AndWhere_WithJoin_AppliesWhereAfterJoin()
        {
            var provider = GetProvider();
            var query = new SqlQuery<Person>(provider)
                .Join<Pet>((person, pet) => person.Id == pet.OwnerId)
                .AndWhere(x => x.Age > 18)
                .Select<Person, Pet>((p, pt) => new { PersonName = p.Name, PetName = pt.Name });
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual(
                "SELECT [t1].[Name] AS [PersonName], [t2].[Name] AS [PetName] FROM [Person] AS [t1] "
                + "INNER JOIN [Pet] AS [t2] ON [t1].[Id] = [t2].[OwnerId]\r\n"
                + "WHERE [t1].[Age] > @p0", sql);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual(18, paramDict["p0"]);
        }

        [Table("Users")]
        private class AppUser
        {
            [Key]
            [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
            public int Id { get; set; }
            public string Name { get; set; }
        }

        [Table("Users", Schema ="dbo")]
        private class DboAppUser
        {
            [Key]
            [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
            public int Id { get; set; }
            public string Name { get; set; }
        }

        protected class PersonWithUser
        {
            [Key]
            [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
            public int Id { get; set; }
            public string Name { get; set; }
            public int Age { get; set; }
            public int UserId { get; set; }
        }

        [TestMethod]
        public void AndWhere_TwoJoins_ReferencingAllThreeEntities_CompoundPredicat()
        {
            var provider = GetProvider();
            var query = new SqlQuery<PersonWithUser>(provider)
                .Join<Pet>((person, pet) => person.Id == pet.OwnerId)
                .Join<AppUser>((person, user) => person.UserId == user.Id)
                .Where(person => person.Name != null)
                .AndWhere<Pet, AppUser>((person, pet, user) =>
                    person.Age > 18 && pet.Name != null && user.Id > 0)
                .Select<PersonWithUser, Pet, AppUser>((p, pt, u) =>
                    new { PersonName = p.Name, PetName = pt.Name, UserName = u.Name });
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual(
                "SELECT [t1].[Name] AS [PersonName], [t2].[Name] AS [PetName], [t3].[Name] AS [UserName] "
                + "FROM [PersonWithUser] AS [t1] "
                + "INNER JOIN [Pet] AS [t2] ON [t1].[Id] = [t2].[OwnerId] "
                + "INNER JOIN [Users] AS [t3] ON [t1].[UserId] = [t3].[Id]\r\n"
                + "WHERE [t1].[Name] IS NOT NULL AND ([t1].[Age] > @p0 AND [t2].[Name] IS NOT NULL AND [t3].[Id] > @p1)", sql);
            Assert.IsTrue(parameters is ExpandoObject);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual(18, paramDict["p0"]);
            Assert.AreEqual(0, paramDict["p1"]);
        }

        [TestMethod]
        public void AndWhere_TwoJoins_ThreeEntities_CustomMapping_CompoundPredicate()
        {
            var provider = GetProvider();
            provider.Translator.Entity2TableMapper = (entityType, tableName) =>
            { 
                return "schema."+tableName;
            };
            var query = new SqlQuery<PersonWithUser>(provider)
                .Join<Pet>((person, pet) => person.Id == pet.OwnerId)
                .Join<AppUser>((person, user) => person.UserId == user.Id)
                .Where(person => person.Name != null)
                .AndWhere<Pet, AppUser>((person, pet, user) =>
                    person.Age > 18 && pet.Name != null && user.Id > 0)
                .Select<PersonWithUser, Pet, AppUser>((p, pt, u) =>
                    new { PersonName = p.Name, PetName = pt.Name, UserName = u.Name });
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual(
                "SELECT [t1].[Name] AS [PersonName], [t2].[Name] AS [PetName], [t3].[Name] AS [UserName] "
                + "FROM [schema].[PersonWithUser] AS [t1] "
                + "INNER JOIN [schema].[Pet] AS [t2] ON [t1].[Id] = [t2].[OwnerId] "
                + "INNER JOIN [schema].[Users] AS [t3] ON [t1].[UserId] = [t3].[Id]\r\n"
                + "WHERE [t1].[Name] IS NOT NULL AND ([t1].[Age] > @p0 AND [t2].[Name] IS NOT NULL AND [t3].[Id] > @p1)", sql);
            Assert.IsTrue(parameters is ExpandoObject);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual(18, paramDict["p0"]);
            Assert.AreEqual(0, paramDict["p1"]);
        }

        [TestMethod]
        public void AndWhere_TwoJoins_ThreeEntities_OverrideSchema_CompoundPredicate()
        {
            var provider = GetProvider();
            
            var query = new SqlQuery<PersonWithUser>(provider)
                .Join<Pet>((person, pet) => person.Id == pet.OwnerId)
                .Join<AppUser>((person, user) => person.UserId == user.Id)
                .Where(person => person.Name != null)
                .AndWhere<Pet, AppUser>((person, pet, user) =>
                    person.Age > 18 && pet.Name != null && user.Id > 0)
                .Select<PersonWithUser, Pet, AppUser>((p, pt, u) =>
                    new { PersonName = p.Name, PetName = pt.Name, UserName = u.Name });
            var (sql, parameters) = query.ToSql(new QueryOptions { Schema = "xyz" });
            Console.WriteLine(sql);
            Assert.AreEqual(
                "SELECT [t1].[Name] AS [PersonName], [t2].[Name] AS [PetName], [t3].[Name] AS [UserName] "
                + "FROM [xyz].[PersonWithUser] AS [t1] "
                + "INNER JOIN [xyz].[Pet] AS [t2] ON [t1].[Id] = [t2].[OwnerId] "
                + "INNER JOIN [xyz].[Users] AS [t3] ON [t1].[UserId] = [t3].[Id]\r\n"
                + "WHERE [t1].[Name] IS NOT NULL AND ([t1].[Age] > @p0 AND [t2].[Name] IS NOT NULL AND [t3].[Id] > @p1)", sql);
            Assert.IsTrue(parameters is ExpandoObject);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual(18, paramDict["p0"]);
            Assert.AreEqual(0, paramDict["p1"]);
        }

        [TestMethod]
        public void AndWhere_TwoJoins_ThreeEntities_DefaultSchema_CompoundPredicate()
        {
            var provider = GetProvider();

            var query = new SqlQuery<PersonWithUser>(provider)
                .Join<Pet>((person, pet) => person.Id == pet.OwnerId)
                .Join<DboAppUser>((person, user) => person.UserId == user.Id)
                .Where(person => person.Name != null)
                .AndWhere<Pet, DboAppUser>((person, pet, user) =>
                    person.Age > 18 && pet.Name != null && user.Id > 0)
                .Select<PersonWithUser, Pet, DboAppUser>((p, pt, u) =>
                    new { PersonName = p.Name, PetName = pt.Name, UserName = u.Name });
            var (sql, parameters) = query.ToSql(new QueryOptions { DefaultSchema = "abc" });
            Console.WriteLine(sql);
            Assert.AreEqual(
                "SELECT [t1].[Name] AS [PersonName], [t2].[Name] AS [PetName], [t3].[Name] AS [UserName] "
                + "FROM [abc].[PersonWithUser] AS [t1] "
                + "INNER JOIN [abc].[Pet] AS [t2] ON [t1].[Id] = [t2].[OwnerId] "
                + "INNER JOIN [dbo].[Users] AS [t3] ON [t1].[UserId] = [t3].[Id]\r\n"
                + "WHERE [t1].[Name] IS NOT NULL AND ([t1].[Age] > @p0 AND [t2].[Name] IS NOT NULL AND [t3].[Id] > @p1)", sql);
            Assert.IsTrue(parameters is ExpandoObject);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual(18, paramDict["p0"]);
            Assert.AreEqual(0, paramDict["p1"]);
        }

        [TestMethod]
        public void OrWhere_TwoJoins_BuildPredicateExternally()
        {
            var provider = GetProvider();
            var query = new SqlQuery<PersonWithUser>(provider)
                .Join<Pet>((person, pet) => person.Id == pet.OwnerId)
                .Join<AppUser>((person, user) => person.UserId == user.Id)
                .Select<PersonWithUser, Pet, AppUser>((p, pt, u) =>
                    new { PersonName = p.Name, PetName = pt.Name, UserName = u.Name });
            SqlQueryTests_BuildPredicate(query);
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual("SELECT [t1].[Name] AS [PersonName], [t2].[Name] AS [PetName], [t3].[Name] AS [UserName] FROM [PersonWithUser] AS [t1] INNER JOIN [Pet] AS [t2] ON [t1].[Id] = [t2].[OwnerId] INNER JOIN [Users] AS [t3] ON [t1].[UserId] = [t3].[Id]\r\nWHERE [t1].[Name] = @p0 OR [t1].[Name] = @p1 OR [t1].[Name] = @p2", sql);
            Assert.IsTrue(parameters is ExpandoObject);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual("Alice", paramDict["p0"]);
            Assert.AreEqual("Bob", paramDict["p1"]);
            Assert.AreEqual("Charlie", paramDict["p2"]);
        }

        [TestMethod]
        public void OrWhere_TwoJoins_BuildCompoundPredicate_Externally()
        {
            var provider = GetProvider();
            var query = new SqlQuery<PersonWithUser>(provider)
                .Join<Pet>((person, pet) => person.Id == pet.OwnerId)
                .Join<AppUser>((person, user) => person.UserId == user.Id)
                .Select<PersonWithUser, Pet, AppUser>((p, pt, u) =>
                    new { PersonName = p.Name, PetName = pt.Name, UserName = u.Name });
            SqlQueryTests_BuildPredicate2(query);
            var (sql, parameters) = query.ToSql();
            Console.WriteLine(sql);
            Assert.AreEqual("SELECT [t1].[Name] AS [PersonName], [t2].[Name] AS [PetName], [t3].[Name] AS [UserName] FROM [PersonWithUser] AS [t1] INNER JOIN [Pet] AS [t2] ON [t1].[Id] = [t2].[OwnerId] INNER JOIN [Users] AS [t3] ON [t1].[UserId] = [t3].[Id]\r\nWHERE ([t1].[Name] = @p0 AND [t1].[Id] > @p1) OR ([t1].[Name] = @p2 AND [t1].[Id] > @p3) OR ([t1].[Name] = @p4 AND [t1].[Id] > @p5)", sql);
            Assert.IsTrue(parameters is ExpandoObject);
            var paramDict = (IDictionary<string, object>)parameters;
            Assert.AreEqual("Alice", paramDict["p0"]);
            Assert.AreEqual(3, paramDict["p1"]);
            Assert.AreEqual("Bob", paramDict["p2"]);
            Assert.AreEqual(3, paramDict["p3"]);
            Assert.AreEqual("Charlie", paramDict["p4"]);
            Assert.AreEqual(3, paramDict["p5"]);
        }

        protected void SqlQueryTests_BuildPredicate(SqlQuery<PersonWithUser> query)
        {
            var names = new List<string>() { "Alice", "Bob", "Charlie" };
            foreach (var name in names)
            {
                query.OrWhere(x => x.Name == name);
            }
        }

        protected void SqlQueryTests_BuildPredicate2(SqlQuery<PersonWithUser> query)
        {
            var names = new List<string>() { "Alice", "Bob", "Charlie" };
            foreach (var name in names)
            {
                query.OrWhere(x => x.Name == name && x.Id > 3);
            }
        }

        private class Department
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        private class Employee
        {
            public int Id { get; set; }
            public int DepartmentId { get; set; }
            public string Name { get; set; }
        }

        private class EmployeeDepartment
        {
            public string EmployeeName { get; set; }
            public string DepartmentName { get; set; }
        }


        private class Order
        {
            public int Id { get; set; }
            public int CustomerId { get; set; }
            public int ProductId { get; set; }
        }
        private class Customer
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }
        private class Product
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }
        private class OrderInfo
        {
            public string CustomerName { get; set; }
            public string ProductName { get; set; }
        }
        private class OrderCustomer
        {
            public Order o { get; set; }
            public Customer c { get; set; }
        }
        private class OrderCustomerProduct
        {
            public OrderCustomer oc { get; set; }
            public Product p { get; set; }
        }
        private class OrderUUID
        {
            public Guid Id { get; set; }
            public OrderTypeCode TypeCode { get; set; }
        }
        private class OrderUUIDNullable
        {
            public Guid? Id { get; set; }
            public OrderTypeCode TypeCode { get; set; }
        }

        [CharEnum]
        private enum OrderTypeCode
        {
            Standard = 'S',
            Express = 'E',
            International = 'I'
        }

        // Regular enum without [CharEnum] attribute - should remain as int
        private enum ProductStatus
        {
            Inactive = 0,
            Active = 1,
            Discontinued = 2
        }

        private class ProductWithStatus
        {
            public int Id { get; set; }
            public ProductStatus Status { get; set; }
        }
    }
}
