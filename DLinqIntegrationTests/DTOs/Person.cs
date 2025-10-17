using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DLinqIntegrationTests.DTOs
{
    public class Person
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int? Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public int Age { get; set; }
        public DateTime CreateDateUTC { get; set; } = DateTime.UtcNow;
    }

    [Table("Person")]
    public class Person2: Person;

    public class PersonUUID
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public int Age { get; set; }
        public DateTime CreateDateUTC { get; set; } = DateTime.UtcNow;
    }

    [Table("PersonUUID")]
    public class PersonCK
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        public string FirstName { get; set; }
        [Key]
        public string LastName { get; set; }
        public int Age { get; set; }
        public DateTime CreateDateUTC { get; set; } = DateTime.UtcNow;
    }

    [Table("Person")]
    public class PersonUpdateName
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
    }
}
