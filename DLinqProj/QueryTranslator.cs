using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Drawing;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

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

        public QueryTranslator(ISqlDialect dialect, Func<Type, string,string> entity2TableMapper):this(dialect)
        {
            Entity2TableMapper = entity2TableMapper;
        }

        /// <summary>
        /// Exposes the ISqlDialect instance used by this QueryTranslator.
        /// </summary>
        public ISqlDialect Dialect => _dialect;

        /// <summary>
        /// Map entity names to table names. assign custom mapping function.
        /// P1 = Entity Type
        /// P2 = TableName (from attribute mapping)
        /// Return = your mapped table name. if null is returned TableName will be used as the mapping
        /// </summary>
        public Func<Type, string, string>? Entity2TableMapper { get; set; }

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
            var entityType = memberExpr.Expression?.Type;

            return entityType != null ? GetAliasForEntity(entityType, context) : "";
        }

        private static MemberExpression? GetMemberInfoFromLambda(LambdaExpression expression)
        {
            Expression body = expression.Body;
            // Unwrap unary expressions (e.g., Convert)
            while (body is UnaryExpression unary &&
                   (unary.NodeType == ExpressionType.Convert || unary.NodeType == ExpressionType.ConvertChecked || unary.NodeType == ExpressionType.Quote))
            {
                body = unary.Operand;
            }
            if (body is MemberExpression mbrExpr && mbrExpr.Member != null)
                return mbrExpr;

            throw new NotSupportedException("Only simple member expression is supported.");
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

        private static object? GetValueFromExpression(Expression expr, HashSet<string> entityParamNames)
        {
            if (expr == null)
                return null;

            // If the expression contains any ParameterExpression nodes that are entity parameters, treat as column/reference
            if (ExpressionContainsParameter(expr, entityParamNames))
                return null;

            if (expr is ConstantExpression c)
                return c.Value;

            // Handle MethodCallExpression for conversion operators (e.g., op_Implicit) 
            // These cannot be invoked via DynamicInvoke, so we extract the underlying value
            if (expr is MethodCallExpression methodCall)
            {
                // Check if this is a conversion operator (op_Implicit, op_Explicit, etc.)
                if (methodCall.Method.IsSpecialName && 
                    (methodCall.Method.Name.StartsWith("op_") || methodCall.Method.Name == "ToArray"))
                {
                    // Try to extract from the first argument
                    if (methodCall.Arguments.Count > 0)
                    {
                        var argValue = GetValueFromExpression(methodCall.Arguments[0], entityParamNames);
                        // If the method is a conversion, we might need to apply it manually
                        // For op_Implicit/op_Explicit on arrays/collections, often just the underlying value works
                        if (argValue != null)
                            return argValue;
                    }
                }
            }

            var lambda = Expression.Lambda(expr);
            var compiled = lambda.Compile();
            return compiled.DynamicInvoke();
        }

        private static bool ExpressionContainsParameter(Expression expr, HashSet<string> entityParamNames)
        {
            if (expr is ParameterExpression param)
                return entityParamNames.Contains(param.Name);

            var finder = new ParameterFinderWithNames(entityParamNames);
            finder.Visit(expr);
            return finder.Found;
        }

        private class ParameterFinderWithNames : ExpressionVisitor
        {
            private readonly HashSet<string> _entityParamNames;
            public bool Found { get; private set; }

            public ParameterFinderWithNames(HashSet<string> entityParamNames)
            {
                _entityParamNames = entityParamNames;
            }

            protected override Expression VisitParameter(ParameterExpression node)
            {
                if (_entityParamNames.Contains(node.Name))
                    Found = true;
                return base.VisitParameter(node);
            }
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

        internal string GetEntityTableName(Type entityType)
        {
            if (entityType == null) return string.Empty;
            if (entityType.Name == nameof(ConstantExpression)) return string.Empty;
            var tableAttr = entityType.GetCustomAttribute<TableAttribute>();
            var tableName = tableAttr?.Name ?? entityType.Name;
            if (tableAttr?.Schema != null)
                tableName = $"{tableAttr.Schema}.{tableName}";  
            if (Entity2TableMapper != null) tableName = Entity2TableMapper(entityType, tableName) ?? tableName;
            return tableName;
        }

        private static Type GetEntityType(Type entityType)
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
            }

            return entityType;
        }
        private class ParameterReplacer : ExpressionVisitor
        {
            private readonly Dictionary<ParameterExpression, Expression> _map;

            public ParameterReplacer(Dictionary<ParameterExpression, Expression> map)
            {
                _map = map ?? new Dictionary<ParameterExpression, Expression>();
            }

            protected override Expression VisitParameter(ParameterExpression node)
            {
                if (_map.TryGetValue(node, out var replacement))
                    return Visit(replacement);
                return base.VisitParameter(node);
            }
        }

        /// <summary>
        /// Parses a predicate expression (e.g., from a Where clause) into SQL syntax and collects parameters.
        /// Supports AND/OR, comparison, IN/NOT IN, and basic member access.
        /// </summary>
        /// <param name="expr">The predicate expression to parse.</param>
        /// <param name="parameters">List to collect parameter values for SQL statement.</param>
        /// <param name="entityType">Type of the entity being queried.</param>
        /// <returns>SQL WHERE clause string.</returns>
        private string ParsePredicate(Expression expr, List<object> parameters, TranslateContext context, HashSet<string> entityParamNames)
        {
            // Try to get lambda parameters for this predicate (if any)
            if (expr is LambdaExpression lambda)
            {
                foreach (var p in lambda.Parameters)
                    entityParamNames.Add(p.Name);
                expr = lambda.Body;
            }
            return ParsePredicateWithEntityParams(expr, parameters, context, entityParamNames);
        }

        // Recursively parse predicate, parameterizing all MemberExpressions not from entityParamNames
        private string ParsePredicateWithEntityParams(Expression expr, List<object> parameters, TranslateContext context, HashSet<string> entityParamNames)
        {
            switch (expr)
            {
                case BinaryExpression binary:
                    // Special handling for null constants: use IS NULL/IS NOT NULL
                    bool leftIsNull = IsNullConstant(binary.Left);
                    bool rightIsNull = IsNullConstant(binary.Right);
                    if (leftIsNull || rightIsNull)
                    {
                        // Find the non-null side (should be a column)
                        Expression colExpr = leftIsNull ? binary.Right : binary.Left;
                        string colSql = ParsePredicateWithEntityParams(colExpr, parameters, context, entityParamNames);
                        if (binary.NodeType == ExpressionType.Equal)
                            return $"{colSql} IS NULL";
                        if (binary.NodeType == ExpressionType.NotEqual)
                            return $"{colSql} IS NOT NULL";
                        throw new NotSupportedException("Null comparison only supported for == and !=");
                    }

                    // Check if either side is a Convert from enum to int (for char enum handling)
                    Type enumType = null;
                    if (binary.Left is UnaryExpression leftUnary && leftUnary.NodeType == ExpressionType.Convert && 
                        leftUnary.Operand.Type.IsEnum)
                    {
                        enumType = leftUnary.Operand.Type;
                    }
                    else if (binary.Right is UnaryExpression rightUnary && rightUnary.NodeType == ExpressionType.Convert && 
                        rightUnary.Operand.Type.IsEnum)
                    {
                        enumType = rightUnary.Operand.Type;
                    }

                    string op = context.dialect.MapExpressionTypeToSqlOperator(binary.NodeType);

                    if (op == "AND" || op == "OR")
                    {
                        // Flatten same-operator chains to avoid redundant nested parentheses.
                        // e.g. C#'s left-associative  (a && b) && c  becomes  (a AND b AND c)
                        // instead of  ((a AND b) AND c).
                        var terms = CollectLogicalTerms(binary, binary.NodeType, parameters, context, entityParamNames);
                        return $"({string.Join($" {op} ", terms)})";
                    }

                    var left = ParsePredicateWithEntityParams(binary.Left, parameters, context, entityParamNames);
                    var right = ParsePredicateWithEntityParams(binary.Right, parameters, context, entityParamNames);

                    // Convert the last parameter if it was a constant from a char enum comparison
                    // Check if the enum has the [CharEnum] attribute indicating it should be treated as char
                    if (enumType != null && parameters.Count > 0 && enumType.IsDefined(typeof(CharEnumAttribute), false))
                    {
                        var lastParam = parameters[parameters.Count - 1];
                        if (lastParam != null && (lastParam is int || lastParam.GetType().IsEnum))
                        {
                            // Convert int or enum to char for enums marked with [CharEnum]
                            int intValue = lastParam is int i ? i : Convert.ToInt32(lastParam);
                            parameters[parameters.Count - 1] = (char)intValue;
                        }
                    }

                    return $"{left} {op} {right}";
                case MemberExpression memberExpr:
                    if (IsEntityMember(memberExpr, entityParamNames))
                    {
                        // Treat as column reference - use the dialect's FormatColumn method
                        var colEntityType = memberExpr.Expression?.Type;
                        var colTableAlias = colEntityType != null ? GetAliasForEntity(colEntityType, context) : "";
                        var col = GetColumnInfo(memberExpr, context);
                        if (colTableAlias == col.colTableName && context.TableAliasMap.Count <= 1) colTableAlias = null;
                        //var colName = memberExpr.Member.Name;
                        return context.dialect.FormatColumn(col.colName, colTableAlias);
                    }
                    else
                    {
                        // Try to evaluate as a captured variable/constant
                        // But first check if this still contains parameters (safety check)
                        if (ExpressionContainsParameter(memberExpr))
                        {
                            // This shouldn't happen if IsEntityMember works correctly, but guard against it
                            throw new InvalidOperationException($"Member expression '{memberExpr}' contains query parameters and cannot be evaluated as a constant.");
                        }
                        var value = EvaluateMemberExpression(memberExpr);
                        if (value == null)
                        {
                            // Null constant: handled in BinaryExpression above, but if used directly, emit NULL
                            return "NULL";
                        }
                        parameters.Add(value);
                        return context.dialect.ParameterPlaceholder(parameters.Count - 1);
                    }
                case ConstantExpression constExpr:
                    if (constExpr.Value == null)
                    {
                        // Null constant: handled in BinaryExpression above, but if used directly, emit NULL
                        return "NULL";
                    }
                    parameters.Add(constExpr.Value);
                    return context.dialect.ParameterPlaceholder(parameters.Count - 1);
                case UnaryExpression unary when unary.NodeType == ExpressionType.Convert || unary.NodeType == ExpressionType.ConvertChecked:
                    return ParsePredicateWithEntityParams(unary.Operand, parameters, context, entityParamNames);
                case MethodCallExpression methodCall when methodCall.Method.Name == "Contains":
                    // Check if it's a string.Contains or collection.Contains
                    if (methodCall.Method.DeclaringType == typeof(string))
                        return ParseStringMethodPredicate(methodCall, "Contains", parameters, context, entityParamNames);
                    return ParseContainsPredicate(methodCall, parameters, context, entityParamNames);
                case MethodCallExpression methodCall when methodCall.Method.Name == "StartsWith":
                    return ParseStringMethodPredicate(methodCall, "StartsWith", parameters, context, entityParamNames);
                case MethodCallExpression methodCall when methodCall.Method.Name == "EndsWith":
                    return ParseStringMethodPredicate(methodCall, "EndsWith", parameters, context, entityParamNames);
                case UnaryExpression unary when unary.NodeType == ExpressionType.Not && unary.Operand is MethodCallExpression mce && mce.Method.Name == "Contains":
                    // Check if it's a string.Contains or collection.Contains
                    if (mce.Method.DeclaringType == typeof(string))
                        return $"NOT ({ParseStringMethodPredicate(mce, "Contains", parameters, context, entityParamNames)})";
                    return ParseNotContainsPredicate(mce, parameters, context, entityParamNames);
                case UnaryExpression unary when unary.NodeType == ExpressionType.Not && unary.Operand is MethodCallExpression mce2 && mce2.Method.Name == "StartsWith":
                    return $"NOT ({ParseStringMethodPredicate(mce2, "StartsWith", parameters, context, entityParamNames)})";
                case UnaryExpression unary when unary.NodeType == ExpressionType.Not && unary.Operand is MethodCallExpression mce3 && mce3.Method.Name == "EndsWith":
                    return $"NOT ({ParseStringMethodPredicate(mce3, "EndsWith", parameters, context, entityParamNames)})";
                case InvocationExpression invocation:
                    // Fallback to old logic for invocation
                    throw new NotSupportedException("Invocation expressions in predicates are not supported in this mode.");
                default:
                    // Try to evaluate and parameterize
                    object val = GetValueFromExpression(expr);
                    if (val == null)
                        return "NULL";
                    parameters.Add(val);
                    return context.dialect.ParameterPlaceholder(parameters.Count - 1);
            }
        }

        /// <summary>
        /// Recursively collects all leaf SQL fragments for a left-associative logical chain
        /// (e.g. <c>(a &amp;&amp; b) &amp;&amp; c</c>) that shares the same root operator, so the caller
        /// can join them with a single pair of outer parentheses instead of nesting them.
        /// When the operator differs (e.g. mixing AND and OR), the sub-expression is treated
        /// as an opaque fragment and is NOT flattened further.
        /// </summary>
        private List<string> CollectLogicalTerms(
            BinaryExpression binary,
            ExpressionType rootNodeType,
            List<object> parameters,
            TranslateContext context,
            HashSet<string> entityParamNames)
        {
            var terms = new List<string>();
            CollectLogicalTermsInto(binary, rootNodeType, parameters, context, entityParamNames, terms);
            return terms;
        }

        private void CollectLogicalTermsInto(
            Expression expr,
            ExpressionType rootNodeType,
            List<object> parameters,
            TranslateContext context,
            HashSet<string> entityParamNames,
            List<string> terms)
        {
            if (expr is BinaryExpression bin && bin.NodeType == rootNodeType)
            {
                // Same operator — recurse into both sides to keep flattening
                CollectLogicalTermsInto(bin.Left, rootNodeType, parameters, context, entityParamNames, terms);
                CollectLogicalTermsInto(bin.Right, rootNodeType, parameters, context, entityParamNames, terms);
            }
            else
            {
                // Different operator or leaf — translate as a normal sub-predicate
                terms.Add(ParsePredicateWithEntityParams(expr, parameters, context, entityParamNames));
            }
        }

        private static bool IsNullConstant(Expression expr)
        {
            if (expr is ConstantExpression ce && ce.Value == null)
                return true;
            if (expr is MemberExpression me)
            {
                // Try to evaluate the member expression
                if (!ExpressionContainsParameter(me))
                {
                    var value = EvaluateMemberExpression(me);
                    return value == null;
                }
            }
            return false;
        }

        internal (string colName, string colTableName, Type colEntityType) GetColumnInfo(MemberExpression member, TranslateContext context)
        {
            var memberInfo = member.Member;
            var colAttr = memberInfo.GetCustomAttribute<ColumnAttribute>();
            var colName = colAttr?.Name ?? memberInfo.Name;
            var colTableNameFallback = GetEntityTableName(member.Expression!.Type);
            var colEntityTypeFallback = member.Expression!.Type;
            return (colName, colTableNameFallback, colEntityTypeFallback);
        }

        /// <summary>
        /// Gets Column info based on Member declaration. 
        /// Do Not use with Member Expressions. The Declared/Reflected type is not the Entity type (e.g. captured closure variables)
        /// </summary>
        /// <param name="member"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        internal (string colName, string colTableName, Type colEntityType) GetColumnInfo(MemberInfo memberInfo, TranslateContext context)
        {
            var colAttr = memberInfo.GetCustomAttribute<ColumnAttribute>();
            var colName = colAttr?.Name ?? memberInfo.Name;
            var colTableNameFallback = GetEntityTableName(memberInfo.ReflectedType!);
            var colEntityTypeFallback = memberInfo.ReflectedType!;
            return (colName, colTableNameFallback, colEntityTypeFallback);
        }
        private static List<string> AddParameters(IEnumerable<object> values, List<object> parameters, TranslateContext context)
        {
            var paramNames = new List<string>();
            foreach (var v in values ?? Enumerable.Empty<object>())
            {
                parameters.Add(v);
                paramNames.Add(context.dialect.ParameterPlaceholder(parameters.Count - 1));
            }
            return paramNames;
        }

        private string ParseBinaryPredicate(BinaryExpression binary, List<object> parameters, TranslateContext context, HashSet<string> entityParamNames)
        {
            if (binary.NodeType == ExpressionType.AndAlso || binary.NodeType == ExpressionType.OrElse)
            {
                var left = ParsePredicate(binary.Left, parameters, context, entityParamNames);
                var right = ParsePredicate(binary.Right, parameters, context, entityParamNames);
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

                    string sqlOp = context.dialect.MapExpressionTypeToSqlOperator(binary.NodeType);

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
                string sqlOp = context.dialect.MapExpressionTypeToSqlOperator(binary.NodeType);

                return $"{_dialect.FormatColumn(leftColName, leftAlias)} {sqlOp} {_dialect.FormatColumn(rightColName, rightAlias)}";
            }



            MemberExpression member = null;
            object constantValue = null;
            bool constantAvailable = false;

            // Unwrap Convert nodes to handle enum comparisons (e.g., Convert(x.EnumProp, Int32) == 83)
            Expression leftExpr = binary.Left;
            Expression rightExpr = binary.Right;
            Type enumType = null;

            if (leftExpr is UnaryExpression leftUnary && leftUnary.NodeType == ExpressionType.Convert && 
                leftUnary.Operand is MemberExpression && leftUnary.Operand.Type.IsEnum)
            {
                enumType = leftUnary.Operand.Type;
                leftExpr = leftUnary.Operand;
            }

            if (rightExpr is UnaryExpression rightUnary && rightUnary.NodeType == ExpressionType.Convert && 
                rightUnary.Operand is MemberExpression && rightUnary.Operand.Type.IsEnum)
            {
                enumType = rightUnary.Operand.Type;
                rightExpr = rightUnary.Operand;
            }

            if (leftExpr is MemberExpression lhsMember)
            {
                member = lhsMember;
                constantAvailable = TryGetValueFromExpression(rightExpr, out constantValue);
            }
            else if (rightExpr is MemberExpression rhsMember)
            {
                member = rhsMember;
                constantAvailable = TryGetValueFromExpression(leftExpr, out constantValue);
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

                // Convert constant value to char if this is a char enum (marked with [CharEnum] attribute)
                if (enumType != null && constantValue != null && constantValue.GetType().IsEnum && 
                    enumType.IsDefined(typeof(CharEnumAttribute), false))
                {
                    constantValue = (char)Convert.ToInt32(constantValue);
                }
                else if (enumType != null && constantValue != null && constantValue is int intValue && 
                    enumType.IsDefined(typeof(CharEnumAttribute), false))
                {
                    constantValue = (char)intValue;
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

        private static (MemberExpression member, List<string> paramNames) ParseContainsPredicateCore(MethodCallExpression containsCall, List<object> parameters, TranslateContext context, HashSet<string> entityParamNames)
        {
            // Handles: ids.Contains(x.Id) or list.Contains(x.Id)
            Expression valuesExpr = null;
            MemberExpression member = null;

            // Static: ids.Contains(x.Id)
            if (containsCall.Object != null)
            {
                valuesExpr = containsCall.Object;
                var arg = containsCall.Arguments[0];
                // Unwrap Convert/ConvertChecked expressions (e.g., (Guid)x.Id)
                if (arg is UnaryExpression unary && (unary.NodeType == ExpressionType.Convert || unary.NodeType == ExpressionType.ConvertChecked))
                    arg = unary.Operand;
                if (arg is MemberExpression m)
                    member = m;
            }
            else if (containsCall.Arguments.Count == 2)
            {
                valuesExpr = containsCall.Arguments[0];
                var arg = containsCall.Arguments[1];
                // Unwrap Convert/ConvertChecked expressions (e.g., (Guid)x.Id)
                if (arg is UnaryExpression unary && (unary.NodeType == ExpressionType.Convert || unary.NodeType == ExpressionType.ConvertChecked))
                    arg = unary.Operand;
                if (arg is MemberExpression m)
                    member = m;
            }

            if (member != null && IsEntityMember(member, entityParamNames))
            {
                // Use new overload that distinguishes entity params from closure variables
                var values = GetValueFromExpression(valuesExpr, entityParamNames) as IEnumerable;
                if (values != null)
                {
                    var valuesList = values.Cast<object>().ToList();
                    // Check if valuesExpr is constant (does not reference entity parameters)
                    bool isConstant = !ExpressionContainsParameter(valuesExpr, entityParamNames);
                    List<string> paramNames;
                    if (isConstant)
                    {
                        // Emit literals directly
                        paramNames = valuesList.Select(v =>context.dialect.FormatValue(v)).ToList();
                    }
                    else
                    {
                        // Parameterize
                        paramNames = AddParameters(valuesList, parameters, context);
                    }
                    return (member, paramNames);
                }
            }
            throw new NotSupportedException("Unsupported Contains predicate.");
        }

        private string ParseContainsPredicate(MethodCallExpression containsCall, List<object> parameters, TranslateContext context, HashSet<string> entityParamNames)
        {
            var (member, paramNames) = ParseContainsPredicateCore(containsCall, parameters, context, entityParamNames);
            var (colName, _, colEntityType) = GetColumnInfo(member, context);
            string tableAlias = GetAliasForEntity(colEntityType, context);
            return $"{context.dialect.FormatColumn(colName, tableAlias)} IN ({string.Join(", ", paramNames)})";

        }

        private string ParseNotContainsPredicate(MethodCallExpression containsCall, List<object> parameters, TranslateContext context, HashSet<string> entityParamNames)
        {
            var (member, paramNames) = ParseContainsPredicateCore(containsCall, parameters, context, entityParamNames);
            var (colName, _, colEntityType) = GetColumnInfo(member, context);
            string tableAlias = GetAliasForEntity(colEntityType, context);
            return $"{context.dialect.FormatColumn(colName, tableAlias)} NOT IN ({string.Join(", ", paramNames)})";

        }

        private string ParseStringMethodPredicate(MethodCallExpression methodCall, string methodName, List<object> parameters, TranslateContext context, HashSet<string> entityParamNames)
        {
            // Handles: x.Name.StartsWith("value"), x.Name.EndsWith("value"), x.Name.Contains("value")
            if (methodCall.Object is MemberExpression member && IsEntityMember(member, entityParamNames) && methodCall.Arguments.Count == 1)
            {
                var (colName, _, colEntityType) = GetColumnInfo(member, context);
                string tableAlias = GetAliasForEntity(colEntityType, context);
                string columnSql = context.dialect.FormatColumn(colName, tableAlias);

                // Get the pattern value
                var patternExpr = methodCall.Arguments[0];
                object patternValue = GetValueFromExpression(patternExpr, entityParamNames);

                if (patternValue is string pattern)
                {
                    // Build the LIKE pattern based on the method
                    string likePattern = methodName switch
                    {
                        "StartsWith" => pattern + "%",
                        "EndsWith" => "%" + pattern,
                        "Contains" => "%" + pattern + "%",
                        _ => throw new NotSupportedException($"Unsupported string method: {methodName}")
                    };

                    parameters.Add(likePattern);
                    return $"{columnSql} LIKE {context.dialect.ParameterPlaceholder(parameters.Count - 1)}";
                }

                throw new NotSupportedException($"String {methodName} requires a string argument.");
            }

            throw new NotSupportedException($"Unsupported {methodName} predicate.");
        }

        public class TranslateContext
        {
            public ISqlDialect dialect { get; set; }
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
        public string Translate(SqlSelectNode queryTree, out List<object> parameters)
        {
            var context = new TranslateContext { dialect = _dialect };
            parameters = new List<object>();
            var primaryKeys = new List<string>();

            // Pre-populate parameters with TVF arguments so WHERE clause parameters get correct offsets
            if (queryTree.FromFunction != null)
            {
                foreach (var arg in queryTree.FromFunction.Arguments)
                    parameters.Add(arg);
            }

            ParseJoins(queryTree.Joins, parameters, context);
            queryTree.WhereSqlExpr = ParseWhere(queryTree.WhereExpr, parameters, context, queryTree.ChainedWherePredicates);
            foreach (var obe in queryTree.OrderByExpr) ParseOrderBy(obe.Expression, obe.Descending, queryTree.OrderBy, context);

            var projectedColumns = ParseProjectionColumns(queryTree.SelectExpr, context, parameters);
            if (projectedColumns != null && projectedColumns.Count > 0)
            {
                queryTree.Columns = projectedColumns;
            }


            queryTree.FromTable = GetEntityTableName(queryTree.FromEntity);
            queryTree.TableAlias = GetAliasForEntity(queryTree.FromEntity, context);

            if (queryTree.Columns == null)
            {
                var properties = queryTree.FromEntity.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                queryTree.Columns = new List<Column>();
                foreach (var prop in properties)
                {
                    if (prop.GetCustomAttribute<NotMappedAttribute>() != null)
                        continue;
                    var colAttr = prop.GetCustomAttribute<ColumnAttribute>();
                    var colName = colAttr?.Name ?? prop.Name;
                    queryTree.Columns.Add(new Column(null, queryTree.TableAlias!, colName));
                    if (prop.GetCustomAttribute<KeyAttribute>() != null)
                        queryTree.PrimaryKeys.Add(colName);
                }
            }
            else
            {
                queryTree.PrimaryKeys = new List<string>();
            }

            return _dialect.SelectStatement(queryTree, parameters);
        }

        private void ParseJoins(List<SqlJoin> joins, List<object> parameters, TranslateContext context)
        {
            foreach (var join in joins) ParseJoin(join, parameters, context);
        }
        private void ParseJoin(SqlJoin join, List<object> parameters, TranslateContext context)
        {
            Type runtimeType = join.GetType();
            Type[] args = runtimeType.GetGenericArguments();
            Type SqlJoinType = typeof(SqlJoin<,>).MakeGenericType(args);
            // Use reflection to access the onPredicate property
            var exprProp = SqlJoinType.GetProperty("onPredicate");
            Expression onExpr = exprProp?.GetValue(join) as Expression;
            // Robustly unwrap a quoted/converted lambda: support Quote(...), Convert(...), and direct lambda forms
            while (onExpr is UnaryExpression unary &&
                   (unary.NodeType == ExpressionType.Quote || unary.NodeType == ExpressionType.Convert))
            {
                onExpr = unary.Operand;
            }
            var onPredicate = onExpr as LambdaExpression;

            // Fallback: try to resolve lambda if not directly available
            if (onPredicate == null)
            {
                onPredicate = ResolveLambdaFromExpression(onExpr);
            }

            Type leftType = null;
            Type rightType = null;
            ParameterExpression leftParam = null;
            ParameterExpression rightParam = null;
            HashSet<string> entityParamNames = new(5);

            if (onPredicate != null && onPredicate.Parameters.Count >= 2)
            {
                leftParam = onPredicate.Parameters[0];
                rightParam = onPredicate.Parameters[1];
                leftType = GetEntityType(leftParam.Type);
                rightType = GetEntityType(rightParam.Type);
                // Add all parameter names from the join lambda
                foreach (var p in onPredicate.Parameters)
                    entityParamNames.Add(p.Name!);
            }

            if (leftType == null || rightType == null)
                throw new InvalidOperationException("Unable to determine join left/right entity types for translation.");

            join.LeftTable = GetEntityTableName(leftType);
            join.RightTable = GetEntityTableName(rightType);

            // Ensure aliases are registered so ParsePredicate and projection logic resolve the same aliases
            var leftAlias = GetAliasForEntity(leftType, context);
            join.RightAlias = GetAliasForEntity(rightType, context);

            // Parse the ON clause using the context so aliases resolve correctly.
            string onSql = null;
            if (onPredicate != null)
            {
                join.OnClause = ParsePredicate(onPredicate.Body, parameters, context, entityParamNames);
            }

            if (string.IsNullOrWhiteSpace(join.OnClause))
                throw new NullReferenceException("Join Criteria 'ON' clause cannot be null or empty.");
        }

        /// <summary>
        /// Generates an INSERT SQL statement for the given entity object.
        /// Skips properties marked as NotMapped, Identity, or Computed.
        /// </summary>
        /// <param name="entity">Entity object to insert.</param>
        /// <param name="options">Mutation options (optional, includes TableName).</param>
        /// <returns>Tuple of SQL string and parameters object.</returns>
        public (string sql, object parameters) GenerateInsertSql<TableType>(object entity, InsertOptions? options = null)
        {
            options ??= new InsertOptions();
            var entityType = entity.GetType();
            var tableType = typeof(TableType);
            var tableAttr = tableType.GetCustomAttribute<TableAttribute>();
            var tableName = options.TableName ?? GetEntityTableName(tableType);
            var properties = tableType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var entityProps = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance).ToDictionary(_ => _.Name);
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
                if (!(entityProps.ContainsKey(prop.Name) && entityProps[prop.Name].GetValue(entity) != null))
                    continue; // Skip properties that are not present on the actual entity instance
                if (prop.GetCustomAttribute<KeyAttribute>() != null && colName != null)
                    keyInfo.Add((colName, entityProps[prop.Name].GetValue(entity), isIdentity));
                if (dbGenAttr != null && (dbGenAttr.DatabaseGeneratedOption == DatabaseGeneratedOption.Identity || dbGenAttr.DatabaseGeneratedOption == DatabaseGeneratedOption.Computed))
                    continue;
                var paramName = _dialect.FormatParameter(colName);
                columns.Add(colName);
                paramNames.Add(paramName);
                paramDict[paramName] = entityProps[prop.Name].GetValue(entity);
            }
            var sql = _dialect.InsertStatement(tableName, columns, paramNames, options);

            var parameters = ToAnonymousObject(paramDict);
            return (sql, parameters);
        }

        private string GenerateIdentityWhereClause(Type entityType, string tableName, List<(string colName, object? value, bool isIdentity)> keyInfo)
        {
            var whereParts = keyInfo.Select(key =>
                key.isIdentity
                    ? $"{_dialect.FormatColumn(key.colName)} = {_dialect.IdentityValueExpression(tableName, key.colName)}"
                    : $"{_dialect.FormatColumn(key.colName)} = {_dialect.FormatParameter(key.colName)}"
            );
            return string.Join(" AND ", whereParts);
        }

        /// <summary>
        /// Generates an UPDATE SQL statement for the given entity object.
        /// Skips properties marked as NotMapped or Computed.
        /// Uses primary key(s) for WHERE clause with positional parameters (@p0, @p1, etc.).
        /// </summary>
        /// <param name="entity">Entity object to update.</param>
        /// <param name="options">Mutation options (optional, includes TableName).</param>
        /// <returns>Tuple of SQL string and parameters object.</returns>
        public (string sql, object parameters) GenerateUpdateSql<TableType>(object entity, UpdateOptions? options = null)
        {
            options ??= new UpdateOptions();
            var entityType = entity.GetType();
            var tableType = typeof(TableType);
            var tableName = options.TableName ?? GetEntityTableName(tableType);
            var properties = tableType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var entityProps = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance).ToDictionary(_ => _.Name);
            var setDict = new Dictionary<string, object>();
            var primaryKeys = new List<(string colName, object value)>();
            var whereParams = new List<object>();

            foreach (var prop in properties)
            {
                if (prop.GetCustomAttribute<NotMappedAttribute>() != null)
                    continue;
                var dbGenAttr = prop.GetCustomAttribute<DatabaseGeneratedAttribute>();
                if (dbGenAttr != null && dbGenAttr.DatabaseGeneratedOption == DatabaseGeneratedOption.Computed)
                    continue;
                if (!entityProps.ContainsKey(prop.Name))
                    continue; // Skip properties that are not present on the actual entity instance
                var colAttr = prop.GetCustomAttribute<ColumnAttribute>();
                var colName = colAttr?.Name ?? prop.Name;
                var value = entityProps[prop.Name].GetValue(entity);
                if (prop.GetCustomAttribute<KeyAttribute>() != null)
                {
                    primaryKeys.Add((colName, value));
                    whereParams.Add(value);
                }
                else
                {
                    setDict[colName] = value;
                }
            }

            // Build SET clause with column -> parameter mapping
            var setClause = setDict.ToDictionary(kvp => kvp.Key, kvp => _dialect.FormatParameter(kvp.Key));

            // Build WHERE clause using positional parameters for consistency with predicate-based updates
            string? whereClause = null;
            if (primaryKeys.Count > 0)
            {
                var whereClauses = new List<string>();
                for (int i = 0; i < primaryKeys.Count; i++)
                {
                    whereClauses.Add($"{_dialect.FormatColumn(primaryKeys[i].colName)} = {_dialect.ParameterPlaceholder(i)}");
                }
                whereClause = string.Join(" AND ", whereClauses);
            }

            // Use dialect method to build complete SQL (handles OUTPUT/RETURNING placement)
            var sql = _dialect.UpdateStatement(tableName, setClause, whereClause, options);

            // Combine SET parameters and WHERE parameters
            var allParamsDict = setDict.ToDictionary(kvp => _dialect.FormatParameter(kvp.Key), kvp => kvp.Value);
            for (int i = 0; i < whereParams.Count; i++)
            {
                allParamsDict[_dialect.ParameterPlaceholder(i)] = whereParams[i];
            }

            var allParams = ToAnonymousObject(allParamsDict);
            return (sql, allParams);
        }

        /// <summary>
        /// Generates an UPDATE SQL statement for the given entity object with a custom WHERE predicate.
        /// Skips properties marked as NotMapped or Computed.
        /// Supports comprehensive predicate expressions including AND/OR, IN/NOT IN, string methods, and more.
        /// </summary>
        /// <param name="entity">Entity object to update.</param>
        /// <param name="wherePredicate">Custom predicate expression for WHERE clause.</param>
        /// <param name="options">Mutation options (optional, includes TableName).</param>
        /// <returns>Tuple of SQL string and parameters object.</returns>
        public (string sql, object parameters) GenerateUpdateSql<TableType>(object entity, Expression wherePredicate, UpdateOptions? options = null)
        {
            options ??= new UpdateOptions();
            var context = new TranslateContext { dialect = _dialect };
            var entityType = entity.GetType();
            var tableType = typeof(TableType);
            var tableName = options.TableName ?? GetEntityTableName(tableType);
            var properties = tableType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var entityProps = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance).ToDictionary(_ => _.Name);
            var setDict = new Dictionary<string, object>();
            var primaryKeys = new List<(string colName, object value)>();

            foreach (var prop in properties)
            {
                if (prop.GetCustomAttribute<NotMappedAttribute>() != null)
                    continue;
                var dbGenAttr = prop.GetCustomAttribute<DatabaseGeneratedAttribute>();
                if (dbGenAttr != null && dbGenAttr.DatabaseGeneratedOption == DatabaseGeneratedOption.Computed)
                    continue;
                if (!entityProps.ContainsKey(prop.Name))
                    continue; // Skip properties that are not present on the actual entity instance
                var colAttr = prop.GetCustomAttribute<ColumnAttribute>();
                var colName = colAttr?.Name ?? prop.Name;
                var value = entityProps[prop.Name].GetValue(entity);
                if (prop.GetCustomAttribute<KeyAttribute>() != null)
                {
                    primaryKeys.Add((colName, value));
                }
                else
                {
                    setDict[colName] = value;
                }
            }

            // Use ParsePredicate for comprehensive predicate support
            var parameters = new List<object>();
            var entityParamNames = new HashSet<string>();
            string? whereSql = null;

            if (wherePredicate != null)
            {
                // Register the entity type alias for column resolution
                context.TableAliasMap[tableType.FullName!] = tableName;

                // Parse the predicate using the robust ParsePredicate method
                whereSql = ParsePredicate(wherePredicate, parameters, context, entityParamNames);
            }

            // Build SET clause with column -> parameter mapping
            var setClause = setDict.ToDictionary(kvp => kvp.Key, kvp => _dialect.FormatParameter(kvp.Key));

            // Use dialect method to build complete SQL (handles OUTPUT/RETURNING placement)
            var sql = _dialect.UpdateStatement(tableName, setClause, whereSql, options);

            // Combine SET parameters and WHERE parameters
            var allParamsDict = setDict.ToDictionary(kvp => _dialect.FormatParameter(kvp.Key), kvp => kvp.Value);
            for (int i = 0; i < parameters.Count; i++)
            {
                allParamsDict[_dialect.ParameterPlaceholder(i)] = parameters[i];
            }

            var allParams = ToAnonymousObject(allParamsDict);
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
            LambdaExpression wherePredicate,
            Options? options = null,
            Dictionary<string, object>? keyValues = null)
        {
            options ??= new Options();
            var context = new TranslateContext { dialect = _dialect };
            var tableAttr = entityType.GetCustomAttribute<TableAttribute>();
            var tableName = options.TableName ?? GetEntityTableName(entityType);
            var parameters = new List<object>();
            var whereSql = "";

            context.TableAliasMap[entityType.FullName!] = tableName;
            HashSet<string> entityParamNames = new(5);

            if (wherePredicate != null)
            {
                foreach (var p in wherePredicate.Parameters)
                    entityParamNames.Add(p.Name!);
                whereSql = _dialect.WhereClauseFromFragments([ParsePredicate(wherePredicate, parameters, context, entityParamNames)]);
            }
            else if (keyValues != null)
            {
                var whereParts = new List<string>();
                BuildWhereFromKeyValues(keyValues, entityType, parameters, whereParts, context);
                if (whereParts.Count > 0) whereSql = _dialect.WhereClauseFromFragments(whereParts, "AND");
            }


            var sql = _dialect.DeleteStatement(tableName, new { }); // pass empty object or null
            if (!string.IsNullOrWhiteSpace(whereSql)) sql += whereSql;
            var paramObj = ToAnonymousObject(parameters
                .Select((v, i) => new KeyValuePair<string, object>(_dialect.ParameterPlaceholder(i), v))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
            return (sql, paramObj);
        }

        private void BuildWhereFromKeyValues(Dictionary<string, object> keyValues, Type entityType, List<object> parameters, List<string> whereParts, TranslateContext context)
        {
            var keyProps = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetCustomAttribute<KeyAttribute>() != null)
                .ToList();
            foreach (var keyProp in keyProps)
            {
                var col = GetColumnInfo(keyProp, context);// colAttr?.Name ?? keyProp.Name;
                var tableAlias = GetAliasForEntity(col.colEntityType, context);
                if (tableAlias == col.colTableName) tableAlias = null;
                if (keyValues.TryGetValue(keyProp.Name, out var value))
                {
                    parameters.Add(value);
                    whereParts.Add($"{_dialect.FormatColumn(col.colName, tableAlias)} = {_dialect.ParameterPlaceholder(parameters.Count - 1)}");
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
                var paramName = _dialect.FormatParameter(colName);
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


        private static List<Column>? ParseProjectionColumns(Expression body, TranslateContext context, List<object>? parameters)
        {
            if (body is null) return null;
            if (parameters == null) parameters = new List<object>();
            HashSet<string> entityParamNames = new();
            if (body is LambdaExpression lambda)
            {
                foreach (var p in lambda.Parameters)
                    entityParamNames.Add(p.Name);
                body = lambda.Body;
            }

            if (body is MemberInitExpression memberInit)
                return ParseMemberInitProjection(memberInit, context, parameters, entityParamNames);
            if (body is NewExpression newExpr)
                return ParseNewExpressionProjection(newExpr, context, parameters, entityParamNames);

            return ParseFallbackProjection(body, context);
        }

        private static bool IsEntityMember(MemberExpression memberExpr, HashSet<string> entityParamNames)
        {
            // Walk up the expression tree to find the root parameter
            Expression current = memberExpr;
            while (current is MemberExpression mem)
            {
                current = mem.Expression;
            }

            // Check if root is a parameter in our entity set
            return current is ParameterExpression param && entityParamNames.Contains(param.Name);
        }

        // Recursively translate BinaryExpression for projection, parameterizing constants and handling columns
        private static string ParseBinaryExpressionProjection(BinaryExpression binary, TranslateContext context, List<object>? parameters, HashSet<string> entityParamNames)
        {
            string leftSql;
            string rightSql;
            // Handle left side
            if (binary.Left is MemberExpression leftMember)
            {
                if (IsEntityMember(leftMember, entityParamNames))
                {
                    var colEntityType = leftMember.Expression?.Type;
                    var colTableAlias = colEntityType != null ? GetAliasForEntity(colEntityType, context) : "";
                    var colName = leftMember.Member.Name;
                    leftSql = context.dialect.FormatColumn(colName, colTableAlias);
                }
                else
                {
                    var value = EvaluateMemberExpression(leftMember);
                    if (parameters != null)
                    {
                        parameters.Add(value);
                        leftSql = context.dialect.ParameterPlaceholder(parameters.Count - 1);
                    }
                    else
                    {
                        leftSql = Convert.ToString(value, CultureInfo.InvariantCulture);
                    }
                }
            }
            else if (binary.Left is ConstantExpression leftConst)
            {
                if (parameters != null)
                {
                    parameters.Add(leftConst.Value);
                    leftSql = context.dialect.ParameterPlaceholder(parameters.Count - 1);
                }
                else
                {
                    leftSql = Convert.ToString(leftConst.Value, CultureInfo.InvariantCulture);
                }
            }
            else if (binary.Left is BinaryExpression leftBinary)
            {
                leftSql = ParseBinaryExpressionProjection(leftBinary, context, parameters, entityParamNames);
            }
            else if (TryGetValueFromExpression(binary.Left, out var leftVal))
            {
                if (parameters != null)
                {
                    parameters.Add(leftVal);
                    leftSql = context.dialect.ParameterPlaceholder(parameters.Count - 1);
                }
                else
                {
                    leftSql = Convert.ToString(leftVal, CultureInfo.InvariantCulture);
                }
            }
            else
            {
                leftSql = "NULL";
            }

            // Handle right side
            if (binary.Right is MemberExpression rightMember)
            {
                if (IsEntityMember(rightMember, entityParamNames))
                {
                    var colEntityType = rightMember.Expression?.Type;
                    var colTableAlias = colEntityType != null ? GetAliasForEntity(colEntityType, context) : "";
                    var colName = rightMember.Member.Name;
                    rightSql = context.dialect.FormatColumn(colName, colTableAlias);
                }
                else
                {
                    var value = EvaluateMemberExpression(rightMember);
                    if (parameters != null)
                    {
                        parameters.Add(value);
                        rightSql = context.dialect.ParameterPlaceholder(parameters.Count - 1);
                    }
                    else
                    {
                        rightSql = Convert.ToString(value, CultureInfo.InvariantCulture);
                    }
                }
            }
            else if (binary.Right is ConstantExpression rightConst)
            {
                if (parameters != null)
                {
                    parameters.Add(rightConst.Value);
                    rightSql = context.dialect.ParameterPlaceholder(parameters.Count - 1);
                }
                else
                {
                    rightSql = Convert.ToString(rightConst.Value, CultureInfo.InvariantCulture);
                }
            }
            else if (binary.Right is BinaryExpression rightBinary)
            {
                rightSql = ParseBinaryExpressionProjection(rightBinary, context, parameters, entityParamNames);
            }
            else if (TryGetValueFromExpression(binary.Right, out var rightVal))
            {
                if (parameters != null)
                {
                    parameters.Add(rightVal);
                    rightSql = context.dialect.ParameterPlaceholder(parameters.Count - 1);
                }
                else
                {
                    rightSql = Convert.ToString(rightVal, CultureInfo.InvariantCulture);
                }
            }
            else
            {
                rightSql = "NULL";
            }

            // Map C# binary operators to SQL
            string op = context.dialect.MapExpressionTypeToSqlOperator(binary.NodeType);

            if (op == "COALESCE")
                return $"COALESCE({leftSql}, {rightSql})";
            else
                return $"({leftSql} {op} {rightSql})";
        }

        private static List<Column> ParseMemberInitProjection(MemberInitExpression memberInit, TranslateContext context, List<object>? parameters, HashSet<string> entityParamNames)
        {
            var columns = new List<Column>();
            foreach (var binding in memberInit.Bindings)
            {
                if (binding is MemberAssignment assignment)
                {
                    if (assignment.Expression is MemberExpression memberExpr)
                    {
                        if (IsEntityMember(memberExpr, entityParamNames))
                        {
                            columns.Add(ParseDirectMemberColumn(memberExpr, assignment.Member.Name, context));
                        }
                        else if (IsJoinSideMember(memberExpr))
                        {
                            columns.Add(ParseJoinMemberExpression(memberExpr, assignment.Member.Name, context));
                            continue;
                        }
                        else
                        {
                            var value = EvaluateMemberExpression(memberExpr);
                            if (parameters != null)
                            {
                                parameters.Add(value);
                                columns.Add(new Column(null, "", context.dialect.ParameterPlaceholder(parameters.Count - 1), assignment.Member.Name, true));
                            }
                            else
                            {
                                columns.Add(ParseLiteralColumn(value, assignment.Member.Name));
                            }
                        }
                    }
                    else if (assignment.Expression is BinaryExpression binaryExpr)
                    {
                        var sqlExpr = ParseBinaryExpressionProjection(binaryExpr, context, parameters, entityParamNames);
                        columns.Add(new Column(null, "", sqlExpr, assignment.Member.Name, true));
                    }
                    else if (TryGetValueFromExpression(assignment.Expression, out var constValue))
                    {
                        if (parameters != null)
                        {
                            parameters.Add(constValue);
                            columns.Add(new Column(null, "", context.dialect.ParameterPlaceholder(parameters.Count - 1), assignment.Member.Name, true));
                        }
                        else
                        {
                            columns.Add(ParseLiteralColumn(constValue, assignment.Member.Name));
                        }
                    }
                }
            }
            return columns;
        }

        private static List<Column> ParseNewExpressionProjection(NewExpression newExpr, TranslateContext context, List<object>? parameters, HashSet<string> entityParamNames)
        {
            var columns = new List<Column>();
            for (int i = 0; i < newExpr.Arguments.Count; i++)
            {
                var arg = newExpr.Arguments[i];
                var alias = newExpr.Members?[i].Name ?? $"c{i}";
                if (arg is MemberExpression memberExpr)
                {
                    if (IsEntityMember(memberExpr, entityParamNames))
                    {
                        columns.Add(ParseDirectMemberColumn(memberExpr, alias, context));
                    }
                    else if (IsJoinSideMember(memberExpr))
                    {
                        columns.Add(ParseJoinMemberExpression(memberExpr, alias, context));
                        continue;
                    }
                    else
                    {
                        var value = EvaluateMemberExpression(memberExpr);
                        if (parameters != null)
                        {
                            parameters.Add(value);
                            columns.Add(new Column(null, "", context.dialect.ParameterPlaceholder(parameters.Count - 1), alias, true));
                        }
                        else
                        {
                            columns.Add(ParseLiteralColumn(value, alias));
                        }
                    }
                }
                else if (arg is BinaryExpression binaryExpr)
                {
                    var sqlExpr = ParseBinaryExpressionProjection(binaryExpr, context, parameters, entityParamNames);
                    columns.Add(new Column(null, "", sqlExpr, alias, true));
                }
                else if (TryGetValueFromExpression(arg, out var constValue))
                {
                    if (parameters != null)
                    {
                        parameters.Add(constValue);
                        columns.Add(new Column(null, "", context.dialect.ParameterPlaceholder(parameters.Count - 1), alias, true));
                    }
                    else
                    {
                        columns.Add(ParseLiteralColumn(constValue, alias));
                    }
                }
            }
            return columns;
        }

        private static List<Column> ParseFallbackProjection(Expression body, TranslateContext context)
        {
            var columns = new List<Column>();
            var type = body.Type;
            // Avoid projecting delegate or expression types (which have Target/Method, not entity columns)
            if (typeof(Delegate).IsAssignableFrom(type) || typeof(System.Linq.Expressions.Expression).IsAssignableFrom(type))
            {
                throw new NotSupportedException($"Projection of type '{type}' is not supported. The projection expression is likely incorrect.");
            }
            foreach (var prop in type.GetProperties())
            {
                var tableAlias = GetAliasForEntity(prop.DeclaringType!, context);
                columns.Add(new Column(null, tableAlias, prop.Name, prop.Name));
            }
            return columns;
        }

        // Helper to detect closure/captured variable member expressions
        private static bool IsClosureMember(MemberExpression memberExpr)
        {
            var declaringType = memberExpr.Member.DeclaringType;
            return memberExpr.Expression is ConstantExpression &&
                (declaringType != null &&
                    (declaringType.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false)
                        || declaringType.Name.Contains("<>")));
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
            var tableAlias = GetAliasForEntity(entityType, context);
            var columnName = memberExpr.Member.Name;
            return new Column(null, tableAlias, columnName, alias);
        }

        private static Column ParseDirectMemberColumn(MemberExpression memberExpr, string alias, TranslateContext context)
        {
            var colEntityType = memberExpr.Expression?.Type;
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

        // Helper for OrderBy/ThenBy
        private void ParseOrderBy(LambdaExpression lambda, bool Descending, List<(Column Column, bool Descending)> orderBy, TranslateContext context)
        {
            var member = GetMemberInfoFromLambda(lambda);
            if (member == null)
                throw new NotSupportedException("Only simple member OrderBy/ThenBy supported.");
            var colInfo = GetColumnInfo(member, context);
            var colAlias = colInfo.colName == member.Member.Name ? null : member.Member.Name;
            var tableAlias = GetAliasForEntity(colInfo.colEntityType, context);
            if (tableAlias == colInfo.colTableName) tableAlias = null; // avoid redundant table alias if it matches Table Name
            orderBy.Add((new Column(null, tableAlias, colInfo.colName, colAlias), Descending));
        }

        private string? ParseWhere(Expression? expr, List<object> parameters, TranslateContext context,
            IReadOnlyList<(LambdaExpression Expr, string LogicalOperator)>? additionalPredicates = null)
        {
            string? baseFragment = null;
            if (expr != null)
            {
                var whereLambda = (LambdaExpression)expr;
                var entityParamNames = new HashSet<string>(5);
                foreach (var p in whereLambda.Parameters)
                    entityParamNames.Add(p.Name!);
                baseFragment = ParsePredicate(whereLambda.Body, parameters, context, entityParamNames);
            }

            var fragments = new List<string>();
            if (baseFragment != null)
                fragments.Add(baseFragment);

            if (additionalPredicates != null)
            {
                foreach (var (additionalExpr, logicalOperator) in additionalPredicates)
                {
                    var entityParamNames = new HashSet<string>(5);
                    foreach (var p in additionalExpr.Parameters)
                        entityParamNames.Add(p.Name!);
                    var fragment = ParsePredicate(additionalExpr.Body, parameters, context, entityParamNames);
                    // Only prefix with the logical operator when there is already a preceding fragment
                    fragments.Add(fragments.Count > 0 ? $"{logicalOperator} {fragment}" : fragment);
                }
            }

            if (fragments.Count == 0) return null;

            return "\r\nWHERE " + fragments[0] + (fragments.Count > 1 ? " " + string.Join(" ", fragments.Skip(1)) : "");
        }

        private static object? EvaluateMemberExpression(MemberExpression memberExpr)
        {
            // If this member expression references a query parameter (e.g., x.Id),
            // it should not be evaluated locally - it's a column reference
            if (ExpressionContainsParameter(memberExpr))
            {
                return null; // Or throw if you prefer: throw new InvalidOperationException("Cannot evaluate member expression that references query parameter");
            }

            // Evaluate the expression's target (the object instance)
            object? target = null;
            if (memberExpr.Expression != null)
            {
                if (memberExpr.Expression is ConstantExpression constExpr)
                {
                    target = constExpr.Value;
                }
                else
                {
                    // Recursively evaluate the target
                    target = GetValueFromExpression(memberExpr.Expression);
                }
            }

            // Get the value of the member (field or property)
            if (memberExpr.Member is FieldInfo field)
                return field.GetValue(target);
            if (memberExpr.Member is PropertyInfo prop)
                return prop.GetValue(target);

            throw new NotSupportedException("Unsupported member type in EvaluateMemberExpression.");
        }

        // Try to resolve a LambdaExpression that may be wrapped/quoted inside other expression shapes.
        // This performs a best-effort search (does not compile arbitrary expressions) and tries:
        //  - direct LambdaExpression
        //  - Unary/Quote -> operand
        //  - ConstantExpression whose Value is a LambdaExpression or an Expression carrying one
        //  - MemberExpression evaluated via EvaluateMemberExpression
        //  - MethodCallExpression: try evaluating or search object/arguments
        private LambdaExpression? ResolveLambdaFromExpression(Expression? expr)
        {
            if (expr == null) return null;

            switch (expr)
            {
                case LambdaExpression le:
                    return le;
                case UnaryExpression ue:
                    // e.g. quoted lambda: Expression.Quote(...)
                    return ResolveLambdaFromExpression(ue.Operand);
                case ConstantExpression ce:
                    if (ce.Value is LambdaExpression cle) return cle;
                    if (ce.Value is Expression ex && ex is LambdaExpression cle2) return cle2;
                    return null;
                case MemberExpression mem:
                    // Try to evaluate the member (closure field/property) and see if it holds a Lambda/Expression/Delegate
                    try
                    {
                        var val = EvaluateMemberExpression(mem);
                        if (val is LambdaExpression ml) return ml;
                        if (val is Expression mex && mex is LambdaExpression ml2) return ml2;
                        if (val is Delegate d && d.Target != null)
                        {
                            // Look for an Expression field or property on the delegate target as before;
                            var target = d.Target;
                            var exprField = target.GetType()
                                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                                .FirstOrDefault(f => typeof(Expression).IsAssignableFrom(f.FieldType));
                            if (exprField != null)
                            {
                                var fval = exprField.GetValue(target) as Expression;
                                if (fval is LambdaExpression flev) return flev;
                            }
                            var exprProp = target.GetType()
                                .GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                                .FirstOrDefault(p => typeof(Expression).IsAssignableFrom(p.PropertyType));
                            if (exprProp != null)
                            {
                                var pval = exprProp.GetValue(target) as Expression;
                                if (pval is LambdaExpression plev) return plev;
                            }
                        }
                    }
                    catch
                    {
                        // ignore evaluation errors; fall through to try inner expression
                    }
                    // fall back to trying the member's target expression (e.g. nested member chains)
                    return ResolveLambdaFromExpression(mem.Expression);
                case MethodCallExpression mcall:
                    // Try to evaluate call if it's safe (GetValueFromExpression will return null if the expression contains ParameterExpression)
                    try
                    {
                        var val = GetValueFromExpression(mcall);
                        if (val is LambdaExpression ml) return ml;
                        if (val is Expression mex && mex is LambdaExpression ml2) return ml2;
                    }
                    catch { /* ignore */ }

                    // Otherwise search object and arguments for an embedded LambdaExpression
                    var candidate = ResolveLambdaFromExpression(mcall.Object);
                    if (candidate != null) return candidate;
                    foreach (var a in mcall.Arguments)
                    {
                        candidate = ResolveLambdaFromExpression(a);
                        if (candidate != null) return candidate;
                    }
                    return null;

                default:
                    return null;
            }
        }
    }
}