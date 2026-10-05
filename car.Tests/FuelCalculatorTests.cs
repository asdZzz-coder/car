using car.Models;
using car.Services;

namespace car.Tests
{
    /// <summary>油耗計算（加滿法）的自動測試。</summary>
    public class FuelCalculatorTests
    {
        private static FuelRecord Fill(double odo, double liters, decimal amount, bool full = true, int day = 1) =>
            new() { Date = new DateTime(2026, 1, 1).AddDays(day), Odometer = odo, Liters = liters, Amount = amount, IsFull = full };

        [Fact]
        public void DistanceToNext_IsOdometerDifference_AndLatestHasNone()
        {
            var a = Fill(1000, 40, 1200, day: 1);
            var b = Fill(1500, 40, 1200, day: 2);
            var c = Fill(1980, 38, 1140, day: 3);

            var s = FuelCalculator.Calculate(new[] { c, a, b }); // 順序打亂也一樣

            Assert.Equal(500, s[a].DistanceToNext);
            Assert.Equal(480, s[b].DistanceToNext);
            Assert.Null(s[c].DistanceToNext);
        }

        [Fact]
        public void KmPerLiter_UsesFuelAddedAtNextFullFill()
        {
            // 加滿 → 開 500 公里 → 加滿時加了 40 公升 → 12.5 km/L
            var a = Fill(1000, 35, 1050);
            var b = Fill(1500, 40, 1200, day: 2);

            var s = FuelCalculator.Calculate(new[] { a, b });

            Assert.Equal(12.5, s[a].KmPerLiter);
            Assert.Equal(500, s[a].SegmentDistance);
            Assert.Equal(40, s[a].FuelUsed);
            Assert.Null(s[b].KmPerLiter); // 最新一筆還沒有下次加滿
        }

        [Fact]
        public void PartialFill_IsMergedIntoNextFullFill()
        {
            // 加滿(1000) → 沒加滿(1300, 20L) → 加滿(1600, 25L)：600 公里用了 45 公升
            var a = Fill(1000, 40, 1200);
            var p = Fill(1300, 20, 600, full: false, day: 2);
            var b = Fill(1600, 25, 750, day: 3);

            var s = FuelCalculator.Calculate(new[] { a, p, b });

            Assert.Equal(600.0 / 45, s[a].KmPerLiter!.Value, 6);
            Assert.Equal(300, s[a].DistanceToNext); // 「到下次加油」仍是到下一筆（沒加滿的那筆）
            Assert.Null(s[p].KmPerLiter);
            Assert.Equal(300, s[p].DistanceToNext);
        }

        [Fact]
        public void FirstRecordPartial_HasNoEconomyUntilAFullFillStartsASegment()
        {
            var p = Fill(1000, 20, 600, full: false);
            var a = Fill(1200, 40, 1200, day: 2);
            var b = Fill(1700, 40, 1200, day: 3);

            var s = FuelCalculator.Calculate(new[] { p, a, b });

            Assert.Null(s[p].KmPerLiter);
            Assert.Equal(12.5, s[a].KmPerLiter);
        }

        [Fact]
        public void Summary_AveragesOnlyFullToFullSegments()
        {
            var records = new[]
            {
                Fill(1000, 30, 900),           // 起點（加的油不算進平均）
                Fill(1500, 40, 1200, day: 2),  // 500 km / 40 L
                Fill(2100, 40, 1240, day: 3),  // 600 km / 40 L
            };

            var sum = FuelCalculator.Summarize(records);

            Assert.Equal(3, sum.Count);
            Assert.Equal(3340m, sum.TotalAmount);
            Assert.Equal(110, sum.TotalLiters);
            Assert.Equal(1100.0 / 80, sum.AverageKmPerLiter!.Value, 6);
            Assert.Equal(1100, sum.TrackedDistance);
            Assert.Equal(2440m / 1100m, sum.CostPerKm!.Value, 6);
        }

        [Fact]
        public void Summary_WithFewerThanTwoFullFills_HasNoAverage()
        {
            var sum = FuelCalculator.Summarize(new[] { Fill(1000, 30, 900), Fill(1300, 10, 300, full: false, day: 2) });

            Assert.Null(sum.AverageKmPerLiter);
            Assert.Null(sum.CostPerKm);
            Assert.Equal(1200m, sum.TotalAmount);
        }

        [Fact]
        public void Summary_Empty()
        {
            var sum = FuelCalculator.Summarize(Array.Empty<FuelRecord>());

            Assert.Equal(0, sum.Count);
            Assert.Null(sum.AverageKmPerLiter);
        }

        [Fact]
        public void SameOdometer_DoesNotDivideByZeroOrGoNegative()
        {
            var a = Fill(1000, 40, 1200);
            var b = Fill(1000, 40, 1200, day: 2);

            var s = FuelCalculator.Calculate(new[] { a, b });

            Assert.Null(s[a].DistanceToNext);
            Assert.Null(s[a].KmPerLiter);
            Assert.Null(FuelCalculator.Summarize(new[] { a, b }).AverageKmPerLiter);
        }

        [Fact]
        public void UnitPrice_IsAmountPerLiter()
        {
            Assert.Equal(30m, Fill(0, 40, 1200).UnitPrice);
            Assert.Null(Fill(0, 0, 1200).UnitPrice);
        }
    }
}
