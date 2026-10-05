namespace car.Models
{
    /// <summary>加油清單的一列（畫面用）：原始紀錄 + 算好的文字。</summary>
    public class FuelRow
    {
        public required FuelRecord Record { get; init; }
        public string Day { get; init; } = "";
        public string Year { get; init; } = "";
        public string Title { get; init; } = "";
        public string Subtitle { get; init; } = "";
        public string Economy { get; init; } = "";
        public string Distance { get; init; } = "";
        public bool HasEconomy { get; init; }

        // 螢幕閱讀器與 UI 自動化讀到的名稱
        public override string ToString() => $"{Day} {Title}";
    }

    /// <summary>保養清單的一列（畫面用）。</summary>
    public class MaintenanceRow
    {
        public required MaintenanceRecord Record { get; init; }
        public string Day { get; init; } = "";
        public string Year { get; init; } = "";
        public string Title { get; init; } = "";
        public string Subtitle { get; init; } = "";
        public string AmountText { get; init; } = "";

        public override string ToString() => $"{Day} {Title}";
    }
}
