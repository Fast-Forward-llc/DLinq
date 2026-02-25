using System;

namespace DLinq
{
    /// <summary>
    /// Indicates that an enum with integer underlying type should be treated as having char values
    /// for database parameter conversion. Use this on enums defined with char literals like:
    /// [CharEnum] enum Status { Active = 'A', Inactive = 'I' }
    /// </summary>
    [AttributeUsage(AttributeTargets.Enum, AllowMultiple = false, Inherited = false)]
    public class CharEnumAttribute : Attribute
    {
    }
}
