using car.Models;
using car.Services;

namespace car.Tests
{
    /// <summary>保養統計與里程檢查的自動測試。</summary>
    public class MaintenanceCalculatorTests
    {
        private static MaintenanceRecord Service(double odo, decimal amount, double? next = null, int month = 1) =>
            new() { Date = new DateTime(2026, month, 1), Odometer = odo, Items = "機油", Amount = amount, NextOdometer = next };

        [Fact]
        public void Summary_TotalsAndLatest()
        {
            var a = Service(5000, 1500, next: 10000, month: 1);
            var b = Service(10200, 2500, next: 15200, month: 6);

            var s = MaintenanceCalculator.Summarize(new[] { b, a }, currentOdometer: 12000);

            Assert.Equal(2, s.Count);
            Assert.Equal(4000m, s.TotalAmount);
            Assert.Same(b, s.Latest);
            Assert.Equal(15200, s.NextDueOdometer);
            Assert.Equal(3200, s.RemainingKm);
        }

        [Fact]
        public void Remaining_IsNegativeWhenOverdue()
        {
            var s = MaintenanceCalculator.Summarize(new[] { Service(5000, 1500, next: 10000) }, currentOdometer: 10300);

            Assert.Equal(-300, s.RemainingKm);
        }

        [Fact]
        public void NextDue_UsesLatestRecordThatHasIt()
        {
            // 最後一次是小修理沒填下次保養，仍沿用前一次保養填的
            var a = Service(5000, 1500, next: 10000, month: 1);
            var repair = Service(7000, 800, next: null, month: 3);

            var s = MaintenanceCalculator.Summarize(new[] { a, repair }, currentOdometer: 7000);

            Assert.Same(repair, s.Latest);
            Assert.Equal(10000, s.NextDueOdometer);
            Assert.Equal(3000, s.RemainingKm);
        }

        [Fact]
        public void Remaining_IsNullWithoutNextOrCurrent()
        {
            Assert.Null(MaintenanceCalculator.Summarize(new[] { Service(5000, 1) }, 6000).RemainingKm);
            Assert.Null(MaintenanceCalculator.Summarize(new[] { Service(5000, 1, next: 10000) }, null).RemainingKm);
            Assert.Null(MaintenanceCalculator.Summarize(Array.Empty<MaintenanceRecord>(), 100).Latest);
        }

        [Fact]
        public void CurrentOdometer_IsHighestOfBothKinds()
        {
            var fuel = new[] { new FuelRecord { Odometer = 12000 } };
            var maint = new[] { Service(12500, 1) };

            Assert.Equal(12500, OdometerCheck.Current(fuel, maint));
            Assert.Null(OdometerCheck.Current(Array.Empty<FuelRecord>(), Array.Empty<MaintenanceRecord>()));
        }

        [Fact]
        public void FindConflict_FlagsEarlierDateWithHigherOdometer()
        {
            var others = new[] { (new DateTime(2026, 3, 1), 10000.0), (new DateTime(2026, 5, 1), 12000.0) };

            // 4 月卻只有 9,000 公里（比 3 月的 10,000 少）
            Assert.Equal((new DateTime(2026, 3, 1), 10000.0), OdometerCheck.FindConflict(new DateTime(2026, 4, 1), 9000, others));
            // 4 月卻有 13,000 公里（比 5 月的 12,000 多）
            Assert.Equal((new DateTime(2026, 5, 1), 12000.0), OdometerCheck.FindConflict(new DateTime(2026, 4, 1), 13000, others));
            // 合理
            Assert.Null(OdometerCheck.FindConflict(new DateTime(2026, 4, 1), 11000, others));
            // 同一天不同里程（一天加兩次油）不算矛盾
            Assert.Null(OdometerCheck.FindConflict(new DateTime(2026, 3, 1), 10300, others));
        }
    }
}
