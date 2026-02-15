namespace DLinq
{
    public class JoinResult<TLeft, TRight>
    {
        public TLeft Left { get; set; }
        public TRight Right { get; set; }
        public JoinResult(TLeft left, TRight right)
        {
            Left = left;
            Right = right;
        }
    }
}
