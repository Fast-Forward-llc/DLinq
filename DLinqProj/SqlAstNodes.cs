using System.Collections.Generic;

namespace DLinq
{
    // Abstract base for AST nodes
    public abstract class SqlAstNode { }

    public class SqlSelectNode : SqlAstNode
    {
        public string Table { get; set; }
        public string Alias { get; set; } // NEW: auto-generated alias
        public List<Column> Columns { get; set; } = new List<Column>();
        public SqlWhereNode Where { get; set; }
        public string WhereSql { get; set; }
        public List<string> PrimaryKeys { get; set; } = new List<string>();
        public int? Skip { get; set; }
        public int? Take { get; set; }
        public SqlFunctionSource FromFunction { get; set; }
        public List<(string Column, bool Descending)> OrderBy { get; set; } = new List<(string, bool)>();
    }

    public class SqlFunctionSource
    {
        public string FunctionName { get; set; }
        public List<object> Arguments { get; set; } = new();
    }

    public class SqlWhereNode : SqlAstNode
    {
        public string Column { get; set; }
        public string Operator { get; set; }
        public object Value { get; set; } // Can be IEnumerable<object> or SqlSelectNode (subquery)
        public bool IsSubQuery { get; set; } = false;
    }

    public class Column
    {
        public string? Schema { get; set; }
        public string Table { get; set; }
        public string Name { get; set; }
        public string? Alias { get; set; }
        public Column(string? schema, string table, string name, string? alias = null)
        {
            Schema = schema;
            Table = table;
            Name = name;
            Alias = alias;
        }
    }

    public class SqlJoinSelectNode : SqlSelectNode
    {
        public List<SqlJoin> Joins { get; set; } = new List<SqlJoin>();
    }

    public class SqlJoin
    {
        public string JoinType { get; set; } = "INNER";
        public string RightTable { get; set; }
        public string RightAlias { get; set; }
        public string OnClause { get; set; }
    }
}