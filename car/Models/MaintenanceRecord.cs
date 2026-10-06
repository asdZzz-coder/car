namespace car.Models
{
    /// <summary>一次保養：日期、當時的里程、做了哪些項目、花多少錢，以及（選填）下次該保養的里程。</summary>
    public class MaintenanceRecord
    {
        /// <summary>哪一台車（Vehicle.Id）。舊版資料沒有這個欄位，載入時會補成第一台車。</summary>
        public string VehicleId { get; set; } = "";

        public DateTime Date { get; set; } = DateTime.Today;

        /// <summary>保養時里程表上的總公里數。</summary>
        public double Odometer { get; set; }

        /// <summary>保養項目，例如「機油、機油芯、空氣濾網」。</summary>
        public string Items { get; set; } = "";

        public decimal Amount { get; set; }

        public string Shop { get; set; } = "";

        /// <summary>下次保養里程；沒填為 null。</summary>
        public double? NextOdometer { get; set; }

        public string Note { get; set; } = "";
    }
}
