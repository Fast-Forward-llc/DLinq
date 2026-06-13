namespace DLinq
{
    public class QueryOptions
    {
        /// <summary>
        /// Default Schema. This schema is only applied if table name lacks a schema
        /// </summary>
        public string? DefaultSchema { get; set; }
        /// <summary>
        /// DB Schema. This schema overrides any schema included in a muti-part table name
        /// </summary>
        public string? Schema { get; set; }
    }

    public class TableOptions: QueryOptions
    {
        public string? TableName { get; set; }
    }

    public class UpdateOptions : TableOptions
    {
        public UpdateOptions() { }

        public UpdateOptions(QueryOptions queryOptions) {
            if (queryOptions == null) return;
            this.Schema = queryOptions.Schema;
            this.DefaultSchema = queryOptions.DefaultSchema;
        }

        public bool SelectAfterMutation { get; set; } = false;
        
    }

    public class InsertOptions :UpdateOptions
    {
        public InsertOptions()
        {
        }
        public InsertOptions(QueryOptions queryOptions)
        {
            if (queryOptions == null) return;
            this.Schema = queryOptions.Schema;
            this.DefaultSchema = queryOptions.DefaultSchema;
        }
        public InsertOptions(UpdateOptions updateOptions): this((QueryOptions)updateOptions)
        {
            if (updateOptions == null) return;
            this.TableName = updateOptions.TableName;
            this.SelectAfterMutation = updateOptions.SelectAfterMutation;
        }
    }
}
