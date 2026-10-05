using car.Models;

namespace car.Services
{
    /// <summary>
    /// 保養統計。Latest：里程最高的一筆保養。
    /// NextDueOdometer：最近一筆有填「下次保養里程」的紀錄所填的值。
    /// RemainingKm：距離下次保養還有幾公里（負數 = 已超過）；沒填下次保養或不知道目前里程時為 null。
    /// </summary>
    public record MaintenanceSummary(int Count, decimal TotalAmount, MaintenanceRecord? Latest,
                                     double? NextDueOdometer, double? RemainingKm);

    public static class MaintenanceCalculator
    {
        public static List<MaintenanceRecord> Ordered(IEnumerable<MaintenanceRecord> records) =>
            records.OrderBy(r => r.Odometer).ThenBy(r => r.Date).ToList();

        public static MaintenanceSummary Summarize(IEnumerable<MaintenanceRecord> records, double? currentOdometer)
        {
            var s = Ordered(records);
            var latest = s.LastOrDefault();
            var next = s.LastOrDefault(r => r.NextOdometer is > 0)?.NextOdometer;
            double? remaining = next != null && currentOdometer != null ? next - currentOdometer : null;
            return new MaintenanceSummary(s.Count, s.Sum(r => r.Amount), latest, next, remaining);
        }
    }

    public static class OdometerCheck
    {
        /// <summary>
        /// 目前里程：加油與保養紀錄裡最高的里程；完全沒有紀錄時為 null。
        /// </summary>
        public static double? Current(IEnumerable<FuelRecord> fuel, IEnumerable<MaintenanceRecord> maintenance)
        {
            var all = fuel.Select(f => f.Odometer).Concat(maintenance.Select(m => m.Odometer)).ToList();
            return all.Count == 0 ? null : all.Max();
        }

        /// <summary>
        /// 檢查日期與里程是否前後矛盾：比較早的日期卻有比較高的里程（或反過來）。
        /// 回傳第一筆矛盾的紀錄（日期, 里程），沒有矛盾時為 null。
        /// </summary>
        public static (DateTime Date, double Odometer)? FindConflict(
            DateTime date, double odometer, IEnumerable<(DateTime Date, double Odometer)> others)
        {
            foreach (var o in others)
            {
                if (o.Date.Date < date.Date && o.Odometer > odometer) return o;
                if (o.Date.Date > date.Date && o.Odometer < odometer) return o;
            }
            return null;
        }
    }
}
