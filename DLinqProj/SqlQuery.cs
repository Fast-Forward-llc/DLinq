using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace DLinq
{
    public abstract class SqlQuery 
    {
        public SqlSelectNode selectNode = new SqlSelectNode();
        public Type ElementType { get; protected set; }
        public IQueryProvider Provider { get; protected set; }

        public static LambdaExpression BuildPredicate(FilterCriteria[] filters, string boolOperator)
        {
            if (filters == null || filters.Length == 0)
                throw new ArgumentException("At least one filter is required.");

            if (!(string.Equals(boolOperator,"AND", StringComparison.OrdinalIgnoreCase) || string.Equals(boolOperator, "OR", StringComparison.OrdinalIgnoreCase)))
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
        public (string sql, object parameters) ToDeleteSql(Expression<Func<T, bool>> wherePredicate, Options? options = null)
        {
            if (Provider is QueryProvider qp)
            {
                return qp.Translator.GenerateDeleteSql(typeof(T), wherePredicate, options);
            }
            throw new NotSupportedException("ToDeleteSql is only supported for SqlQuery using QueryProvider.");
        }

        // Overload to generate Delete SQL for an entity instance by its key fields
        public (string sql, object parameters) ToDeleteSql(T entity, Options? options = null)
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
        public SqlQuery<T> OrderByDescending(Expression<Func<T, object>> expression)
        {
            AddOrderBy(expression, true);
            return this;
        }
        public SqlQuery<T> ThenBy(Expression<Func<T, object>> expression)
        {
            AddOrderBy(expression, false);
            return this;
        }
        public SqlQuery<T> ThenByDescending(Expression<Func<T, object>> expression)
        {
            AddOrderBy(expression, true);
            return this;
        }
        public SqlQuery<T> Skip(int? count)
        {
            this.selectNode.Skip = count;
            return this;
        }
        public SqlQuery<T> Take(int? count)
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
