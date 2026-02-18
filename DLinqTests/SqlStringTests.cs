using DLinq;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DLinqTests
{
    [TestClass]
    public class SqlStringTests
    {
        private SqlServerDialect sqldialect = new SqlServerDialect();
        private PostgresDialect pgdialect = new PostgresDialect(PostgresDialect.DialectOptions.ForceLowerCase);
      

        [TestMethod]
        public void SqlString_Postgres_TableAndIdentifierAndParameter()
        {
            var sqlString = new SqlString(pgdialect);
            var minAge = 10;
            FormattableString sqlTemplate = $"SELECT {nameof(Person.Name):I}, {nameof(Person.Age):I} FROM {sqlString.TableName<Person>()} WHERE {nameof(Person.Age):I} > {nameof(minAge):P}";
            var sql = sqlString.Format(sqlTemplate);
            Console.WriteLine($"{sql}");
            Assert.AreEqual("SELECT \"name\", \"age\" FROM \"person\" WHERE \"age\" > @minAge", sql);
        }

        [TestMethod]
        public void SqlString_Postgres_TableAndColumnAndParameter()
        {
            var sqlString = new SqlString(pgdialect);
            var minAge = 10;
            FormattableString sqlTemplate = $"SELECT {sqlString.ColumnName<Person>(p => p.Name)}, {sqlString.ColumnName<Person>(p => p.Age)} FROM {sqlString.TableName<Person>()} WHERE {nameof(Person.Age):I} > {nameof(minAge):P}";
            var sql = sqlString.Format(sqlTemplate);
            Console.WriteLine($"{sql}");
            Assert.AreEqual("SELECT \"name\", \"age\" FROM \"person\" WHERE \"age\" > @minAge", sql);
        }

        [TestMethod]
        public void SqlString_Postgres_TableAndColumnAttributeAndParameter()
        {
            var sqlString = new SqlString(pgdialect);
            var minAge = 10;
            FormattableString sqlTemplate = $"SELECT {sqlString.ColumnName<Person2>(p => p.FullName)}, {sqlString.ColumnName<Person2>(p => p.Age)} FROM {sqlString.TableName<Person2>()} WHERE {nameof(Person2.Age):I} > {nameof(minAge):P}";
            var sql = sqlString.Format(sqlTemplate);
            Console.WriteLine($"{sql}");
            Assert.AreEqual("SELECT \"name\" AS \"fullname\", \"age\" FROM \"person\" WHERE \"age\" > @minAge", sql);
        }

        [TestMethod]
        public void SqlString_SqlServer_TableAndIdentifierAndParameter()
        {
            var sqlString = new SqlString(sqldialect);
            var minAge = 10;
            FormattableString sqlTemplate = $"SELECT {nameof(Person.Name):I}, {nameof(Person.Age):I} FROM {sqlString.TableName<Person>()} WHERE {nameof(Person.Age):I} > {nameof(minAge):P}";
            var sql = sqlString.Format(sqlTemplate);
            Console.WriteLine($"{sql}");
            Assert.AreEqual("SELECT [Name], [Age] FROM [Person] WHERE [Age] > @minAge", sql);
        }

        [TestMethod]
        public void SqlString_SqlServer_TableAndColumnAndParameter()
        {
            var sqlString = new SqlString(sqldialect);
            var minAge = 10;
            FormattableString sqlTemplate = $"SELECT {sqlString.ColumnName<Person>(p => p.Name)}, {sqlString.ColumnName<Person>(p => p.Age)} FROM {sqlString.TableName<Person>()} WHERE {nameof(Person.Age):I} > {nameof(minAge):P}";
            var sql = sqlString.Format(sqlTemplate);
            Console.WriteLine($"{sql}");
            Assert.AreEqual("SELECT [Name], [Age] FROM [Person] WHERE [Age] > @minAge", sql);
        }

        [TestMethod]
        public void SqlString_SqlServer_TableAndColumnAttributeAndParameter()
        {
            var sqlString = new SqlString(sqldialect);
            var minAge = 10;
            FormattableString sqlTemplate = $"SELECT {sqlString.ColumnName<Person2>(p => p.FullName)}, {sqlString.ColumnName<Person2>(p => p.Age)} FROM {sqlString.TableName<Person2>()} WHERE {nameof(Person2.Age):I} > {nameof(minAge):P}";
            var sql = sqlString.Format(sqlTemplate);
            Console.WriteLine($"{sql}");
            Assert.AreEqual("SELECT [Name] AS [FullName], [Age] FROM [person] WHERE [Age] > @minAge", sql);
        }

        private class Person
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public int Age { get; set; }
            public int CreatedByUserId { get; set; } = 1;
            public int ModifiedByUserId { get; set; } = 1;
        }

        [Table("person")]
        private class Person2
        {
            public int Id { get; set; }
            [Column("Name")]
            public string FullName { get; set; }
            public int Age { get; set; }
            public int CreatedByUserId { get; set; } = 1;
            public int ModifiedByUserId { get; set; } = 1;
        }
        private class Pet
        {
            public int Id { get; set; }
            public int OwnerId { get; set; }
            public string Name { get; set; }
        }

        private class PersonPet
        {
            public string PersonName { get; set; }
            public string PetName { get; set; }
        }
    }
}
