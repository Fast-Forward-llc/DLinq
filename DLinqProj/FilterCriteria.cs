using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace DLinq
{
    public class FilterCriteria
    {
        public FilterCriteria(Type entityType, string propertyName, ExpressionType @operator, object rightOperand) {
            EntityType = entityType;
            PropertyName = propertyName;
            Operator = @operator;
            RightOperand = rightOperand;
        }

        public Type EntityType { get; private set; }
        public string PropertyName { get; private set; }
        public ExpressionType Operator { get; private set; } = ExpressionType.Equal;
        public object RightOperand { get; private set; }
    }
}
