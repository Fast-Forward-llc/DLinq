namespace DLinq
{
    public class Options
    {
        public string? TableName { get; set; }
    }

    public class UpdateOptions : Options
    {
        public bool SelectAfterMutation { get; set; } = false;
    }

    public class InsertOptions :UpdateOptions
    {
        public InsertOptions()
        {
        }
        public InsertOptions(UpdateOptions updateOptions)
        {
            if (updateOptions != null)
            {
                this.TableName = updateOptions.TableName;
                this.SelectAfterMutation = updateOptions.SelectAfterMutation;
            }
        }
    }
}
