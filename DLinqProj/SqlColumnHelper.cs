using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Reflection;

namespace DLinq
{
    public static class SqlColumnHelper
    {
        public static List<Column> BuildColumnsFromType(Type entityType)
        {
            var columns = new List<Column>();
            var tableAttr = entityType.GetCustomAttribute<TableAttribute>();
            var schema = tableAttr?.Schema;
            var tableName = tableAttr?.Name ?? entityType.Name;
            foreach (var prop in entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.GetCustomAttribute<NotMappedAttribute>() != null)
                    continue;
                var colAttr = prop.GetCustomAttribute<ColumnAttribute>();
                var colName = colAttr?.Name ?? prop.Name;
                columns.Add(new Column(schema, tableName, colName));
            }
            return columns;
        }
    }
}
