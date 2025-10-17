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

                // Handle left side as member, right side as constant or unary
                if (binary.Left is MemberExpression leftMember)
                {
                    member = leftMember;
                    if (binary.Right is ConstantExpression rightConst)
                    {
                        constantValue = rightConst.Value;
                    }
                    else if (binary.Right is UnaryExpression rightUnary && rightUnary.Operand is ConstantExpression rightUnaryConst)
                    {
                        constantValue = rightUnaryConst.Value;
                    }
                }
                // Handle right side as member, left side as constant or unary
                else if (binary.Right is MemberExpression rightMember)
                {
                    member = rightMember;
                    if (binary.Left is ConstantExpression leftConst)
                    {
                        constantValue = leftConst.Value;
                    }
                    else if (binary.Left is UnaryExpression leftUnary && leftUnary.Operand is ConstantExpression leftUnaryConst)
                    {
                        constantValue = leftUnaryConst.Value;
                    }
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
                var values = (valuesExpr as ConstantExpression)?.Value as IEnumerable<object>;
                if (member == null && containsCall.Arguments.Count == 2)
                {
                    member = containsCall.Arguments[1] as MemberExpression;
                    valuesExpr = containsCall.Arguments[0];
                    values = (valuesExpr as ConstantExpression)?.Value as IEnumerable<object>;
                }
                if (member != null && valuesExpr is ConstantExpression constExpr)
                {
                    var prop = entityType.GetProperty(member.Member.Name);
                    var colAttr = prop?.GetCustomAttribute<ColumnAttribute>();
                    var colName = colAttr?.Name ?? member.Member.Name;
                    var paramNames = new List<string>();
                    foreach (var v in (constExpr.Value as IEnumerable<object>) ?? Enumerable.Empty<object>())
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
                    var values = (valuesExpr as ConstantExpression)?.Value as IEnumerable<object>;
                    if (member == null && notContainsCall.Arguments.Count == 2)
                    {
                        member = notContainsCall.Arguments[1] as MemberExpression;
                        valuesExpr = notContainsCall.Arguments[0];
                        values = (valuesExpr as ConstantExpression)?.Value as IEnumerable<object>;
                    }
                    if (member != null && valuesExpr is ConstantExpression constExpr)
                    {
                        var prop = entityType.GetProperty(member.Member.Name);
                        var colAttr = prop?.GetCustomAttribute<ColumnAttribute>();
                        var colName = colAttr?.Name ?? member.Member.Name;
                        var paramNames = new List<string>();
                        foreach (var v in (constExpr.Value as IEnumerable<object>) ?? Enumerable.Empty<object>())
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
                    default:
                        current = (current as MethodCallExpression)?.Arguments[0];
                        break;
                }
            }

            // Try to infer entityType if not set by Where
            if (entityType == null && current != null)
            {
                var type = current.Type;
                if (type.IsGenericType)
                {
                    // Try SqlQuery<T>
                    if (type.GetGenericTypeDefinition() == typeof(SqlQuery<>))
                    {
                        entityType = type.GetGenericArguments()[0];
                    }
                    // Try IQueryable<T>
                    else if (typeof(IQueryable).IsAssignableFrom(type))
                    {
                        entityType = type.GetGenericArguments()[0];
                    }
                }
            }

            // If still not found, try from Expression.Type
            if (entityType == null && expression.Type.IsGenericType)
            {
                entityType = expression.Type.GetGenericArguments().FirstOrDefault();
            }

            // Final check: throw if entityType is still null
            if (entityType == null)
            {
                throw new InvalidOperationException("Unable to determine entity type for SQL translation. Ensure your query targets a valid entity type.");
            }

            var properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var columns = new List<string>();
            var primaryKeys = new List<string>();
            foreach (var prop in properties)
            {
                if (prop.GetCustomAttribute<NotMappedAttribute>() != null)
                    continue;
                var colAttr = prop.GetCustomAttribute<ColumnAttribute>();
                var colName = colAttr?.Name ?? prop.Name;
                columns.Add(colName);
                if (prop.GetCustomAttribute<KeyAttribute>() != null)
                    primaryKeys.Add(colName);
            }
            var tableAttr = entityType.GetCustomAttribute<TableAttribute>();
            var tableName = tableAttr?.Name ?? entityType.Name;
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
            var outer = mce.Arguments[0];
            var inner = mce.Arguments[1];
            var outerKeyLambda = (LambdaExpression)((UnaryExpression)mce.Arguments[2]).Operand;
            var innerKeyLambda = (LambdaExpression)((UnaryExpression)mce.Arguments[3]).Operand;
            var outerKey = (outerKeyLambda.Body as MemberExpression)?.Member.Name;
            var innerKey = (innerKeyLambda.Body as MemberExpression)?.Member.Name;
            var innerType = inner.GetType().GetGenericArguments()[0];
            var innerTableAttr = innerType.GetCustomAttribute<TableAttribute>();
            var innerTable = innerTableAttr?.Name ?? innerType.Name;
            joins.Add(new SqlJoinNode
            {
                Table = innerTable,
                LeftColumn = outerKey,
                RightColumn = innerKey,
                JoinType = "INNER"
            });
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
        public (string sql, object parameters) GenerateInsertSql(object entity, Options? options = null)
        {
            options ??= new Options();
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
                    .Select(p => p.GetCustomAttribute<ColumnAttribute>()?.Name ?? p.Name)
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
        public (string sql, object parameters) GenerateUpdateSql(object entity, Options? options = null)
        {
            options ??= new Options();
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
        public (string sql, object parameters) GenerateUpdateSql(object entity, Expression wherePredicate, Options? options = null)
        {
            options ??= new Options();
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
            object? keyValues = null)
        {
            options ??= new Options();
            var tableAttr = entityType.GetCustomAttribute<TableAttribute>();
            var tableName = options.TableName ?? tableAttr?.Name ?? entityType.Name;
            Dictionary<string, object> whereDict = new();
            List<string> whereParts = new();
            int paramIndex = 0;

            // If keyValues is provided, use it to build the WHERE clause
            if (keyValues != null)
            {
                if (keyValues is IDictionary<string, object> dict)
                {
                    foreach (var kvp in dict)
                    {
                        whereDict[kvp.Key] = kvp.Value;
                        whereParts.Add($"{_dialect.FormatColumn(kvp.Key)} = @{kvp.Key}");
                    }
                }
                else
                {
                    var keyValueType = keyValues.GetType();
                    foreach (var prop in keyValueType.GetProperties())
                    {
                        var value = prop.GetValue(keyValues);
                        whereDict[prop.Name] = value;
                        whereParts.Add($"{_dialect.FormatColumn(prop.Name)} = @{prop.Name}");
                    }
                }
            }
            // If a predicate is provided, use it
            else if (wherePredicate != null)
            {
                BuildWhereFromPredicate(wherePredicate, entityType, whereDict, whereParts, ref paramIndex, _dialect);
            }

            string whereClause = whereParts.Count > 0 ? string.Join(" AND ", whereParts) : null;
            var sql = _dialect.DeleteStatement(tableName, whereDict);
            if (!string.IsNullOrEmpty(whereClause) && !sql.Contains("WHERE", StringComparison.OrdinalIgnoreCase))
            {
                sql += $" WHERE {whereClause}";
            }
            var parameters = ToAnonymousObject(whereDict.ToDictionary(kvp => "@" + kvp.Key, kvp => kvp.Value));
            return (sql, parameters);
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

        /// <summary>
        /// Generates an INSERT SQL statement for the given entity object.
        /// Skips properties marked as NotMapped, Identity, or Computed.
        /// </summary>
        /// <param name="entity">Entity object to insert.</param>
        /// <param name="options">Mutation options (optional, includes TableName).</param>
        /// <returns>Tuple of SQL string, parameters object, and key info for SELECT-after-mutation.</returns>
        public (string sql, object parameters, List<(string colName, object? value, bool isIdentity)> keyInfo) GenerateInsertSqlWithKeyInfo(object entity, Options? options = null)
        {
            options ??= new Options();
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
                columns.Add(_dialect.FormatColumn(colName));
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
    }
}