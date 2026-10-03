using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace DLinq
{
    public abstract class SqlQuery 
    {
        public SqlSelectNode selectNode = new SqlSelectNode();
        public Type ElementType { get; protected set; }
        public IQueryProvider Provider { get; protected set; }

        public abstract SqlQuery Take(int count);

        public static LambdaExpression BuildPredicate(FilterCriteria[] filters, string boolOperator)
        {
            if (filters == null || filters.Length == 0)
                throw new ArgumentException("At least one filter is required.");

            if (!(string.Equals(boolOperator, "AND", StringComparison.OrdinalIgnoreCase) || string.Equals(boolOperator, "OR", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Only 'AND' and 'OR' are supported.");
            boolOperator = boolOperator.ToUpper();
            // Get distinct entity types in order of first appearance
            var entityTypes = filters.Select(f => f.EntityType).Distinct().ToArray();
            var parameters = entityTypes.Select((t, i) => Expression.Parameter(t, $"e{i + 1}")).ToArray();

            // Build each filter expression
            var expressions = filters.Select(filter =>
            {
                // Find the parameter for the filter's entity type
                int paramIndex = Array.FindIndex(entityTypes, t => t == filter.EntityType);
                if (paramIndex == -1)
                    throw new ArgumentException($"EntityType {filter.EntityType.Name} not found in generic parameters.");

                var param = parameters[paramIndex];
                var property = Expression.PropertyOrField(param, filter.PropertyName);

                // Convert right operand to the property type
                var right = Expression.Constant(Convert.ChangeType(filter.RightOperand, property.Type), property.Type);

                // Build the comparison
                return filter.Operator switch
                {
                    ExpressionType.Equal => Expression.Equal(property, right),
                    ExpressionType.NotEqual => Expression.NotEqual(property, right),
                    ExpressionType.GreaterThan => Expression.GreaterThan(property, right),
                    ExpressionType.GreaterThanOrEqual => Expression.GreaterThanOrEqual(property, right),
                    ExpressionType.LessThan => Expression.LessThan(property, right),
                    ExpressionType.LessThanOrEqual => Expression.LessThanOrEqual(property, right),
                    _ => throw new NotSupportedException($"Unsupported ExpressionType: {filter.Operator}")
                };
            }).ToArray();

            // Combine all expressions with the specified boolean operator
            Expression combined = expressions[0];
            for (int i = 1; i < expressions.Length; i++)
            {
                combined = boolOperator == "AND"
                    ? Expression.AndAlso(combined, expressions[i])
                    : Expression.OrElse(combined, expressions[i]);
            }

            // Build the lambda: (T1 e1, ..., T3 e3) => combined
            var funcType = Expression.GetFuncType(parameters.Select(p => p.Type).Concat(new[] { typeof(bool) }).ToArray());
            return Expression.Lambda(funcType, combined, parameters);
        }
    }

    public class SqlQuery<T> : SqlQuery 
    {
        public SqlQuery(QueryProvider provider)
        {
            ElementType = typeof(T);
            Provider = provider;
            selectNode.FromEntity = ElementType;
        }

        // Method to generate Insert SQL for the specified entity
        public (string sql, object parameters) ToInsertSql(object entity, InsertOptions? options = null)
        {
            if (Provider is QueryProvider qp)
            {
                return qp.Translator.GenerateInsertSql<T>(entity, options);
            }
            throw new NotSupportedException("ToInsertSql is only supported for SqlQuery using QueryProvider.");
        }

        // Method to generate Update SQL for the specified entity
        public (string sql, object parameters) ToUpdateSql(object entity, UpdateOptions? options = null)
        {
            if (Provider is QueryProvider qp)
            {
                return qp.Translator.GenerateUpdateSql<T>(entity, options);
            }
            throw new NotSupportedException("ToUpdateSql is only supported for SqlQuery using QueryProvider.");
        }

        // Method to generate Update SQL for the specified entity with a where predicate
        public (string sql, object parameters) ToUpdateSql(object entity, Expression<Func<T, bool>> wherePredicate, UpdateOptions? options = null)
        {
            if (Provider is QueryProvider qp)
            {
                return qp.Translator.GenerateUpdateSql<T>(entity, wherePredicate, options);
            }
            throw new NotSupportedException("ToUpdateSql is only supported for SqlQuery using QueryProvider.");
        }

        // Existing overload for backward compatibility
        public (string sql, object parameters) ToUpdateSql(object entity)
        {
            if (Provider is QueryProvider qp)
            {
                return qp.Translator.GenerateUpdateSql<T>(entity);
            }
            throw new NotSupportedException("ToUpdateSql is only supported for SqlQuery using QueryProvider.");
        }

        // Method to generate Delete SQL for the specified entity type with a where predicate
        public (string sql, object parameters) ToDeleteSql(Expression<Func<T, bool>> wherePredicate, TableOptions? options = null)
        {
            if (Provider is QueryProvider qp)
            {
                return qp.Translator.GenerateDeleteSql(typeof(T), wherePredicate, options);
            }
            throw new NotSupportedException("ToDeleteSql is only supported for SqlQuery using QueryProvider.");
        }

        // Overload to generate Delete SQL for an entity instance by its key fields
        public (string sql, object parameters) ToDeleteSql(T entity, TableOptions? options = null)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));
            if (Provider is QueryProvider qp)
            {
                if (qp.Translator == null)
                    throw new InvalidOperationException("QueryTranslator is not available.");
                var entityType = typeof(T);
                var keyProps = entityType.GetProperties()
                    .Where(p => p.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.KeyAttribute), true).Any())
                    .ToArray();
                if (keyProps.Length == 0)
                    throw new InvalidOperationException($"Type {entityType.Name} does not have any [Key] properties.");
                var keyValues = new Dictionary<string, object>();
                foreach (var prop in keyProps)
                {
                    var colAttr = prop.GetCustomAttribute(typeof(System.ComponentModel.DataAnnotations.Schema.ColumnAttribute)) as System.ComponentModel.DataAnnotations.Schema.ColumnAttribute;
                    var colName = colAttr?.Name ?? prop.Name;
                    keyValues[colName] = prop.GetValue(entity);
                }
                // Use GenerateDeleteSql with keyValues
                return qp.Translator.GenerateDeleteSql(entityType, null, options, keyValues);
            }
            throw new NotSupportedException("ToDeleteSql is only supported for SqlQuery using QueryProvider.");
        }

        public SqlQuery<T> FromFunction(string functionName, params object[] args)
        {
            this.selectNode.FromFunction = new SqlFunctionSource() { FunctionName = functionName, Arguments = args.ToList() };
            return this;
        }
        
        public SqlQuery<T> Where(LambdaExpression predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            this.selectNode.WhereExpr = predicate;
            return this;
        }

        public SqlQuery<T> Where(Expression<Func<T, bool>> predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            this.selectNode.WhereExpr = predicate;
            return this;
        }
        public SqlQuery<T> Where<T1>(Expression<Func<T,T1, bool>> predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            this.selectNode.WhereExpr = predicate;
            return this;
        }
        public SqlQuery<T> Where<T1,T2>(Expression<Func<T, T1,T2, bool>> predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            this.selectNode.WhereExpr = predicate;
            return this;
        }
        public SqlQuery<T> Where<T1,T2,T3>(Expression<Func<T, T1, T2, T3, bool>> predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            this.selectNode.WhereExpr = predicate;
            return this;
        }
        public SqlQuery<T> Where<T1,T2,T3,T4>(Expression<Func<T, T1, T2, T3, T4, bool>> predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            this.selectNode.WhereExpr = predicate;
            return this;
        }

        /// <summary>
        /// Dynamically builds and sets the base WHERE predicate from a <see cref="JsonObject"/>, where each
        /// property specifies a column filter. Column names are resolved against the generic types used by
        /// this query's <c>Select</c>, <c>From</c>, and <c>Join</c> clauses (in that precedence order), using
        /// the same "ClassName.PropertyName" / plain "PropertyName" resolution rules as
        /// <see cref="OrderBy(IEnumerable{OrderBy}, bool)"/>.
        /// </summary>
        /// <remarks>
        /// Each JSON property value can be either:
        /// <list type="bullet">
        /// <item>A JSON primitive (string, number, bool, or null) — interpreted as an equality comparison.</item>
        /// <item>A JSON array — interpreted as an <c>IN</c> comparison against the array elements.</item>
        /// <item>A nested JSON object of the shape <c>{ "op": "gt", "value": 18 }</c> — interpreted using the
        /// operator abbreviation in <c>op</c> applied against <c>value</c>. Supported operators: <c>eq</c>,
        /// <c>ne</c>, <c>gt</c>, <c>gte</c>, <c>lt</c>, <c>lte</c>, and <c>~</c> (LIKE/Contains). When
        /// <c>value</c> is a JSON array in this shape, <c>eq</c>/<c>ne</c> produce <c>IN</c>/<c>NOT IN</c>.</item>
        /// </list>
        /// All property filters are combined using <paramref name="combineOperator"/> ("and" or "or", case-insensitive).
        /// This method sets the base WHERE predicate (equivalent to calling <see cref="Where(LambdaExpression)"/>);
        /// it does not chain with any previously set WHERE predicate. Use <see cref="AndWhere"/>/<see cref="OrWhere"/>
        /// afterward to add more conditions.
        /// </remarks>
        /// <param name="filter">A JsonObject whose properties specify the column filters to apply.</param>
        /// <param name="combineOperator">How to combine the individual property filters: "and" or "or" (default "and").</param>
        /// <param name="ignoreInvalidColumns">
        /// When true, properties whose column name cannot be resolved to a known type/property are silently
        /// skipped instead of throwing. Defaults to false.
        /// </param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="filter"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="combineOperator"/> is not "and"/"or", a column cannot be resolved (and
        /// <paramref name="ignoreInvalidColumns"/> is false), or an unsupported <c>op</c> value is used.
        /// </exception>
        public SqlQuery<T> Where(JsonObject filter, string combineOperator = "and", bool ignoreInvalidColumns = false)
        {
            var lambda = BuildJsonPredicate(filter, combineOperator, ignoreInvalidColumns);
            return Where(lambda);
        }

        // Builds a combined LambdaExpression predicate from a JsonObject's properties, resolving each column
        // against the Select/From/Join generic types of this query. Shared by the Where/AndWhere/OrWhere
        // JsonObject overloads.
        private LambdaExpression BuildJsonPredicate(JsonObject filter, string combineOperator, bool ignoreInvalidColumns)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));

            if (!string.Equals(combineOperator, "and", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(combineOperator, "or", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Only 'and' and 'or' are supported.", nameof(combineOperator));

            var useAnd = string.Equals(combineOperator, "and", StringComparison.OrdinalIgnoreCase);

            // Map from resolved entity type to its ParameterExpression, so all filters against the
            // same type reuse a single parameter (needed to build a valid multi-parameter lambda).
            var paramsByType = new Dictionary<Type, ParameterExpression>();
            Expression? combined = null;

            foreach (var kvp in filter)
            {
                var column = kvp.Key;
                var jsonValue = kvp.Value;

                Type targetType;
                string propertyName;
                try
                {
                    (targetType, propertyName) = ResolveColumn(column);
                }
                catch (ArgumentException)
                {
                    if (ignoreInvalidColumns) continue;
                    throw;
                }

                if (!paramsByType.TryGetValue(targetType, out var param))
                {
                    param = Expression.Parameter(targetType, $"e{paramsByType.Count + 1}");
                    paramsByType[targetType] = param;
                }

                var property = Expression.PropertyOrField(param, propertyName);
                var comparison = BuildJsonComparison(property, jsonValue, column);

                combined = combined == null
                    ? comparison
                    : (useAnd ? Expression.AndAlso(combined, comparison) : Expression.OrElse(combined, comparison));
            }

            if (combined == null)
                throw new ArgumentException("No valid column filters were found in the supplied JsonObject.", nameof(filter));

            var funcType = Expression.GetFuncType(paramsByType.Values.Select(p => p.Type).Concat(new[] { typeof(bool) }).ToArray());
            return Expression.Lambda(funcType, combined, paramsByType.Values);
        }

        // Builds a comparison expression for a single JSON property against the resolved target property.
        private static Expression BuildJsonComparison(MemberExpression property, JsonNode? jsonValue, string column)
        {
            // Nested operator object: { "op": "gt", "value": 18 }
            if (jsonValue is JsonObject opObject)
            {
                if (!opObject.TryGetPropertyValue("op", out var opNode) || opNode == null)
                    throw new ArgumentException($"Filter for column '{column}' is an object but is missing the required 'op' property.");

                var op = opNode.GetValue<string>();
                opObject.TryGetPropertyValue("value", out var valueNode);

                return op.ToLowerInvariant() switch
                {
                    "eq" => BuildEqualityOrIn(property, valueNode, column, negate: false),
                    "ne" => BuildEqualityOrIn(property, valueNode, column, negate: true),
                    "gt" => Expression.GreaterThan(property, CoerceConstant(valueNode, property.Type, column)),
                    "gte" => Expression.GreaterThanOrEqual(property, CoerceConstant(valueNode, property.Type, column)),
                    "lt" => Expression.LessThan(property, CoerceConstant(valueNode, property.Type, column)),
                    "lte" => Expression.LessThanOrEqual(property, CoerceConstant(valueNode, property.Type, column)),
                    "~" => BuildLike(property, valueNode, column),
                    _ => throw new ArgumentException($"Unsupported operator '{op}' for column '{column}'.")
                };
            }

            // Plain JsonValue (primitive) or array => equality / IN
            return BuildEqualityOrIn(property, jsonValue, column, negate: false);
        }

        // Builds an equality (or IN) comparison; arrays produce IN/NOT IN, scalars produce =/<>, and null produces IS [NOT] NULL.
        private static Expression BuildEqualityOrIn(MemberExpression property, JsonNode? valueNode, string column, bool negate)
        {
            if (valueNode is JsonArray array)
            {
                var elementType = property.Type;
                var values = array.Select(n => CoerceValue(n, elementType, column)).ToArray();
                var typedArray = Array.CreateInstance(elementType, values.Length);
                for (int i = 0; i < values.Length; i++) typedArray.SetValue(values[i], i);

                var containsMethod = typeof(Enumerable).GetMethods(BindingFlags.Static | BindingFlags.Public)
                    .First(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2)
                    .MakeGenericMethod(elementType);

                var constantArray = Expression.Constant(typedArray, elementType.MakeArrayType());
                var containsCall = Expression.Call(containsMethod, constantArray, property);
                return negate ? Expression.Not(containsCall) : containsCall;
            }

            var constant = CoerceConstant(valueNode, property.Type, column);
            return negate ? Expression.NotEqual(property, constant) : Expression.Equal(property, constant);
        }

        // Builds a LIKE-style comparison using string.Contains, matching the translator's existing LIKE support.
        private static Expression BuildLike(MemberExpression property, JsonNode? valueNode, string column)
        {
            if (property.Type != typeof(string))
                throw new ArgumentException($"The '~' (LIKE) operator can only be used on string columns; column '{column}' is of type '{property.Type.Name}'.");

            var value = valueNode?.GetValue<string>() ?? throw new ArgumentException($"Filter for column '{column}' using '~' requires a non-null string value.");
            var containsMethod = typeof(string).GetMethod(nameof(string.Contains), new[] { typeof(string) })!;
            return Expression.Call(property, containsMethod, Expression.Constant(value));
        }

        // Coerces a JsonNode to the target CLR type and wraps it in a ConstantExpression, handling null specially.
        private static ConstantExpression CoerceConstant(JsonNode? valueNode, Type targetType, string column)
        {
            var value = CoerceValue(valueNode, targetType, column);
            return Expression.Constant(value, targetType);
        }

        // Coerces a JsonNode to the target CLR type (handling Nullable<T> and null).
        private static object? CoerceValue(JsonNode? valueNode, Type targetType, string column)
        {
            if (valueNode == null)
                return null;

            var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (valueNode is JsonValue jsonValue)
            {
                if (underlyingType == typeof(string))
                    return jsonValue.GetValue<object>()?.ToString();

                if (underlyingType.IsEnum)
                {
                    var raw = jsonValue.GetValue<object>();
                    return raw is string s ? Enum.Parse(underlyingType, s, ignoreCase: true) : Enum.ToObject(underlyingType, Convert.ChangeType(raw, Enum.GetUnderlyingType(underlyingType)));
                }

                var rawValue = jsonValue.GetValue<object>();
                return Convert.ChangeType(rawValue, underlyingType, System.Globalization.CultureInfo.InvariantCulture);
            }

            throw new ArgumentException($"Filter value for column '{column}' must be a JSON primitive, null, or array, not a nested object in this context.");
        }

        // Resolves a (possibly dotted) column name against the Select/From/Join generic types of this query,
        // returning the resolved type and property name. Shares resolution rules with BuildOrderByExpression.
        private (Type targetType, string propertyName) ResolveColumn(string column)
        {
            if (string.IsNullOrWhiteSpace(column))
                throw new ArgumentException("Column name must be specified.");

            var candidateTypes = GetOrderByCandidateEntityTypes();

            var dotIndex = column.IndexOf('.');
            if (dotIndex >= 0)
            {
                var className = column.Substring(0, dotIndex);
                var propertyName = column.Substring(dotIndex + 1);

                if (string.IsNullOrWhiteSpace(className) || string.IsNullOrWhiteSpace(propertyName))
                    throw new ArgumentException($"Column '{column}' is not a valid 'ClassName.PropertyName' expression.");

                var targetType = candidateTypes.FirstOrDefault(t => string.Equals(t.Name, className, StringComparison.Ordinal));
                if (targetType == null)
                    throw new ArgumentException($"Column '{column}' references class '{className}' which does not match any of the Select, From, or Join types of this query.");

                if (targetType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance) == null)
                    throw new ArgumentException($"Column '{column}': property '{propertyName}' not found on type '{targetType.Name}'.");

                return (targetType, propertyName);
            }
            else
            {
                var targetType = candidateTypes.FirstOrDefault(t => t.GetProperty(column, BindingFlags.Public | BindingFlags.Instance) != null);
                if (targetType == null)
                    throw new ArgumentException($"Column '{column}' does not match any property on the Select, From, or Join types of this query.");

                return (targetType, column);
            }
        }

        public SqlQuery<T> AndWhere(Expression<Func<T, bool>> predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            this.selectNode.ChainedWherePredicates.Add((predicate, "AND"));
            return this;
        }

        public SqlQuery<T> AndWhere<T1>(Expression<Func<T, T1, bool>> predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            this.selectNode.ChainedWherePredicates.Add((predicate, "AND"));
            return this;
        }

        public SqlQuery<T> AndWhere<T1, T2>(Expression<Func<T, T1, T2, bool>> predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            this.selectNode.ChainedWherePredicates.Add((predicate, "AND"));
            return this;
        }

        /// <summary>
        /// Dynamically builds a predicate from a <see cref="JsonObject"/> (using the same column resolution
        /// and value/operator semantics as <see cref="Where(JsonObject, string, bool)"/>) and appends it as a
        /// chained predicate combined with <c>AND</c>, without replacing the base WHERE predicate.
        /// </summary>
        /// <param name="filter">A JsonObject whose properties specify the column filters to apply.</param>
        /// <param name="combineOperator">How to combine the individual property filters within this JsonObject: "and" or "or" (default "and").</param>
        /// <param name="ignoreInvalidColumns">
        /// When true, properties whose column name cannot be resolved to a known type/property are silently
        /// skipped instead of throwing. Defaults to false.
        /// </param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="filter"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="combineOperator"/> is not "and"/"or", a column cannot be resolved (and
        /// <paramref name="ignoreInvalidColumns"/> is false), or an unsupported <c>op</c> value is used.
        /// </exception>
        public SqlQuery<T> AndWhere(JsonObject filter, string combineOperator = "and", bool ignoreInvalidColumns = false)
        {
            var lambda = BuildJsonPredicate(filter, combineOperator, ignoreInvalidColumns);
            this.selectNode.ChainedWherePredicates.Add((lambda, "AND"));
            return this;
        }

        public SqlQuery<T> OrWhere(Expression<Func<T, bool>> predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            this.selectNode.ChainedWherePredicates.Add((predicate, "OR"));
            return this;
        }

        public SqlQuery<T> OrWhere<T1>(Expression<Func<T, T1, bool>> predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            this.selectNode.ChainedWherePredicates.Add((predicate, "OR"));
            return this;
        }

        public SqlQuery<T> OrWhere<T1, T2>(Expression<Func<T, T1, T2, bool>> predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            this.selectNode.ChainedWherePredicates.Add((predicate, "OR"));
            return this;
        }

        /// <summary>
        /// Dynamically builds a predicate from a <see cref="JsonObject"/> (using the same column resolution
        /// and value/operator semantics as <see cref="Where(JsonObject, string, bool)"/>) and appends it as a
        /// chained predicate combined with <c>OR</c>, without replacing the base WHERE predicate.
        /// </summary>
        /// <param name="filter">A JsonObject whose properties specify the column filters to apply.</param>
        /// <param name="combineOperator">How to combine the individual property filters within this JsonObject: "and" or "or" (default "and").</param>
        /// <param name="ignoreInvalidColumns">
        /// When true, properties whose column name cannot be resolved to a known type/property are silently
        /// skipped instead of throwing. Defaults to false.
        /// </param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="filter"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="combineOperator"/> is not "and"/"or", a column cannot be resolved (and
        /// <paramref name="ignoreInvalidColumns"/> is false), or an unsupported <c>op</c> value is used.
        /// </exception>
        public SqlQuery<T> OrWhere(JsonObject filter, string combineOperator = "and", bool ignoreInvalidColumns = false)
        {
            var lambda = BuildJsonPredicate(filter, combineOperator, ignoreInvalidColumns);
            this.selectNode.ChainedWherePredicates.Add((lambda, "OR"));
            return this;
        }

        private void AddOrderBy(LambdaExpression expression, bool descending)
        {
            if (expression == null) throw new ArgumentNullException(nameof(expression));
            
            var body = expression.Body;
            
            // Unwrap UnaryExpression (Convert/ConvertChecked)
            if (body is UnaryExpression unary && 
                (unary.NodeType == ExpressionType.Convert || unary.NodeType == ExpressionType.ConvertChecked))
            {
                body = unary.Operand;
            }
            
            if (body is not MemberExpression)
                throw new NotSupportedException("Only simple member OrderBy/ThenBy supported.");
                
            this.selectNode.OrderByExpr.Add((expression, descending));
        }

        public SqlQuery<T> OrderBy(Expression<Func<T, object>> expression)
        {
            AddOrderBy(expression, false);
            return this;
        }
        public SqlQuery<T> OrderBy<T1>(Expression<Func<T, T1, object>> expression)
        {
            AddOrderBy(expression, false);
            return this;
        }
        public SqlQuery<T> OrderByDescending(Expression<Func<T, object>> expression)
        {
            AddOrderBy(expression, true);
            return this;
        }
        public SqlQuery<T> OrderByDescending<T1>(Expression<Func<T, T1, object>> expression)
        {
            AddOrderBy(expression, true);
            return this;
        }
        public SqlQuery<T> ThenBy(Expression<Func<T, object>> expression)
        {
            AddOrderBy(expression, false);
            return this;
        }
        public SqlQuery<T> ThenBy<T1>(Expression<Func<T, T1, object>> expression)
        {
            AddOrderBy(expression, false);
            return this;
        }
        public SqlQuery<T> ThenByDescending(Expression<Func<T, object>> expression)
        {
            AddOrderBy(expression, true);
            return this;
        }
        public SqlQuery<T> ThenByDescending<T1>(Expression<Func<T, T1, object>> expression)
        {
            AddOrderBy(expression, true);
            return this;
        }

        /// <summary>
        /// Dynamically builds and adds an OrderBy expression for each <see cref="OrderBy"/> element in
        /// <paramref name="sortBy"/>. For each element, <see cref="OrderBy.Column"/> is resolved against the
        /// generic types used by this query's <c>Select</c>, <c>From</c>, and <c>Join</c> clauses (in that
        /// precedence order):
        /// <list type="bullet">
        /// <item>If the column is in dotted notation ("ClassName.PropertyName"), the class name must match the
        /// simple name of one of those types, and the property name must exist on that type.</item>
        /// <item>If the column is a plain property name, the first candidate type (searched in
        /// Select, From, then Join precedence) that declares a matching property is used.</item>
        /// </list>
        /// </summary>
        /// <param name="sortBy">The collection of OrderBy specifications to apply.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="sortBy"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when a column cannot be resolved to a known type/property.</exception>
        public SqlQuery<T> OrderBy(IEnumerable<OrderBy> sortBy, bool ignoreInvalidColumns = false)
        {
            if (sortBy == null) throw new ArgumentNullException(nameof(sortBy));

            foreach (var order in sortBy)
            {
                if (order == null || string.IsNullOrWhiteSpace(order.Column))
                    throw new ArgumentException("OrderBy.Column must be specified.", nameof(sortBy));

                var descending = order.Direction == SortDir.Desc;
                LambdaExpression? lambda = null;
                try
                {
                    lambda = BuildOrderByExpression(order.Column);
                }
                catch (ArgumentException)
                {
                    if (!ignoreInvalidColumns)
                        throw;
                }

                if (lambda != null) AddOrderBy(lambda, descending);
            }

            return this;
        }

        // Resolves a (possibly dotted) column name against the Select/From/Join generic types of this query
        // and builds a LambdaExpression suitable for use with AddOrderBy.
        private LambdaExpression BuildOrderByExpression(string column)
        {
            var (targetType, propertyName) = ResolveColumn(column);

            var param = Expression.Parameter(targetType, "x");
            var propertyAccess = Expression.PropertyOrField(param, propertyName);
            var body = Expression.Convert(propertyAccess, typeof(object));
            var funcType = typeof(Func<,>).MakeGenericType(targetType, typeof(object));
            return Expression.Lambda(funcType, body, param);
        }

        // Returns the candidate entity types for OrderBy column resolution, in precedence order:
        // Select generic types, then the From type (T), then Join generic types (in join-added order).
        private List<Type> GetOrderByCandidateEntityTypes()
        {
            var types = new List<Type>();

            if (this.selectNode.SelectExpr is LambdaExpression selectLambda)
            {
                foreach (var p in selectLambda.Parameters)
                {
                    if (!types.Contains(p.Type)) types.Add(p.Type);
                }
            }

            if (!types.Contains(typeof(T))) types.Add(typeof(T));

            foreach (var join in this.selectNode.Joins)
            {
                var joinType = join.GetType();
                if (joinType.IsGenericType)
                {
                    foreach (var t in joinType.GetGenericArguments())
                    {
                        if (!types.Contains(t)) types.Add(t);
                    }
                }
            }

            return types;
        }

        public SqlQuery<T> Skip(int count)
        {
            this.selectNode.Skip = count;
            return this;
        }
        public override SqlQuery<T> Take(int count)
        {
            this.selectNode.Take = count;
            return this;
        }
        public SqlQuery<T> Select(Expression<Func<T,object>> selector)
        {
            if (selector == null) throw new ArgumentNullException(nameof(selector));
            this.selectNode.SelectExpr = selector;
            return this;
        }

        public SqlQuery<T> Select<T1>()
        {
            // Build SELECT column expression that projects all columns of T1 (identity projection).
            var param = Expression.Parameter(typeof(T1), "x");
            var body = Expression.Convert(param, typeof(object));
            var selector = Expression.Lambda<Func<T1, object>>(body, param);
            this.selectNode.SelectExpr = selector;
            return this;
        }

        public SqlQuery<T> Select<T1>(Expression<Func<T1, object>> selector)
        {
            if (selector == null) throw new ArgumentNullException(nameof(selector));
            this.selectNode.SelectExpr = selector;
            return this;
        }

        public SqlQuery<T> Select<T1,T2>(Expression<Func<T1, T2, object>> selector)
        {
            if (selector == null) throw new ArgumentNullException(nameof(selector));
            this.selectNode.SelectExpr = selector;
            return this;
        }
        public SqlQuery<T> Select<T1,T2,T3>(Expression<Func<T1, T2, T3, object>> selector)
        {
            if (selector == null) throw new ArgumentNullException(nameof(selector));
            this.selectNode.SelectExpr = selector;
            return this;
        }
        public SqlQuery<T> Select<T1, T2, T3, T4>(Expression<Func<T1, T2, T3, T4, object>> selector)
        {
            if (selector == null) throw new ArgumentNullException(nameof(selector));

            this.selectNode.SelectExpr = selector;

            return this;
        }
        public SqlQuery<T> Select<T1, T2, T3, T4, T5>(Expression<Func<T1, T2, T3, T4, T5, object>> selector)
        {
            if (selector == null) throw new ArgumentNullException(nameof(selector));

            this.selectNode.SelectExpr = selector;

            return this;
        }
        public SqlQuery<T> Select<T1, T2, T3, T4, T5, T6>(Expression<Func<T1, T2, T3, T4, T5, T6, object>> selector)
        {
            if (selector == null) throw new ArgumentNullException(nameof(selector));

            this.selectNode.SelectExpr = selector;

            return this;
        }

        public SqlQuery<T> Distinct()
        {
            this.selectNode.Distinct = true;
            return this;
        }

        public SqlQuery<T> Join<TLeft,TRight>(
            Expression<Func<TLeft, TRight, bool>> onPredicate)
        {
            if (onPredicate == null) throw new ArgumentNullException(nameof(onPredicate));

            this.selectNode.Joins.Add(
                new SqlJoin<TLeft, TRight>()
                {
                    onPredicate = onPredicate
                }
                );
            return this;
        }
        public SqlQuery<T> Join<TRight>(
            Expression<Func<T, TRight, bool>> onPredicate)
        {
            if (onPredicate == null) throw new ArgumentNullException(nameof(onPredicate));

            this.selectNode.Joins.Add(
                new SqlJoin<T, TRight>()
                {
                    onPredicate = onPredicate
                }
                );
            return this;
        }

        public SqlQuery<T> LeftJoin<TLeft, TRight>(
            Expression<Func<TLeft, TRight, bool>> onPredicate)
        {
            if (onPredicate == null) throw new ArgumentNullException(nameof(onPredicate));

            this.selectNode.Joins.Add(
                new SqlJoin<TLeft, TRight>()
                {
                    JoinType = "LEFT",
                    onPredicate = onPredicate
                }
                );
            return this;
        }
        public SqlQuery<T> LeftJoin<TRight>(
            Expression<Func<T, TRight, bool>> onPredicate)
        {
            if (onPredicate == null) throw new ArgumentNullException(nameof(onPredicate));

            this.selectNode.Joins.Add(
                new SqlJoin<T, TRight>()
                {
                    JoinType = "LEFT",
                    onPredicate = onPredicate
                }
                );
            return this;
        }

        public SqlQuery<T> RightJoin<TLeft, TRight>(
            Expression<Func<TLeft, TRight, bool>> onPredicate)
        {
            if (onPredicate == null) throw new ArgumentNullException(nameof(onPredicate));

            this.selectNode.Joins.Add(
                new SqlJoin<TLeft, TRight>()
                {
                    JoinType = "RIGHT",
                    onPredicate = onPredicate
                }
                );
            return this;
        }
        public SqlQuery<T> RightJoin<TRight>(
            Expression<Func<T, TRight, bool>> onPredicate)
        {
            if (onPredicate == null) throw new ArgumentNullException(nameof(onPredicate));

            this.selectNode.Joins.Add(
                new SqlJoin<T, TRight>()
                {
                    JoinType = "RIGHT",
                    onPredicate = onPredicate
                }
                );
            return this;
        }
    }
}
