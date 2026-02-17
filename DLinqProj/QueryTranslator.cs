using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace DLinq
{
    // Utility for generating unique table aliases per query
    public class AliasGenerator
    {
        private int _counter = 1;
        public string Next() => $"t{_counter++}";
    }

    /// <summary>
    /// Translates LINQ expression trees into SQL statements using a provided SQL dialect.
    /// Supports SELECT, INSERT, UPDATE, and basic ORDER/WHERE/IN operations.
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

        // Helper to get the correct alias for an entity type (by full name)
        private static string GetAliasForEntity(Type entityType, TranslateContext context, bool addIfNotFound = true)
        {
            var entityKey = entityType.FullName!;
            if (!context.TableAliasMap.TryGetValue(entityKey, out var alias) && addIfNotFound)
            {
                alias = context.AliasGen.Next();
                context.TableAliasMap[entityKey] = alias;
            }
            return alias!;
        }

        private static string GetAliasForMember(MemberExpression memberExpr, TranslateContext context)
        {
            // Walk up to the root ParameterExpression
            //Expression expr = memberExpr.Expression;
            //while (expr is MemberExpression innerMember)
            //    expr = innerMember.Expression;

            //if (expr is ParameterExpression param && context.ParameterAliasMap.TryGetValue(param, out var alias))
            //    return alias;

            // Fallback: use entity type as before, but unwrap JoinResult<TLeft,TRight> to the left entity so aliases are stable
            var entityType = memberExpr.Expression?.Type;
            if (entityType != null && entityType.IsGenericType && entityType.GetGenericTypeDefinition() == typeof(JoinResult<,>))
            {
                // use the left side of JoinResult<,> to match other resolution logic
                entityType = entityType.GetGenericArguments()[0];
            }
            return entityType != null ? GetAliasForEntity(entityType, context) : "";
        }

        // Helper to evaluate any expression (variable, property, constant, etc.)
        private static object GetValueFromExpression(Expression expr)
        {
            if (expr == null)
                return null;

            // If the expression contains any ParameterExpression nodes, it is a column/reference
            // coming from the query lambda (e.g. "pet.OwnerId"). Don't try to evaluate/compile it
            // here — let the higher-level translator treat it as a column reference.
            if (ExpressionContainsParameter(expr))
                return null;

            if (expr is ConstantExpression c)
                return c.Value;

            var lambda = Expression.Lambda(expr);
            var compiled = lambda.Compile();
            return compiled.DynamicInvoke();
        }

        // New helper: returns true if the expression can be evaluated to a constant value (including null).
        private static bool TryGetValueFromExpression(Expression expr, out object value)
        {
            value = null;
            if (expr == null)
            {
                value = null;
                return true;
            }

            // If the expression contains any ParameterExpression nodes, treat it as non-constant
            if (ExpressionContainsParameter(expr))
                return false;

            if (expr is ConstantExpression c)
            {
                value = c.Value;
                return true;
            }

            var lambda = Expression.Lambda(expr);
            var compiled = lambda.Compile();
            value = compiled.DynamicInvoke();
            return true;
        }

        private static bool ExpressionContainsParameter(Expression expr)
        {
            // Fast path for the common case of a direct ParameterExpression
            if (expr is ParameterExpression)
                return true;

            // Walk the tree and detect any ParameterExpression nodes
            var finder = new ParameterFinder();
            finder.Visit(expr);
            return finder.Found;
        }

        private class ParameterFinder : ExpressionVisitor
        {
            public bool Found { get; private set; }
            protected override Expression VisitParameter(ParameterExpression node)
            {
                Found = true;
                // we can stop early but ExpressionVisitor doesn't provide cancellation, so just mark and continue
                return base.VisitParameter(node);
            }
        }

        private static string GetEntityTableName(Type entityType)
        {
            if (entityType == null) return string.Empty;
            if (entityType.Name == nameof(ConstantExpression)) return string.Empty;
            var tableAttr = entityType.GetCustomAttribute<TableAttribute>();
            var tableName = tableAttr?.Name ?? entityType.Name;
            return tableName;
        }

        private Type GetEntityType(Type entityType)
        {
            if (entityType == null) return null;

            // Handle common wrapper types first (SqlQuery<T>, IQueryable<T>, etc.)
            if (entityType.IsGenericType)
            {
                var genDef = entityType.GetGenericTypeDefinition();
                if (genDef == typeof(SqlQuery<>))
                {
                    entityType = entityType.GetGenericArguments().FirstOrDefault();
                }
                else if (typeof(IQueryable).IsAssignableFrom(entityType))
                {
                    entityType = entityType.GetGenericArguments()[0];
                }
                // If the type itself is JoinResult<TLeft,TRight>, treat the query entity as the left entity.
                else if (genDef == typeof(JoinResult<,>))
                {
                    var leftType = entityType.GetGenericArguments()[0];
                    entityType = GetEntityType(leftType);
                }
            }

            // Also walk base types in case the JoinResult<> is not the direct generic definition (subclassing).
            var baseType = entityType;
            while (baseType != null && baseType != typeof(object))
            {
                if (baseType.IsGenericType && baseType.GetGenericTypeDefinition() == typeof(JoinResult<,>))
                {
                    var leftType = baseType.GetGenericArguments()[0];
                    return GetEntityType(leftType);
                }
                baseType = baseType.BaseType;
            }

            return entityType;
        }

        /// <summary>
        /// Parses a predicate expression (e.g., from a Where clause) into SQL syntax and collects parameters.
        /// Supports AND/OR, comparison, IN/NOT IN, and basic member access.
        /// </summary>
        /// <param name="expr">The predicate expression to parse.</param>
        /// <param name="parameters">List to collect parameter values for SQL statement.</param>
        /// <param name="entityType">Type of the entity being queried.</param>
        /// <returns>SQL WHERE clause string.</returns>
        private string ParsePredicate(Expression expr, List<object> parameters, Type entityType, TranslateContext context)
        {
            switch (expr)
            {
                case BinaryExpression binary:
                    return ParseBinaryPredicate(binary, parameters, entityType, context);
                case MethodCallExpression methodCall when methodCall.Method.Name == "Contains":
                    return ParseContainsPredicate(methodCall, parameters, entityType, context);
                case UnaryExpression unary when unary.NodeType == ExpressionType.Not:
                    return ParseNotContainsPredicate(unary, parameters, entityType, context);
                default:
                    throw new NotSupportedException("Unsupported predicate expression.");
            }
        }

        private (string colName, string colTableName, Type colEntityType) GetColumnInfo(MemberExpression member, TranslateContext context)
        {
            var colAttr = member.Member.GetCustomAttribute<ColumnAttribute>();
            var colName = colAttr?.Name ?? member.Member.Name;
            // Try to resolve the alias using the parameter expression if available
            Expression expr = member.Expression;
            while (expr is MemberExpression innerMember)
                expr = innerMember.Expression;
            if (expr is ParameterExpression param)
            {
                // Use the resolved entity type (unwrap JoinResult<,>, SqlQuery<>, IQueryable<>, etc.)
                var colEntityType = GetEntityType(param.Type);
                var colTableName = GetEntityTableName(colEntityType);
                return (colName, colTableName, colEntityType);
            }
            var colTableNameFallback = GetEntityTableName(member.Member.ReflectedType!);
            var colEntityTypeFallback = member.Member.ReflectedType!;
            return (colName, colTableNameFallback, colEntityTypeFallback);
        }

        private static (string colName, string colTableName) GetMemberColumnInfo(MemberInfo member)
        {
            var colAttr = member.GetCustomAttribute<ColumnAttribute>();
            var colName = colAttr?.Name ?? member.Name;
            var colTableName = GetEntityTableName(member.DeclaringType!);
            return (colName, colTableName);
        }

        private List<string> AddParameters(IEnumerable<object> values, List<object> parameters)
        {
            var paramNames = new List<string>();
            foreach (var v in values ?? Enumerable.Empty<object>())
            {
                parameters.Add(v);
                paramNames.Add(_dialect.ParameterPlaceholder(parameters.Count - 1));
            }
            return paramNames;
        }

        private string ParseBinaryPredicate(BinaryExpression binary, List<object> parameters, Type entityType, TranslateContext context)
        {
            if (binary.NodeType == ExpressionType.AndAlso || binary.NodeType == ExpressionType.OrElse)
            {
                var left = ParsePredicate(binary.Left, parameters, entityType, context);
                var right = ParsePredicate(binary.Right, parameters, entityType, context);
                var op = binary.NodeType == ExpressionType.AndAlso ? "AND" : "OR";
                return $"({left}) {op} ({right})";
            }

            // Parameterize right-side captured variables (e.g., p.Id > minId)
            if (binary.Left is MemberExpression entityMember && binary.Right is MemberExpression rightMemberExpr && !ExpressionContainsParameter(rightMemberExpr))
            {
                // right is a captured variable or constant, left should be the entity property
                if (TryGetValueFromExpression(rightMemberExpr, out var rightValue))
                {
                    var (colName, _, colEntityType) = GetColumnInfo(entityMember, context);

                    string tableAlias = GetAliasForEntity(colEntityType, context);

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

                    parameters.Add(rightValue);
                    return $"{_dialect.FormatColumn(colName, tableAlias)} {sqlOp} {_dialect.ParameterPlaceholder(parameters.Count - 1)}";
                }
            }
            // Handle member-to-member comparisons (e.g. person.Id == pet.OwnerId) used for joins
            if (binary.Left is MemberExpression leftMember && binary.Right is MemberExpression rightMember)
            {
                var (leftColName, leftColTableName, leftEntityType) = GetColumnInfo(leftMember, context);
                var (rightColName, rightColTableName, rightEntityType) = GetColumnInfo(rightMember, context);

                string leftAlias = GetAliasForEntity(leftEntityType, context);
                string rightAlias = GetAliasForEntity(rightEntityType, context);
                if (leftColTableName == leftAlias) leftAlias = null;
                if (rightColTableName == rightAlias) rightAlias = null;
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

                return $"{_dialect.FormatColumn(leftColName, leftAlias)} {sqlOp} {_dialect.FormatColumn(rightColName, rightAlias)}";
            }
            

            MemberExpression member = null;
            object constantValue = null;
            bool constantAvailable = false;

            if (binary.Left is MemberExpression lhsMember)
            {
                member = lhsMember;
                constantAvailable = TryGetValueFromExpression(binary.Right, out constantValue);
            }
            else if (binary.Right is MemberExpression rhsMember)
            {
                member = rhsMember;
                constantAvailable = TryGetValueFromExpression(binary.Left, out constantValue);
            }

            if (member != null && constantAvailable)
            {
                var colName = member.Member.Name;
                string tableAlias = GetAliasForMember(member, context);
                string entityTable = GetEntityTableName(member.Expression.Type);
                if (entityTable == tableAlias) tableAlias = null;
                // Handle null constants as IS NULL / IS NOT NULL
                if (constantValue == null)
                {
                    return binary.NodeType switch
                    {
                        ExpressionType.Equal => $"{_dialect.FormatColumn(colName, tableAlias)} IS NULL",
                        ExpressionType.NotEqual => $"{_dialect.FormatColumn(colName, tableAlias)} IS NOT NULL",
                        _ => throw new NotSupportedException("Unsupported null comparison.")
                    };
                }
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
                return $"{_dialect.FormatColumn(colName, tableAlias)} {sqlOp} {_dialect.ParameterPlaceholder(parameters.Count - 1)}";
            }

            throw new NotSupportedException("Unsupported binary predicate.");
        }

        private string ParseContainsPredicate(MethodCallExpression containsCall, List<object> parameters, Type entityType, TranslateContext context)
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
                var (colName, _, colEntityType) = GetColumnInfo(member, context);
                string tableAlias = GetAliasForEntity(colEntityType, context);
                var paramNames = AddParameters(values, parameters);
                return $"{_dialect.FormatColumn(colName, tableAlias)} IN ({string.Join(", ", paramNames)})";
            }
            throw new NotSupportedException("Unsupported Contains predicate.");
        }

        private string ParseNotContainsPredicate(UnaryExpression unary, List<object> parameters, Type entityType, TranslateContext context)
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
                    var (colName, _, colEntityType) = GetColumnInfo(member, context);
                    var paramNames = AddParameters(values, parameters);
                    string tableAlias = GetAliasForEntity(colEntityType, context);
                    return $"{_dialect.FormatColumn(colName, tableAlias)} NOT IN ({string.Join(", ", paramNames)})";
                }
            }
            throw new NotSupportedException("Unsupported Not Contains predicate.");
        }

        public class TranslateContext
        {
            public AliasGenerator AliasGen { get; } = new AliasGenerator();
            public Dictionary<string, string> TableAliasMap { get; } = new();
            public Dictionary<ParameterExpression, string> ParameterAliasMap { get; } = new(); // NEW
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
            var context = new TranslateContext();
            parameters = new List<object>();
            SqlSelectNode ast = null;
            int? skip = null;
            int? take = null;
            SqlFunctionSource fromFunction = null;
            var orderBy = new List<(string Column, bool Descending)>();
            string whereSql = null;
            Type entityType = null;
            List<Column> columns = null;
            var primaryKeys = new List<string>();
            Expression current = expression;
            List<SqlJoin> joins = null;
            LambdaExpression pendingSelector = null;
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
                    case "Where":
                        HandleWhere(mce, parameters, ref whereSql, ref entityType, ref current, context);
                        break;
                    case "Join":
                        if (joins == null) joins = new List<SqlJoin>();
                        HandleJoin(mce, joins, context);
                        current = mce.Arguments[0];
                        break;
                    case "Select":
                        pendingSelector = (LambdaExpression)((UnaryExpression)mce.Arguments[1]).Operand;
                        current = mce.Arguments[0];
                        break;
                    default:
                        current = (current as MethodCallExpression)?.Arguments[0]!;
                        break;
                }
            }
            if (entityType == null && current != null)
            {
                var type = GetEntityType(current.Type);
                entityType = type;
            }
            if (entityType == null)
            {
                throw new InvalidOperationException("Unable to determine entity type for SQL translation. Ensure your query targets a valid entity type.");
            }
            var tableName = GetEntityTableName(entityType);
            var tableAlias = GetAliasForEntity(entityType, context);
            if (pendingSelector != null)
            {
                var projectedColumns = ParseProjectionColumns(pendingSelector.Body, _dialect, context);
                if (projectedColumns != null && projectedColumns.Count > 0)
                {
                    columns = projectedColumns;
                }
            }
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
                    columns.Add(new Column(null, tableAlias!, colName));
                    if (prop.GetCustomAttribute<KeyAttribute>() != null)
                        primaryKeys.Add(colName);
                }
            }
            else
            {
                primaryKeys = new List<string>();
            }
            if (joins != null && joins.Count > 0)
            {
                var joinAst = new SqlJoinSelectNode
                {
                    Table = tableName,
                    Alias = tableAlias,
                    Columns = columns,
                    Where = null!,
                    WhereSql = whereSql!,
                    PrimaryKeys = primaryKeys,
                    Skip = skip,
                    Take = take,
                    FromFunction = fromFunction!,
                    OrderBy = orderBy,
                    Joins = joins
                };
                ast = joinAst;
            }
            else
            {
                ast = new SqlSelectNode
                {
                    Table = tableName,
                    Alias = tableAlias,
                    Columns = columns,
                    Where = null!,
                    WhereSql = whereSql!,
                    PrimaryKeys = primaryKeys,
                    Skip = skip,
                    Take = take,
                    FromFunction = fromFunction!,
                    OrderBy = orderBy
                };
            }
            return _dialect.SelectStatement(ast, parameters);
        }

        private void HandleJoin(MethodCallExpression mce, List<SqlJoin> joins, TranslateContext context)
        {
            // mce.Arguments: outer, inner, onPredicate, resultSelector

            // Robustly unwrap a quoted/converted lambda: support Quote(...), Convert(...), and direct lambda forms
            Expression onExpr = mce.Arguments.Count > 2 ? mce.Arguments[2] : null!;
            while (onExpr is UnaryExpression unary &&
                   (unary.NodeType == ExpressionType.Quote || unary.NodeType == ExpressionType.Convert))
            {
                onExpr = unary.Operand;
            }
            var onPredicate = onExpr as LambdaExpression;

            Type leftType = null!;
            Type rightType = null!;
            ParameterExpression leftParam = null!;
            ParameterExpression rightParam = null!;

            if (onPredicate != null && onPredicate.Parameters.Count >= 2)
            {
                leftParam = onPredicate.Parameters[0];
                rightParam = onPredicate.Parameters[1];
                leftType = GetEntityType(leftParam.Type);
                rightType = GetEntityType(rightParam.Type);
            }
            else
            {
                // 1) Try generic arguments of the method call (if available)
                if (mce.Method.IsGenericMethod)
                {
                    var genArgs = mce.Method.GetGenericArguments();
                    if (genArgs.Length >= 2)
                    {
                        leftType = GetEntityType(genArgs[0]);
                        rightType = GetEntityType(genArgs[1]);
                    }
                }

                // 2) Helper to try to obtain entity type from an expression argument
                Type TryFromExpressionArg(Expression arg)
                {
                    if (arg == null) return null!;
                    // If argument is a ConstantExpression holding a runtime SqlQuery<T> instance
                    if (arg is ConstantExpression ce && ce.Value != null)
                    {
                        var vt = ce.Value.GetType();
                        if (vt.IsGenericType)
                        {
                            var ga = vt.GetGenericArguments();
                            if (ga.Length > 0) return GetEntityType(ga[0]);
                        }
                    }
                    // Use the expression.Type generic argument if present (e.g., SqlQuery<T>, IQueryable<T>)
                    var exprType = arg.Type;
                    if (exprType.IsGenericType)
                    {
                        var ga2 = exprType.GetGenericArguments();
                        if (ga2.Length > 0) return GetEntityType(ga2[0]);
                    }
                    return null!;
                }

                if (leftType == null) leftType = TryFromExpressionArg(mce.Arguments[0]);
                if (rightType == null) rightType = TryFromExpressionArg(mce.Arguments[1]);
            }

            if (leftType == null || rightType == null)
                throw new InvalidOperationException("Unable to determine join left/right entity types for translation.");

            var leftTable = GetEntityTableName(leftType);
            var rightTable = GetEntityTableName(rightType);

            // Ensure aliases are registered so ParsePredicate and projection logic resolve the same aliases
            var leftAlias = GetAliasForEntity(leftType, context);
            var rightAlias = GetAliasForEntity(rightType, context);

            // Map parameter expressions to aliases (NEW)
            //if (leftParam != null) context.ParameterAliasMap[leftParam] = leftAlias;
            //if (rightParam != null) context.ParameterAliasMap[rightParam] = rightAlias;

            // Parse the ON clause using the context so aliases resolve correctly.
            string onSql = null;
            if (onPredicate != null)
            {
                onSql = ParsePredicate(onPredicate.Body, new List<object>(), leftType, context);
            }

            if (string.IsNullOrWhiteSpace(onSql))
                throw new NullReferenceException("Join Criteria 'ON' clause cannot be null or empty.");

            joins.Add(new SqlJoin
            {
                JoinType = "INNER",
                RightTable = rightTable,
                RightAlias = rightAlias,
                OnClause = onSql
            });
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
            var tableName = options.TableName ?? GetEntityTableName(entityType);
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
            //if (options.SelectAfterMutation && keyInfo.Count > 0)
            //{
            //    var selectColumns = properties
            //        .Where(p => p.GetCustomAttribute<NotMappedAttribute>() == null)
            //        .Select(p =>
            //        {
            //            var colAttr = p.GetCustomAttribute<ColumnAttribute>();
            //            var colName = colAttr?.Name ?? p.Name;
            //            return new Column(null, tableName, colName);
            //        })
            //        .ToList();
            //    var selectAst = new SqlSelectNode
            //    {
            //        Table = tableName,
            //        Columns = selectColumns,
            //        WhereSql = GenerateIdentityWhereClause(entityType, tableName, keyInfo),
            //        PrimaryKeys = keyInfo.Select(k => k.colName).ToList()
            //    };
            //    sql += "; " + _dialect.SelectStatement(selectAst, new List<object>());
            //}
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
            var tableName = options.TableName ?? GetEntityTableName(entityType);
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
            var context = new TranslateContext();
            var entityType = entity.GetType();
            var tableAttr = entityType.GetCustomAttribute<TableAttribute>();
            var tableName = options.TableName ?? GetEntityTableName(entityType);
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
            Dictionary<string, object>? keyValues = null)
        {
            options ??= new Options();
            var context = new TranslateContext();
            var tableAttr = entityType.GetCustomAttribute<TableAttribute>();
            var tableName = options.TableName ?? GetEntityTableName(entityType);
            var parameters = new List<object>();
            string whereSql = null;

            context.TableAliasMap[entityType.FullName!] = tableName;

            if (wherePredicate != null)
            {
                // Use the same predicate parser as SELECT
                whereSql = ParsePredicate(wherePredicate, parameters, entityType, context);
            }
            else if (keyValues != null)
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
            var tableName = options.TableName ?? GetEntityTableName(entityType);
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
        protected List<(string colName, object? value, bool isIdentity)> GetKeyInfo(object entity)
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

        private static bool IsDerivedFromGenericType(Type type, Type genericTypeDefinition)
        {
            while (type != null && type != typeof(object))
            {
                var current = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
                if (current == genericTypeDefinition)
                    return true;

                type = type.BaseType;
            }
            return false;
        }

        private static List<Column>? ParseProjectionColumns(Expression body, ISqlDialect dialect, TranslateContext context)
        {
            if (body is null) return null;
            if (body is MemberInitExpression memberInit)
                return ParseMemberInitProjection(memberInit, context);
            if (body is NewExpression newExpr)
                return ParseNewExpressionProjection(newExpr, context);
            return ParseFallbackProjection(body, context);
        }

        private static List<Column> ParseMemberInitProjection(MemberInitExpression memberInit, TranslateContext context)
        {
            var columns = new List<Column>();
            foreach (var binding in memberInit.Bindings)
            {
                if (binding is MemberAssignment assignment)
                {
                    if (assignment.Expression is MemberExpression memberExpr)
                    {
                        if (IsJoinSideMember(memberExpr))
                        {
                            columns.Add(ParseJoinMemberExpression(memberExpr, assignment.Member.Name, context));
                            continue;
                        }
                        columns.Add(ParseDirectMemberColumn(memberExpr, assignment.Member.Name, context));
                    }
                    else if (TryGetValueFromExpression(assignment.Expression, out var constValue))
                    {
                        columns.Add(ParseLiteralColumn(constValue, assignment.Member.Name));
                    }
                }
            }
            return columns;
        }

        private static List<Column> ParseNewExpressionProjection(NewExpression newExpr, TranslateContext context)
        {
            var columns = new List<Column>();
            for (int i = 0; i < newExpr.Arguments.Count; i++)
            {
                var arg = newExpr.Arguments[i];
                var alias = newExpr.Members?[i].Name ?? $"c{i}";
                if (arg is MemberExpression memberExpr)
                {
                    if (IsJoinSideMember(memberExpr))
                    {
                        columns.Add(ParseJoinMemberExpression(memberExpr, alias, context));
                        continue;
                    }
                    columns.Add(ParseDirectMemberColumn(memberExpr, alias, context));
                }
                else if (TryGetValueFromExpression(arg, out var constValue))
                {
                    columns.Add(ParseLiteralColumn(constValue, alias));
                }
            }
            return columns;
        }

        private static List<Column> ParseFallbackProjection(Expression body, TranslateContext context)
        {
            var columns = new List<Column>();
            foreach (var prop in body.Type.GetProperties())
            {
                var tableAlias = GetAliasForEntity(prop.DeclaringType!, context);
                columns.Add(new Column(null, tableAlias, prop.Name, prop.Name));
            }
            return columns;
        }

        private static bool IsJoinSideMember(MemberExpression memberExpr)
        {
            return memberExpr.Expression is MemberExpression pairExpr &&
                   (pairExpr.Member.Name == "Left" || pairExpr.Member.Name == "Right");
        }

        private static Column ParseJoinMemberExpression(MemberExpression memberExpr, string alias, TranslateContext context)
        {
            var pairExpr = (MemberExpression)memberExpr.Expression!;
            var side = pairExpr.Member.Name;
            Type entityType = pairExpr.Type;
            if (entityType != null && entityType.IsGenericType && entityType.GetGenericTypeDefinition() == typeof(JoinResult<,>))
            {
                var gargs = entityType.GetGenericArguments();
                entityType = side == "Right" ? gargs[1] : gargs[0];
            }
            var tableAlias = GetAliasForEntity(entityType, context);
            var columnName = memberExpr.Member.Name;
            return new Column(null, tableAlias, columnName, alias);
        }

        private static Column ParseDirectMemberColumn(MemberExpression memberExpr, string alias, TranslateContext context)
        {
            var colEntityType = memberExpr.Expression?.Type;
            if (colEntityType != null && colEntityType.IsGenericType && colEntityType.GetGenericTypeDefinition() == typeof(JoinResult<,>))
            {
                colEntityType = colEntityType.GetGenericArguments()[0];
            }
            var colTableAlias = colEntityType != null ? GetAliasForEntity(colEntityType, context) : "";
            var colName = memberExpr.Member.Name;
            return new Column(null, colTableAlias, colName, alias);
        }

        private static Column ParseLiteralColumn(object? value, string alias)
        {
            string literal;
            if (value == null) literal = "NULL";
            else if (value is string s) literal = $"'{s.Replace("'", "''")}'";
            else if (value is bool b) literal = b ? "1" : "0";
            else if (value is DateTime dt) literal = $"'{dt:yyyy-MM-ddTHH:mm:ss.fffffff}'";
            else if (value is Enum) literal = Convert.ToInt32(value).ToString();
            else literal = Convert.ToString(value, CultureInfo.InvariantCulture);
            return new Column(null, "", literal, alias, true);
        }
        // Helper for Skip
        private static void HandleSkip(MethodCallExpression mce, ref int? skip, ref Expression current)
        {
            skip = (int)((ConstantExpression)mce.Arguments[1]).Value!;
            current = mce.Arguments[0];
        }

        // Helper for Take
        private static void HandleTake(MethodCallExpression mce, ref int? take, ref Expression current)
        {
            take = (int)((ConstantExpression)mce.Arguments[1]).Value!;
            current = mce.Arguments[0];
        }

        // Helper for FromFunction
        private static void HandleFromFunction(MethodCallExpression mce, ref SqlFunctionSource fromFunction, ref Expression current)
        {
            var fnName = (string)((ConstantExpression)mce.Arguments[1]).Value!;
            var argsExpr = (NewArrayExpression)mce.Arguments[2];
            var args = argsExpr.Expressions.Select(e => ((ConstantExpression)e).Value).ToList();
            fromFunction = new SqlFunctionSource { FunctionName = fnName, Arguments = args! };
            current = mce.Arguments[0];
        }

        // Helper for OrderBy/ThenBy
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

        private void HandleWhere(MethodCallExpression mce, List<object> parameters, ref string whereSql, ref Type entityType, ref Expression current, TranslateContext context)
        {
            var whereLambda = (LambdaExpression)((UnaryExpression)mce.Arguments[1]).Operand;
            entityType = GetEntityType(mce.Arguments[0].Type.GetGenericArguments()[0]);
            var tableName = GetEntityTableName(entityType);
            var tableAlias = GetAliasForEntity(entityType, context);
            var thisWhereSql = ParsePredicate(whereLambda.Body, parameters, entityType, context);
            whereSql = whereSql == null ? thisWhereSql : $"({thisWhereSql}) AND ({whereSql})";
            current = mce.Arguments[0];
        }
    }
}