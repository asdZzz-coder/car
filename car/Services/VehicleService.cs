using car.Models;

namespace car.Services
{
    public enum VehicleNameError { None, Empty, TooLong, Duplicate }

    /// <summary>
    /// 多台車的管理：補齊舊資料、驗證名稱、依車輛篩選紀錄、刪除車輛、合併匯入的資料。
    /// </summary>
    public static class VehicleService
    {
        public const string DefaultName = "我的車";
        public const int MaxNameLength = 20;

        /// <summary>
        /// 讓資料一定可以用：至少有一台車、車輛 Id 不重複、每筆紀錄都屬於某台存在的車、上次選的車存在。
        /// 舊版（只有一台車）的資料沒有車輛，會建立「我的車」並把所有紀錄歸給它。
        /// </summary>
        public static void Normalize(CarData data)
        {
            data.Vehicles ??= new();
            data.Fuel ??= new();
            data.Maintenance ??= new();
            data.SelectedVehicleId ??= "";

            var seen = new HashSet<string>();
            foreach (var v in data.Vehicles.ToList())
            {
                if (v == null) { data.Vehicles.Remove(v!); continue; }
                if (string.IsNullOrWhiteSpace(v.Id) || !seen.Add(v.Id))
                {
                    v.Id = Guid.NewGuid().ToString("N");
                    seen.Add(v.Id);
                }
                v.Name = (v.Name ?? "").Trim();
                v.Plate = (v.Plate ?? "").Trim();
                if (v.Name.Length == 0) v.Name = UniqueName(data.Vehicles, DefaultName);
            }
            if (data.Vehicles.Count == 0)
                data.Vehicles.Add(new Vehicle { Name = DefaultName });

            var first = data.Vehicles[0].Id;
            foreach (var f in data.Fuel)
                if (!seen.Contains(f.VehicleId ?? "")) f.VehicleId = first;
            foreach (var m in data.Maintenance)
                if (!seen.Contains(m.VehicleId ?? "")) m.VehicleId = first;
            if (data.Vehicles.All(v => v.Id != data.SelectedVehicleId))
                data.SelectedVehicleId = first;
        }

        /// <summary>名稱已被使用時加上「 2」「 3」…，例如「我的車 2」。</summary>
        internal static string UniqueName(IEnumerable<Vehicle> vehicles, string name)
        {
            var used = vehicles.Select(v => v.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!used.Contains(name)) return name;
            for (int i = 2; ; i++)
                if (!used.Contains($"{name} {i}")) return $"{name} {i}";
        }

        /// <summary>檢查車輛名稱；editing 為正在改名的那台（不跟自己比）。</summary>
        public static VehicleNameError Validate(string name, IEnumerable<Vehicle> vehicles, Vehicle? editing = null)
        {
            name = name.Trim();
            if (name.Length == 0) return VehicleNameError.Empty;
            if (name.Length > MaxNameLength) return VehicleNameError.TooLong;
            if (vehicles.Any(v => v != editing && string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)))
                return VehicleNameError.Duplicate;
            return VehicleNameError.None;
        }

        public static List<FuelRecord> FuelOf(CarData data, string vehicleId) =>
            data.Fuel.Where(f => f.VehicleId == vehicleId).ToList();

        public static List<MaintenanceRecord> MaintenanceOf(CarData data, string vehicleId) =>
            data.Maintenance.Where(m => m.VehicleId == vehicleId).ToList();

        /// <summary>刪除車輛與它的所有紀錄。最後一台車不能刪（回傳 false）。</summary>
        public static bool Delete(CarData data, Vehicle vehicle)
        {
            if (data.Vehicles.Count <= 1 || !data.Vehicles.Contains(vehicle)) return false;
            data.Vehicles.Remove(vehicle);
            data.Fuel.RemoveAll(f => f.VehicleId == vehicle.Id);
            data.Maintenance.RemoveAll(m => m.VehicleId == vehicle.Id);
            if (data.SelectedVehicleId == vehicle.Id) data.SelectedVehicleId = data.Vehicles[0].Id;
            return true;
        }

        /// <summary>
        /// 把 Excel 匯入的資料併進現有資料，回傳新增的筆數。
        /// - 匯入的車輛依名稱對應到現有的車（不分大小寫），沒有的就新增。
        /// - 沒寫車輛的紀錄（VehicleId 為空）歸給 defaultVehicleId（目前選的車）。
        /// - 同一台車、同一天、同里程的紀錄視為重複，略過。
        /// - replace = true：先清掉所有紀錄與其他車輛，只留目前選的車，完全以 Excel 為準。
        /// </summary>
        public static int Merge(CarData target, CarData imported, string defaultVehicleId, bool replace)
        {
            if (replace)
            {
                target.Fuel.Clear();
                target.Maintenance.Clear();
                target.Vehicles.RemoveAll(v => v.Id != defaultVehicleId);
                Normalize(target);
                defaultVehicleId = target.Vehicles[0].Id;
            }

            var map = new Dictionary<string, string> { [""] = defaultVehicleId };
            foreach (var v in imported.Vehicles)
            {
                var existing = target.Vehicles.FirstOrDefault(t => string.Equals(t.Name, v.Name, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    existing = new Vehicle { Name = v.Name, Plate = v.Plate };
                    target.Vehicles.Add(existing);
                }
                else if (existing.Plate.Length == 0 && v.Plate.Length > 0)
                {
                    existing.Plate = v.Plate;
                }
                map[v.Id] = existing.Id;
            }

            int added = 0;
            foreach (var f in imported.Fuel)
            {
                f.VehicleId = map.GetValueOrDefault(f.VehicleId ?? "", defaultVehicleId);
                if (target.Fuel.Any(x => x.VehicleId == f.VehicleId && x.Date.Date == f.Date.Date && x.Odometer == f.Odometer)) continue;
                target.Fuel.Add(f);
                added++;
            }
            foreach (var m in imported.Maintenance)
            {
                m.VehicleId = map.GetValueOrDefault(m.VehicleId ?? "", defaultVehicleId);
                if (target.Maintenance.Any(x => x.VehicleId == m.VehicleId && x.Date.Date == m.Date.Date && x.Odometer == m.Odometer)) continue;
                target.Maintenance.Add(m);
                added++;
            }
            return added;
        }
    }
}
