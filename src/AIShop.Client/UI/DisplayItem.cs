namespace AIShop.Client.UI
{
    internal sealed class DisplayItem<T>
    {
        public DisplayItem(T value, string text)
        {
            Value = value;
            Text = text;
        }

        public T Value { get; }

        public string Text { get; }

        public override string ToString()
        {
            return Text;
        }
    }
}
