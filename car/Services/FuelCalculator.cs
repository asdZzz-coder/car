using car.Models;

namespace car.Services
{
    /// <summary>
    /// 一筆加油的計算結果。
    /// DistanceToNext：這次加油到下一次加油開了幾公里（最新一筆為 null）。
    /// KmPerLiter：從這次加滿開到下次加滿的平均油耗；這次沒加滿、或後面還沒有加滿的紀錄時為 null。
    /// SegmentDistance / FuelUsed：上述區間開的公里數與用掉的油（下次加滿時加進去的量，含中間沒加滿的）。
    /// </summary>
    public record FuelStats(double? DistanceToNext, double? KmPerLiter, double? SegmentDistance, double? FuelUsed);

    /// <summary>
    /// 全部加油紀錄的統計。AverageKmPerLiter 與 CostPerKm 只算「加滿到加滿」的區間，
    /// 至少要有兩筆加滿的紀錄才算得出來。
    /// </summary>
    public record FuelSummary(int Count, decimal TotalAmount, double TotalLiters,
                              double? AverageKmPerLiter, decimal? CostPerKm, double TrackedDistance);

    /// <summary>
    /// 油耗計算（加滿法）：
    ///   每次都加滿時，下次加進去的油 = 這段路用掉的油，
    ///   所以「這次 → 下次」的油耗 = (下次里程 − 這次里程) ÷ 下次加油量。
    /// 中間有沒加滿的，就一路累計到下一次加滿為止，再一起算。
    /// 紀錄一律依里程排序（不是依日期），補登舊資料也不會算錯。
    /// </summary>
    public static class FuelCalculator
    {
        public static List<FuelRecord> Ordered(IEnumerable<FuelRecord> records) =>
            records.OrderBy(r => r.Odometer).ThenBy(r => r.Date).ToList();

        public static Dictionary<FuelRecord, FuelStats> Calculate(IEnumerable<FuelRecord> records)
        {
            var s = Ordered(records);
            var result = new Dictionary<FuelRecord, FuelStats>();
            for (int i = 0; i < s.Count; i++)
            {
                double? toNext = i + 1 < s.Count ? Positive(s[i + 1].Odometer - s[i].Odometer) : null;
                var segment = s[i].IsFull ? Segment(s, i) : null;
                result[s[i]] = new FuelStats(toNext, segment?.KmPerLiter, segment?.Distance, segment?.Liters);
            }
            return result;
        }

        public static FuelSummary Summarize(IEnumerable<FuelRecord> records)
        {
            var s = Ordered(records);
            double distance = 0, liters = 0;
            decimal cost = 0;
            for (int i = 0; i < s.Count; i++)
            {
                if (!s[i].IsFull || Segment(s, i) is not { } seg) continue;
                distance += seg.Distance;
                liters += seg.Liters;
                cost += seg.Cost;
            }

            return new FuelSummary(
                s.Count,
                s.Sum(r => r.Amount),
                s.Sum(r => r.Liters),
                liters > 0 ? distance / liters : null,
                distance > 0 ? cost / (decimal)distance : null,
                distance);
        }

        private record SegmentInfo(double Distance, double Liters, decimal Cost)
        {
            public double KmPerLiter => Distance / Liters;
        }

        /// <summary>從第 start 筆（加滿）開到下一筆加滿的區間；找不到下一筆加滿、或資料不合理時為 null。</summary>
        private static SegmentInfo? Segment(List<FuelRecord> s, int start)
        {
            double liters = 0;
            decimal cost = 0;
            for (int j = start + 1; j < s.Count; j++)
            {
                liters += s[j].Liters;
                cost += s[j].Amount;
                if (!s[j].IsFull) continue;
                var distance = s[j].Odometer - s[start].Odometer;
                return distance > 0 && liters > 0 ? new SegmentInfo(distance, liters, cost) : null;
            }
            return null;
        }

        private static double? Positive(double v) => v > 0 ? v : null;
    }
}
