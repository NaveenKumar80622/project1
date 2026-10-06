using System.Collections.Generic;

namespace PickNBook.Api.Models.Config
{
    public class SrdvWalletMonitoringSettings
    {
        public bool Enabled { get; set; } = true;
        public decimal LowBalanceThreshold { get; set; } = 5000.00m;
        public int CheckIntervalMinutes { get; set; } = 15;
        public int AlertCooldownMinutes { get; set; } = 60;
        public List<string> AdminAlertEmails { get; set; } = new();
    }
}
