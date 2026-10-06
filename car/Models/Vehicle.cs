namespace car.Models
{
    /// <summary>一台車。加油與保養紀錄用 VehicleId 對應到車輛，改名不影響紀錄。</summary>
    public class Vehicle
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>車輛名稱，例如「白色 Altis」「老婆的車」。</summary>
        public string Name { get; set; } = "";

        /// <summary>車牌（選填）。</summary>
        public string Plate { get; set; } = "";

        public override string ToString() => Name;
    }
}
