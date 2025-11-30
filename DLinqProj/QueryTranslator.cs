using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using System.Reflection;
using System.Dynamic;
using System.Linq;

namespace DLinq
{
    /// <summary>
    /// Translates LINQ expression trees into SQL statements using a provided SQL dialect.
    /// Supports SELECT, INSERT, UPDATE, and basic JOIN/ORDER/WHERE/IN operations.
    /// </summary>
    public class QueryTranslator
    {
        private readonly ISqlDialect _dialect;

        /// <summary>
        /// Initializes a new instance of QueryTranslator with the specified SQL dialect.
        /// </summary>
        /// <param name="dialect">SQL dialect implementation for formatting SQL statements.</param>
        public QueryTranslator(ISqlDialect dialect)
        {
            _dialect = dialect;
        }

        /// <summary>
        /// Exposes the ISqlDialect instance used by this QueryTranslator.
        /// </summary>
        public ISqlDialect Dialect => _dialect;

        // Helper to evaluate any expression (variable, property, constant, etc.)
        private static object GetValueFromExpression(Expression expr)
        {
            if (expr is ConstantExpression c)
                return c.Value;
            var lambda = Expression.Lambda(expr);
            var compiled = lambda.Compile();
            return compiled.DynamicInvoke();
        }

        /// <summary>
        /// Parses a predicate expression (e.g., from a Where clause) into SQL syntax and collects parameters.
        /// Supports AND/OR, comparison, IN/NOT IN, and basic member access.
        /// </summary>
        /// <param name="expr">The predicate expression to parse.</param>
        /// <param name="parameters">List to collect parameter values for SQL statement.</param>
        /// <param name="entityType">Type of the entity being queried.</param>
        /// <returns>SQL WHERE clause string.</returns>
        private string ParsePredicate(Expression expr, List<object> parameters, Type entityType)
        {
            if (expr is BinaryExpression binary)
            {
                if (binary.NodeType == ExpressionType.AndAlso || binary.NodeType == ExpressionType.OrElse)
                {
                    var left = ParsePredicate(binary.Left, parameters, entityType);
                    var right = ParsePredicate(binary.Right, parameters, entityType);
                    var op = binary.NodeType == ExpressionType.AndAlso ? "AND" : "OR";
                    return $"({left}) {op} ({right})";
                }
                // Comparison operators
                MemberExpression member = null;
                object constantValue = null;

                // Handle left side as member, right side as value
                if (binary.Left is MemberExpression leftMember)
                {
                    member = leftMember;
                    constantValue = GetValueFromExpression(binary.Right);
                }
                // Handle right side as member, left side as value
                else if (binary.Right is MemberExpression rightMember)
                {
                    member = rightMember;
                    constantValue = GetValueFromExpression(binary.Left);
                }

                if (member != null && constantValue != null)
                {
                    var prop = entityType.GetProperty(member.Member.Name);
                    var colAttr = prop?.GetCustomAttribute<ColumnAttribute>();
                    var colName = colAttr?.Name ?? member.Member.Name;
                    string sqlOp = binary.NodeType switch
                    {
                        ExpressionType.Equal => "=",
                        ExpressionType.NotEqual => "!=",
                        ExpressionType.GreaterThan => ">",
                        ExpressionType.GreaterThanOrEqual => ">=",
                        ExpressionType.LessThan => "<",
                        ExpressionType.LessThanOrEqual => "<=",
                        _ => throw new NotSupportedException()
                    };
                    parameters.Add(constantValue);
                    return $"{_dialect.FormatColumn(colName)} {sqlOp} {_dialect.ParameterPlaceholder(parameters.Count - 1)}";
                }
            }
            // IN/NOT IN support
            if (expr is MethodCallExpression containsCall && containsCall.Method.Name == "Contains")
            {
                var member = containsCall.Arguments[0] as MemberExpression;
                var valuesExpr = containsCall.Object ?? containsCall.Arguments[0];
                IEnumerable<object> values = null;
                if (member == null && containsCall.Arguments.Count == 2)
                {
                    member = containsCall.Arguments[1] as MemberExpression;
                    valuesExpr = containsCall.Arguments[0];
                }
                if (member != null)
                {
                    values = GetValueFromExpression(valuesExpr) as IEnumerable<object>;
                    var prop = entityType.GetProperty(member.Member.Name);
                    var colAttr = prop?.GetCustomAttribute<ColumnAttribute>();
                    var colName = colAttr?.Name ?? member.Member.Name;
                    var paramNames = new List<string>();
                    foreach (var v in values ?? Enumerable.Empty<object>())
                    {
                        parameters.Add(v);
                        paramNames.Add(_dialect.ParameterPlaceholder(parameters.Count - 1));
                    }
                    return $"{_dialect.FormatColumn(colName)} IN ({string.Join(", ", paramNames)})";
                }
            }
            // NOT IN support
            if (expr is UnaryExpression unary && unary.NodeType == ExpressionType.Not)
            {
                if (unary.Operand is MethodCallExpression notContainsCall && notContainsCall.Method.Name == "Contains")
                {
                    var member = notContainsCall.Arguments[0] as MemberExpression;
                    var valuesExpr = notContainsCall.Object ?? notContainsCall.Arguments[0];
                    IEnumerable<object> values = null;
                    if (member == null && notContainsCall.Arguments.Count == 2)
                    {
                        member = notContainsCall.Arguments[1] as MemberExpression;
                        valuesExpr = notContainsCall.Arguments[0];
                    }
                    if (member != null)
                    {
                        values = GetValueFromExpression(valuesExpr) as IEnumerable<object>;
                        var prop = entityType.GetProperty(member.Member.Name);
                        var colAttr = prop?.GetCustomAttribute<ColumnAttribute>();
                        var colName = colAttr?.Name ?? member.Member.Name;
                        var paramNames = new List<string>();
                        foreach (var v in values ?? Enumerable.Empty<object>())
                        {
                            parameters.Add(v);
                            paramNames.Add(_dialect.ParameterPlaceholder(parameters.Count - 1));
                        }
                        return $"{_dialect.FormatColumn(colName)} NOT IN ({string.Join(", ", paramNames)})";
                    }
                }
            }
            throw new NotSupportedException("Unsupported predicate expression.");
        }

        /// <summary>
        /// Translates a LINQ expression tree into a SQL SELECT statement.
        /// Handles Skip, Take, Where, OrderBy, Join, and TVF (table-valued function) operations.
        /// </summary>
        /// <param name="expression">LINQ expression tree to translate.</param>
        /// <param name="parameters">Output list of parameter values for SQL statement.</param>
        /// <returns>SQL SELECT statement string.</returns>
        public string Translate(Expression expression, out List<object> parameters)
        {
            parameters = new List<object>();
            SqlSelectNode ast = null;
            int? skip = null;
            int? take = null;
            SqlFunctionSource fromFunction = null;
            var orderBy = new List<(string Column, bool Descending)>();
            var joins = new List<SqlJoinNode>();
            string whereSql = null;
            Type entityType = null;

            // Declare columns here so it can be set by projection
            List<Column> columns = null;
            var primaryKeys = new List<string>();

            Expression current = expression;
            while (current is MethodCallExpression mce)
            {
                switch (mce.Method.Name)
                {
                    case "Skip":
                        HandleSkip(mce, ref skip, ref current);
                        break;
                    case "Take":
                        HandleTake(mce, ref take, ref current);
                        break;
                    case "FromFunction":
                        HandleFromFunction(mce, ref fromFunction, ref current);
                        break;
                    case "OrderBy":
                    case "OrderByDescending":
                    case "ThenBy":
                    case "ThenByDescending":
                        HandleOrderBy(mce, orderBy, ref current);
                        break;
                    case "Join":
                        HandleJoin(mce, joins, ref current);
                        break;
                    case "Where":
                        HandleWhere(mce, parameters, ref whereSql, ref entityType, ref current);
                        break;
                    case "Select":
                        var selectorLambda = (LambdaExpression)((UnaryExpression)mce.Arguments[1]).Operand;
                        var projectedColumns = ParseProjectionColumns(selectorLambda.Body, _dialect);
                        if (projectedColumns != null && projectedColumns.Count > 0)
                        {
                            columns = projectedColumns;
                        }
                        current = mce.Arguments[0];
                        break;
                    default:
                        current = (current as MethodCallExpression)?.Arguments[0];
                        break;
                }
            }

            // Try to infer entityType if not set by Where
            if (entityType == null && current != null)
            {
                var type = current.GetType();
                if (type.IsGenericType)
                {
                    if (type.GetGenericTypeDefinition() == typeof(SqlQuery<>))
                    {
                        entityType = type.GetGenericArguments()[0];
                      }
                    else if (typeof(IQueryable).IsAssignableFrom(type))
                    {
                        entityType = type.GetGenericArguments()[0];
                    }
                }
            }

            if (entityType == null && expression.Type.IsGenericType)
            {
                entityType = expression.Type.GetGenericArguments().FirstOrDefault();
            }

            if (entityType != null && entityType.IsGenericType && entityType.GetGenericTypeDefinition().Name == "Pair`2")
            {
                var leftType = entityType.GetGenericArguments()[0];
                entityType = leftType;
                // Optionally: record rightType for join validation
            }

            if (entityType == null)
            {
                throw new InvalidOperationException("Unable to determine entity type for SQL translation. Ensure your query targets a valid entity type.");
            }

            var tableAttr = entityType.GetCustomAttribute<TableAttribute>();
            var tableName = tableAttr?.Name ?? entityType.Name;

            // Only build columns from entityType if not set by projection
            if (columns == null)
            {
                var properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                columns = new List<Column>();
                foreach (var prop in properties)
                {
                    if (prop.GetCustomAttribute<NotMappedAttribute>() != null)
                        continue;
                    var colAttr = prop.GetCustomAttribute<ColumnAttribute>();
                    var colName = colAttr?.Name ?? prop.Name;
                    columns.Add(new Column(null, tableName, colName));
                    if (prop.GetCustomAttribute<KeyAttribute>() != null)
                        primaryKeys.Add(colName);
                }
            }
            else
            {
                // If columns are set by projection, primaryKeys is not relevant for the select
                primaryKeys = new List<string>();
            }
 
            ast = new SqlSelectNode
            {
                Table = tableName,
                Columns = columns,
                Where = null,
                WhereSql = whereSql,
                PrimaryKeys = primaryKeys,
                Skip = skip,
                Take = take,
                FromFunction = fromFunction,
                OrderBy = orderBy,
                Joins = joins
            };
            return _dialect.SelectStatement(ast, parameters);
        }

        private static void HandleSkip(MethodCallExpression mce, ref int? skip, ref Expression current)
        {
            skip = (int)((ConstantExpression)mce.Arguments[1]).Value;
            current = mce.Arguments[0];
        }

        private static void HandleTake(MethodCallExpression mce, ref int? take, ref Expression current)
        {
            take = (int)((ConstantExpression)mce.Arguments[1]).Value;
            current = mce.Arguments[0];
        }

        private static void HandleFromFunction(MethodCallExpression mce, ref SqlFunctionSource fromFunction, ref Expression current)
        {
            var fnName = (string)((ConstantExpression)mce.Arguments[1]).Value;
            var argsExpr = (NewArrayExpression)mce.Arguments[2];
            var args = argsExpr.Expressions.Select(e => ((ConstantExpression)e).Value).ToList();
            fromFunction = new SqlFunctionSource { FunctionName = fnName, Arguments = args };
            current = mce.Arguments[0];
        }

        private static void HandleOrderBy(MethodCallExpression mce, List<(string Column, bool Descending)> orderBy, ref Expression current)
        {
            var lambda = (LambdaExpression)((UnaryExpression)mce.Arguments[1]).Operand;
            var member = lambda.Body as MemberExpression;
            if (member == null)
                throw new NotSupportedException("Only simple member OrderBy/ThenBy supported.");
            var colName = member.Member.Name;
            bool descending = mce.Method.Name == "OrderByDescending" || mce.Method.Name == "ThenByDescending";
            orderBy.Insert(0, (colName, descending));
            current = mce.Arguments[0];
        }

        private static void HandleJoin(MethodCallExpression mce, List<SqlJoinNode> joins, ref Expression current)
        {
            // Determine whether the call is an instance-style call (mce.Object != null)
            // or a static/extension call (mce.Object == null) and pick argument indexes accordingly.
            Expression outer;
            Expression inner;
            LambdaExpression outerKeyLambda;
            LambdaExpression innerKeyLambda;

            if (mce.Object != null)
            {
                // instance-call form: outer.Join(inner, outerKey, innerKey, resultSelector)
                outer = mce.Object;
                inner = mce.Arguments[0];
                outerKeyLambda = GetLambda(mce.Arguments[1]);
                innerKeyLambda = GetLambda(mce.Arguments[2]);
            }
            else
            {
                // static/extension-call form: Join(outer, inner, outerKey, innerKey, resultSelector)
                outer = mce.Arguments[0];
                inner = mce.Arguments[1];
                outerKeyLambda = GetLambda(mce.Arguments[2]);
                innerKeyLambda = GetLambda(mce.Arguments[3]);
            }

            // Helper to extract lambda from argument
            static LambdaExpression GetLambda(Expression arg)
            {
                if (arg is LambdaExpression le)
                    return le;
                if (arg is UnaryExpression ue && ue.NodeType == ExpressionType.Quote && ue.Operand is LambdaExpression quotedLambda)
                    return quotedLambda;
                throw new NotSupportedException($"Join key selector argument must be a lambda expression (possibly quoted). Got: {arg}");
            }

            // Helper to extract member from lambda body
            static MemberExpression FindMember(Expression expr)
            {
                while (expr is UnaryExpression ue &&
                       (ue.NodeType == ExpressionType.Convert ||
                        ue.NodeType == ExpressionType.ConvertChecked ||
                        ue.NodeType == ExpressionType.Quote))
                {
                    expr = ue.Operand;
                }
                if (expr is MemberExpression me)
                    return me;
                throw new NotSupportedException($"Join key selectors must be or contain a member expression. Got: {expr}");
            }

            var outerKeyMember = FindMember(outerKeyLambda.Body);
            var innerKeyMember = FindMember(innerKeyLambda.Body);

            if (outerKeyMember.Type != innerKeyMember.Type)
                throw new InvalidOperationException($"Join key types do not match: {outerKeyMember.Type} vs {innerKeyMember.Type}");

            var outerType = outerKeyMember.Expression.Type;
            var innerType = innerKeyMember.Expression.Type;
            var outerTable = outerType.GetCustomAttribute<TableAttribute>()?.Name ?? outerType.Name;
            var innerTable = innerType.GetCustomAttribute<TableAttribute>()?.Name ?? innerType.Name;

            var outerCol = outerKeyMember.Member.Name;
            var innerCol = innerKeyMember.Member.Name;

            joins.Add(new SqlJoinNode
            {
                Table = innerTable,
                LeftColumn = outerCol,
                RightColumn = innerCol,
                JoinType = "INNER",
                OnColumns = new List<SqlJoinOnColumn>
                {
                    new SqlJoinOnColumn
                    {
                        LeftTable = outerTable,
                        LeftColumn = outerCol,
                        RightTable = innerTable,
                        RightColumn = innerCol
                    }
                }
            });

            // Set current to the outer sequence for further processing
            current = outer;
        }

        private void HandleWhere(MethodCallExpression mce, List<object> parameters, ref string whereSql, ref Type entityType, ref Expression current)
        {
            var whereLambda = (LambdaExpression)((UnaryExpression)mce.Arguments[1]).Operand;
            entityType = mce.Arguments[0].Type.GetGenericArguments()[0];
            var thisWhereSql = ParsePredicate(whereLambda.Body, parameters, entityType);
            whereSql = whereSql == null ? thisWhereSql : $"({thisWhereSql}) AND ({whereSql})";
            current = mce.Arguments[0];
        }

        /// <summary>
        /// Generates an INSERT SQL statement for the given entity object.
        /// Skips properties marked as NotMapped, Identity, or Computed.
        /// </summary>
        /// <param name="entity">Entity object to insert.</param>
        /// <param name="options">Mutation options (optional, includes TableName).</param>
        /// <returns>Tuple of SQL string and parameters object.</returns>
        public (string sql, object parameters) GenerateInsertSql(object entity, InsertOptions? options = null)
        {
            options ??= new InsertOptions();
            var entityType = entity.GetType();
            var tableAttr = entityType.GetCustomAttribute<TableAttribute>();
            var tableName = options.TableName ?? tableAttr?.Name ?? entityType.Name;
            var properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var columns = new List<string>();
            var paramNames = new List<string>();
            var paramDict = new Dictionary<string, object>();
            var keyInfo = new List<(string colName, object? value, bool isIdentity)>();
            foreach (var prop in properties)
            {
                if (prop.GetCustomAttribute<NotMappedAttribute>() != null)
                    continue;
                var colAttr = prop.GetCustomAttribute<ColumnAttribute>();
                var colName = colAttr?.Name ?? prop.Name;
                var dbGenAttr = prop.GetCustomAttribute<DatabaseGeneratedAttribute>();
                bool isIdentity = dbGenAttr != null && dbGenAttr.DatabaseGeneratedOption == DatabaseGeneratedOption.Identity;
                if (prop.GetCustomAttribute<KeyAttribute>() != null && colName != null)
                    keyInfo.Add((colName, prop.GetValue(entity), isIdentity));
                if (dbGenAttr != null && (dbGenAttr.DatabaseGeneratedOption == DatabaseGeneratedOption.Identity || dbGenAttr.DatabaseGeneratedOption == DatabaseGeneratedOption.Computed))
                    continue;
                var paramName = "@" + colName;
                columns.Add(colName);
                paramNames.Add(paramName);
                paramDict[paramName] = prop.GetValue(entity);
            }
            var sql = _dialect.InsertStatement(tableName, columns, paramNames, options);
            if (options.SelectAfterMutation && keyInfo.Count > 0)
            {
                var selectColumns = properties
                    .Where(p => p.GetCustomAttribute<NotMappedAttribute>() == null)
                    .Select(p => {
                        var colAttr = p.GetCustomAttribute<ColumnAttribute>();
                        var colName = colAttr?.Name ?? p.Name;
                        return new Column(null, tableName, colName);
                    })
                    .ToList();
                var selectAst = new SqlSelectNode
                {
                    Table = tableName,
                    Columns = selectColumns,
                    WhereSql = GenerateIdentityWhereClause(entityType, tableName, keyInfo),
                    PrimaryKeys = keyInfo.Select(k => k.colName).ToList()
                };
                sql += "; " + _dialect.SelectStatement(selectAst, new List<object>());
            }
            var parameters = ToAnonymousObject(paramDict);
            return (sql, parameters);
        }

        private string GenerateIdentityWhereClause(Type entityType, string tableName, List<(string colName, object? value, bool isIdentity)> keyInfo)
        {
            var whereParts = keyInfo.Select(key =>
                key.isIdentity
                    ? $"{_dialect.FormatColumn(key.colName)} = {_dialect.IdentityValueExpression(tableName, key.colName)}"
                    : $"{_dialect.FormatColumn(key.colName)} = @{key.colName}"
            );
            return string.Join(" AND ", whereParts);
        }

        /// <summary>
        /// Generates an UPDATE SQL statement for the given entity object.
        /// Skips properties marked as NotMapped or Computed.
        /// Uses primary key(s) for WHERE clause.
        /// </summary>
        /// <param name="entity">Entity object to update.</param>
        /// <param name="options">Mutation options (optional, includes TableName).</param>
        /// <returns>Tuple of SQL string and parameters object.</returns>
        public (string sql, object parameters) GenerateUpdateSql(object entity, UpdateOptions? options = null)
        {
            options ??= new UpdateOptions();
            var entityType = entity.GetType();
            var tableAttr = entityType.GetCustomAttribute<TableAttribute>();
            var tableName = options.TableName ?? tableAttr?.Name ?? entityType.Name;
            var properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var setDict = new Dictionary<string, object>();
            var whereDict = new Dictionary<string, object>();
            var primaryKeys = new List<(string colName, object value)>();
            foreach (var prop in properties)
            {
                if (prop.GetCustomAttribute<NotMappedAttribute>() != null)
                    continue;
                var dbGenAttr = prop.GetCustomAttribute<DatabaseGeneratedAttribute>();
                if (dbGenAttr != null && dbGenAttr.DatabaseGeneratedOption == DatabaseGeneratedOption.Computed)
                    continue;
                var colAttr = prop.GetCustomAttribute<ColumnAttribute>();
                var colName = colAttr?.Name ?? prop.Name;
                var value = prop.GetValue(entity);
                if (prop.GetCustomAttribute<KeyAttribute>() != null)
                {
                    whereDict[colName] = value;
                    primaryKeys.Add((colName, value));
                }
                else
                {
                    setDict[colName] = value;
                }
            }
            var sql = _dialect.UpdateStatement(tableName, setDict, whereDict, options, primaryKeys);
            var parameters = ToAnonymousObject(setDict.Concat(whereDict).ToDictionary(kvp => "@" + kvp.Key, kvp => kvp.Value));
            return (sql, parameters);
        }

        /// <summary>
        /// Generates an UPDATE SQL statement for the given entity object with a custom WHERE predicate.
        /// Skips properties marked as NotMapped or Computed.
        /// </summary>
        /// <param name="entity">Entity object to update.</param>
        /// <param name="wherePredicate">Custom predicate expression for WHERE clause.</param>
        /// <param name="options">Mutation options (optional, includes TableName).</param>
        /// <returns>Tuple of SQL string and parameters object.</returns>
        public (string sql, object parameters) GenerateUpdateSql(object entity, Expression wherePredicate, UpdateOptions? options = null)
        {
            options ??= new UpdateOptions();
            var entityType = entity.GetType();
            var tableAttr = entityType.GetCustomAttribute<TableAttribute>();
            var tableName = options.TableName ?? tableAttr?.Name ?? entityType.Name;
            var properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var setDict = new Dictionary<string, object>();
            var primaryKeys = new List<(string colName, object value)>();
            foreach (var prop in properties)
            {
                if (prop.GetCustomAttribute<NotMappedAttribute>() != null)
                    continue;
                var dbGenAttr = prop.GetCustomAttribute<DatabaseGeneratedAttribute>();
                if (dbGenAttr != null && dbGenAttr.DatabaseGeneratedOption == DatabaseGeneratedOption.Computed)
                    continue;
                var colAttr = prop.GetCustomAttribute<ColumnAttribute>();
                var colName = colAttr?.Name ?? prop.Name;
                var value = prop.GetValue(entity);
                if (prop.GetCustomAttribute<KeyAttribute>() != null)
                {
                    primaryKeys.Add((colName, value));
                }
                else
                {
                    setDict[colName] = value;
                }
            }
            Dictionary<string, object> whereDict = new();
            var parameters = new List<object>();
            if (wherePredicate != null)
            {
                // Only support simple binary expressions for demo
                if (wherePredicate is BinaryExpression binary && binary.Left is MemberExpression member && binary.Right is ConstantExpression constant)
                {
                    var colName = member.Member.Name;
                    whereDict[colName] = constant.Value;
                    parameters.Add(constant.Value);
                }
            }
            var sql = _dialect.UpdateStatement(tableName, setDict, whereDict, options, primaryKeys);
            var allParams = ToAnonymousObject(setDict.Concat(whereDict).ToDictionary(kvp => "@" + kvp.Key, kvp => kvp.Value));
            return (sql, allParams);
        }

        /// <summary>
        /// Generates a DELETE SQL statement for the given entity type and predicate or key values.
        /// If a predicate is provided, uses it for the WHERE clause.
        /// If key values are provided, generates a WHERE clause for key fields.
        /// </summary>
        /// <param name="entityType">Type of the entity to delete.</param>
        /// <param name="wherePredicate">Predicate expression for WHERE clause.</param>
        /// <param name="options">Mutation options (optional, includes TableName).</param>
        /// <param name="keyValues">Optional: key values for key fields (anonymous object).</param>
        /// <returns>Tuple of SQL string and parameters object.</returns>
        public (string sql, object parameters) GenerateDeleteSql(
            Type entityType,
            Expression wherePredicate,
            Options? options = null,
            Dictionary<string,object>? keyValues = null)
        {
            options ??= new Options();
            var tableAttr = entityType.GetCustomAttribute<TableAttribute>();
            var tableName = options.TableName ?? tableAttr?.Name ?? entityType.Name;
            var parameters = new List<object>();
            string whereSql = null;

            if (wherePredicate != null)
            {
                // Use the same predicate parser as SELECT
                whereSql = ParsePredicate(wherePredicate, parameters, entityType);
            }else if (keyValues != null)
            {
                var whereDict = new Dictionary<string, object>();
                var whereParts = new List<string>();
                int paramIndex = 0;
                BuildWhereFromKeyValues(keyValues, entityType, whereDict, whereParts, ref paramIndex, _dialect);
                whereSql = string.Join(" AND ", whereParts);
                parameters.AddRange(whereDict.Values);
            }

            var sql = _dialect.DeleteStatement(tableName, new { }); // pass empty object or null
            if (!string.IsNullOrEmpty(whereSql))
            {
                sql += $" WHERE {whereSql}";
            }
            var paramObj = ToAnonymousObject(parameters
                .Select((v, i) => new KeyValuePair<string, object>($"@p{i}", v))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
            return (sql, paramObj);
        }

        private void BuildWhereFromKeyValues(Dictionary<string, object> keyValues, Type entityType, Dictionary<string, object> whereDict, List<string> whereParts, ref int paramIndex, ISqlDialect dialect)
        {
            var keyProps = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetCustomAttribute<KeyAttribute>() != null)
                .ToList();
            var i = 0;
            foreach (var keyProp in keyProps)
            {
                var colAttr = keyProp.GetCustomAttribute<ColumnAttribute>();
                var colName = colAttr?.Name ?? keyProp.Name;
                if (keyValues.TryGetValue(keyProp.Name, out var value))
                {
                    string paramName = colName;
                    whereDict[paramName] = value;
                    whereParts.Add($"{dialect.FormatColumn(colName)} = @p{i++}");
                }
            }
        }

        // Shared predicate logic for building WHERE clause dictionary and SQL parts
        private static void BuildWhereFromPredicate(Expression wherePredicate, Type entityType, Dictionary<string, object> whereDict, List<string> whereParts, ref int paramIndex, ISqlDialect dialect)
        {
            // Support =, >, <, >=, <=, != for binary expressions
            if (wherePredicate is BinaryExpression binary && binary.Left is MemberExpression member)
            {
                var colName = member.Member.Name;
                object value = null;
                string sqlOp = binary.NodeType switch
                {
                    ExpressionType.Equal => "=",
                    ExpressionType.GreaterThan => ">",
                    ExpressionType.LessThan => "<",
                    ExpressionType.GreaterThanOrEqual => ">=",
                    ExpressionType.LessThanOrEqual => "<=",
                    ExpressionType.NotEqual => "!=",
                    _ => throw new NotSupportedException($"Unsupported binary operator: {binary.NodeType}")
                };

                if (binary.Right is ConstantExpression constant)
                {
                    value = constant.Value;
                }
                else
                {
                    var lambda = Expression.Lambda(binary.Right);
                    value = lambda.Compile().DynamicInvoke();
                }
                string paramName = colName;
                whereDict[paramName] = value;
                whereParts.Add($"{dialect.FormatColumn(colName)} {sqlOp} @{paramName}");
            }
        }
        /// <summary>
        /// Helper to convert a dictionary of parameter names/values to an anonymous object for parameterization.
        /// </summary>
        /// <param name="dict">Dictionary of parameter names and values.</param>
        /// <returns>Anonymous object with properties matching dictionary keys.</returns>
        public static object ToAnonymousObject(Dictionary<string, object> dict)
        {
            var obj = new ExpandoObject();
            var objDict = (IDictionary<string, object>)obj;
            foreach (var kvp in dict)
                objDict[kvp.Key] = kvp.Value;
            return obj;
        }

        public static Dictionary<string, object?> ObjectToDictionary(object obj)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            var dict = new Dictionary<string, object?>();
            var type = obj.GetType();
            foreach (var prop in type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                dict[prop.Name] = prop.GetValue(obj);
            }
            return dict;
        }

        /// <summary>
        /// Generates an INSERT SQL statement for the given entity object.
        /// Skips properties marked as NotMapped, Identity, or Computed.
        /// </summary>
        /// <param name="entity">Entity object to insert.</param>
        /// <param name="options">Mutation options (optional, includes TableName).</param>
        /// <returns>Tuple of SQL string, parameters object, and key info for SELECT-after-mutation.</returns>
        public (string sql, object parameters, List<(string colName, object? value, bool isIdentity)> keyInfo) GenerateInsertSqlWithKeyInfo(object entity, InsertOptions? options = null)
        {
            options ??= new InsertOptions();
            var entityType = entity.GetType();
            var tableAttr = entityType.GetCustomAttribute<TableAttribute>();
            var tableName = options.TableName ?? tableAttr?.Name ?? entityType.Name;
            var properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var columns = new List<string>();
            var paramNames = new List<string>();
            var paramDict = new Dictionary<string, object>();
            var keyInfo = new List<(string colName, object? value, bool isIdentity)>();

            foreach (var prop in properties)
            {
                if (prop.GetCustomAttribute<NotMappedAttribute>() != null)
                    continue;
                var colAttr = prop.GetCustomAttribute<ColumnAttribute>();
                var colName = colAttr?.Name ?? prop.Name;
                var dbGenAttr = prop.GetCustomAttribute<DatabaseGeneratedAttribute>();
                bool isIdentity = dbGenAttr != null && dbGenAttr.DatabaseGeneratedOption == DatabaseGeneratedOption.Identity;
                if (prop.GetCustomAttribute<KeyAttribute>() != null && colName != null)
                    keyInfo.Add((colName, prop.GetValue(entity), isIdentity));
                if (dbGenAttr != null && (dbGenAttr.DatabaseGeneratedOption == DatabaseGeneratedOption.Identity || dbGenAttr.DatabaseGeneratedOption == DatabaseGeneratedOption.Computed))
                    continue;
                var paramName = "@" + colName;
                columns.Add(colName);
                paramNames.Add(paramName);
                paramDict[paramName] = prop.GetValue(entity);
            }

            var sql = _dialect.InsertStatement(tableName, columns, paramNames, options);
            var parameters = ToAnonymousObject(paramDict);
            return (sql, parameters, keyInfo);
        }

        /// <summary>
        /// Retrieves the key information for the given entity object.
        /// </summary>
        /// <param name="entity">Entity object to inspect.</param>
        /// <returns>List of key information tuples.</returns>
        public List<(string colName, object? value, bool isIdentity)> GetKeyInfo(object entity)
        {
            var entityType = entity.GetType();
            var properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var keyInfo = new List<(string colName, object? value, bool isIdentity)>();
            foreach (var prop in properties)
            {
                if (prop.GetCustomAttribute<KeyAttribute>() != null)
                {
                    var colAttr = prop.GetCustomAttribute<ColumnAttribute>();
                    var colName = colAttr?.Name ?? prop.Name;
                    var dbGenAttr = prop.GetCustomAttribute<DatabaseGeneratedAttribute>();
                    bool isIdentity = dbGenAttr != null && dbGenAttr.DatabaseGeneratedOption == DatabaseGeneratedOption.Identity;
                    keyInfo.Add((colName, prop.GetValue(entity), isIdentity));
                }
            }
            return keyInfo;
        }

        private static List<Column> ParseProjectionColumns(Expression body, ISqlDialect dialect)
        {
            var columns = new List<Column>();
            if (body is MemberInitExpression memberInit)
            {
                foreach (var binding in memberInit.Bindings)
                {
                    if (binding is MemberAssignment assignment)
                    {
                        // Handle Pair<TLeft, TRight> property access
                        if (assignment.Expression is MemberExpression memberExpr)
                        {
                            // Check for x.Left.Prop or x.Right.Prop
                            if (memberExpr.Expression is MemberExpression pairExpr &&
                                (pairExpr.Member.Name == "Left" || pairExpr.Member.Name == "Right"))
                            {
                                var side = pairExpr.Member.Name; // "Left" or "Right"
                                var tableType = pairExpr.Type;
                                var tableName = tableType.Name;
                                var columnName = memberExpr.Member.Name;
                                var alias = assignment.Member.Name;
                                columns.Add(new Column(null, tableName, columnName, alias));
                            }
                            else
                            {
                                // Fallback: direct member access
                                var tableName = memberExpr.Expression?.Type.Name ?? "";
                                var columnName = memberExpr.Member.Name;
                                var alias = assignment.Member.Name;
                                columns.Add(new Column(null, tableName, columnName, alias));
                            }
                        }
                        // Optionally: handle nested MemberInit for more complex projections
                    }
                }
            }
            return columns;
        }
    }
}