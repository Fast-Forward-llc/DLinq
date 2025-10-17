using System.Collections.Generic;

namespace DLinq
{
    // Abstract base for AST nodes
    public abstract class SqlAstNode { }

    public class SqlSelectNode : SqlAstNode
    {
        public string Table { get; set; }
        public List<string> Columns { get; set; } = new();
        public SqlWhereNode Where { get; set; }
        public string WhereSql { get; set; } // Raw SQL predicate for complex expressions
        public List<string> PrimaryKeys { get; set; } = new();
        public int? Skip { get; set; }
        public int? Take { get; set; }
        public SqlFunctionSource FromFunction { get; set; } // TVF support
        public List<(string Column, bool Descending)> OrderBy { get; set; } = new(); // OrderBy support
        public List<SqlJoinNode> Joins { get; set; } = new(); // Join support
    }

    public class SqlJoinNode
    {
        public string Table { get; set; }
        public string LeftColumn { get; set; }
        public string RightColumn { get; set; }
        public string JoinType { get; set; } // e.g., "INNER", "LEFT"
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
}