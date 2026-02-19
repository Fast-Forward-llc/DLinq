using System.Collections.Generic;
using System.Linq.Expressions;

namespace DLinq
{
    // Abstract base for AST nodes
    public abstract class SqlAstNode { }

    public class SqlSelectNode : SqlAstNode
    {
        public Type FromEntity { get; set; }
        public string FromTable { get; set; }
        public string TableAlias { get; set; } // NEW: auto-generated alias
        public List<Column> Columns { get; set; } = new List<Column>();
        public List<SqlJoin> Joins { get; set; } = new List<SqlJoin>(5);
        public Expression? WhereExpr { get; set; }
        public string? WhereSqlExpr { get; set; }
        public List<string> PrimaryKeys { get; set; } = new List<string>();
        public int? Skip { get; set; }
        public int? Take { get; set; }
        public SqlFunctionSource FromFunction { get; set; }
        public List<(Column Column, bool Descending)> OrderBy { get; set; } = new List<(Column, bool)>();
        public Expression SelectExpr { get; set; }
        public List<(LambdaExpression Expression, bool Descending)> OrderByExpr { get; set; } = new List<(LambdaExpression, bool)>(3);
    }

    public class SqlFunctionSource
    {
        public string FunctionName { get; set; }
        public List<object> Arguments { get; set; } = new();
    }

    public class SqlWhereNode : SqlAstNode
    {
        public Column Column { get; set; }
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
        public bool IsLiteralValue { get; set; } = false; // indicates if this column represents a literal value instead of a table column
        public Column(string? schema, string table, string name, string? alias = null, bool isLiteralValue = false)
        {
            Schema = schema;
            Table = table;
            Name = name;
            Alias = alias;
            IsLiteralValue = isLiteralValue;
        }
    }

    public class SqlJoin
    {  
        public string JoinType { get; set; } = "INNER";
        public string LeftTable { get; set; }
        public string RightTable { get; set; }
        public string RightAlias { get; set; }
        public string OnClause { get; set; }
    }
    public class SqlJoin<TLeft, TRight>:SqlJoin
    {
        public Expression<Func<TLeft, TRight, bool>> onPredicate { get; set; }
    }
}