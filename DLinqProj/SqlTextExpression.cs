using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DLinq
{
    public class SqlTextExpression
    {
        public SqlTextExpression(string sql, object parameters)
        {
            Sql = sql;
            Parameters = parameters;
        }
        public string Sql { get; }
        public object Parameters { get; }
    }
}
