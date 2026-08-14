namespace HSGhost.Input
{
    /// <summary>一条按键绑定：一个动作名对应主键与副键。</summary>
    public sealed class InputBindingEntry
    {
        /// <summary>动作名称。</summary>
        public string ActionName { get; set; }

        /// <summary>主按键。</summary>
        public string PrimaryKey { get; set; }

        /// <summary>副按键。</summary>
        public string SecondaryKey { get; set; }
    }
}
