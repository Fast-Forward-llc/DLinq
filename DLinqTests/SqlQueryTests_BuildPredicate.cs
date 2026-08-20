﻿using DLinq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace DLinqTests
{
    [TestClass]
    public class SqlQueryTests_BuildPredicate
    {

        [TestMethod]
        public void BuildPredicate_Single()
        {
            var criteria = new DLinq.FilterCriteria[] {
                new DLinq.FilterCriteria(typeof(Person), "FirstName", ExpressionType.Equal, "TestName"),
            };
            var provider = new QueryProvider(new DummyDialect());
            var predicate = SqlQuery.BuildPredicate(criteria, "AND");
            Console.WriteLine(predicate);
            Assert.IsNotNull(predicate);
        }

        [TestMethod]
        public void BuildPredicate_Multiple()
        {
            var criteria = new DLinq.FilterCriteria[] {
                new DLinq.FilterCriteria(typeof(Person), "FirstName", ExpressionType.Equal, "TestName"),
                new DLinq.FilterCriteria(typeof(Person), "LastName", ExpressionType.Equal, "SurName"),
                new DLinq.FilterCriteria(typeof(Person), "Age", ExpressionType.GreaterThanOrEqual, 18),
                new DLinq.FilterCriteria(typeof(Person), "DOB", ExpressionType.LessThan, DateOnly.FromDateTime(DateTime.Now)),
                new DLinq.FilterCriteria(typeof(Person), "ExpireDate", ExpressionType.GreaterThan, DateTime.UtcNow)
            };
            var provider = new QueryProvider(new DummyDialect());
            var predicate = SqlQuery.BuildPredicate(criteria, "AND");
            Console.WriteLine(predicate);
            Assert.IsNotNull(predicate);
        }

        [TestMethod]
        public void BuildPredicate_Multi_Entities()
        {
            var petName = "Rover";
            var petBreed = "Labrador";
            var criteria = new DLinq.FilterCriteria[] {
                new DLinq.FilterCriteria(typeof(Person), "LastName", ExpressionType.Equal, "SurName"),
                new DLinq.FilterCriteria(typeof(Person), "Age", ExpressionType.GreaterThanOrEqual, 18),
                new DLinq.FilterCriteria(typeof(Pet), "Name", ExpressionType.Equal, petName),
                new DLinq.FilterCriteria(typeof(Pet), "Breed", ExpressionType.Equal, petBreed)
            };
            var provider = new QueryProvider(new DummyDialect());
            var predicate = SqlQuery.BuildPredicate(criteria, "AND");
            Console.WriteLine(predicate);
            Assert.IsNotNull(predicate);
        }

        public class Person
        {
            public string FirstName { get; set; }
            public string LastName { get; set; }
            public int Age { get; set; }
            public DateOnly DOB { get; set; }
            public DateTime ExpireDate { get; set; }
        }
        public class Pet
        {
            public string Name { get; set; }
            public string Breed { get; set; }
        }
    }
}