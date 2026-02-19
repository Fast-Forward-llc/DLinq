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
    public abstract class SqlQuery //: IQueryable
    {
        public SqlSelectNode selectNode = new SqlSelectNode();
        public Type ElementType { get; protected set; }
        public IQueryProvider Provider { get; protected set; }

        //public Expression Expression => throw new NotImplementedException();

        //public IEnumerator GetEnumerator() => throw new NotImplementedException();

    }

    public class SqlQuery<T> : SqlQuery //, IOrderedQueryable<T>
    {
        public SqlQuery(QueryProvider provider)
        {
            ElementType = typeof(T);
            Provider = provider;
            selectNode.FromEntity = ElementType;
            //Expression = expression ?? Expression.Constant(this);
        }

        //public new IEnumerator<T> GetEnumerator() => throw new NotImplementedException();
        //IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        // Method to generate Insert SQL for the specified entity
        public (string sql, object parameters) ToInsertSql(T entity, InsertOptions? options = null)
        {
            if (Provider is QueryProvider qp)
            {
                return qp.Translator.GenerateInsertSql(entity, options);
            }
            throw new NotSupportedException("ToInsertSql is only supported for SqlQuery using QueryProvider.");
        }

        public (string sql, object parameters) ToInsertSql<R>(T entity, InsertOptions? options = null)
        {
            if (Provider is QueryProvider qp)
            {
                return qp.Translator.GenerateInsertSql(entity, options);
            }
            throw new NotSupportedException("ToInsertSql is only supported for SqlQuery using QueryProvider.");
        }

        // Method to generate Update SQL for the specified entity
        public (string sql, object parameters) ToUpdateSql(T entity, UpdateOptions? options = null)
        {
            if (Provider is QueryProvider qp)
            {
                return qp.Translator.GenerateUpdateSql(entity, options);
            }
            throw new NotSupportedException("ToUpdateSql is only supported for SqlQuery using QueryProvider.");
        }

        // Method to generate Update SQL for the specified entity with a where predicate
        public (string sql, object parameters) ToUpdateSql(T entity, Expression<Func<T, bool>> wherePredicate, UpdateOptions? options = null)
        {
            if (Provider is QueryProvider qp)
            {
                return qp.Translator.GenerateUpdateSql(entity, wherePredicate?.Body, options);
            }
            throw new NotSupportedException("ToUpdateSql is only supported for SqlQuery using QueryProvider.");
        }

        // Existing overload for backward compatibility
        public (string sql, object parameters) ToUpdateSql(T entity)
        {
            if (Provider is QueryProvider qp)
            {
                return qp.Translator.GenerateUpdateSql(entity);
            }
            throw new NotSupportedException("ToUpdateSql is only supported for SqlQuery using QueryProvider.");
        }

        // Method to generate Delete SQL for the specified entity type with a where predicate
        public (string sql, object parameters) ToDeleteSql(Expression<Func<T, bool>> wherePredicate, Options? options = null)
        {
            if (Provider is QueryProvider qp)
            {
                return qp.Translator.GenerateDeleteSql(typeof(T), wherePredicate?.Body, options);
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
        public SqlQuery<T> OrderByDecending(Expression<Func<T, object>> expression)
        {
            AddOrderBy(expression, true);
            return this;
        }
        public SqlQuery<T> ThenBy(Expression<Func<T, object>> expression)
        {
            AddOrderBy(expression, false);
            return this;
        }
        public SqlQuery<T> ThenByDecending(Expression<Func<T, object>> expression)
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
    }
}
