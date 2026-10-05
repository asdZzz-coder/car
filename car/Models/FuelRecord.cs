using System.Text.Json.Serialization;

namespace car.Models
{
    /// <summary>一次加油：日期、當時的里程表讀數、加了幾公升、花多少錢、是否加滿。</summary>
    public class FuelRecord
    {
        public DateTime Date { get; set; } = DateTime.Today;

        /// <summary>加油時里程表上的總公里數。</summary>
        public double Odometer { get; set; }

        public double Liters { get; set; }

        public decimal Amount { get; set; }

        /// <summary>
        /// 是否加滿。油耗用「加滿法」計算：從這次加滿開到下次加滿，用下次加進去的油量（含中間沒加滿的）來算。
        /// 舊資料或匯入時沒有這個欄位，一律當作加滿。
        /// </summary>
        public bool IsFull { get; set; } = true;

        public string Note { get; set; } = "";

        /// <summary>每公升單價（金額 ÷ 公升，僅供畫面使用，不存檔）。</summary>
        [JsonIgnore]
        public decimal? UnitPrice => Liters > 0 ? Amount / (decimal)Liters : null;
    }
}
