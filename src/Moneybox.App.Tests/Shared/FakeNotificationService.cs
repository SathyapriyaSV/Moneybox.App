using Moneybox.App.Domain.Services;

namespace Moneybox.App.Tests.Shared
{
    public class FakeNotificationService : INotificationService
    {
        public bool FundsLowNotified { get; private set; }
        public bool ApproachingPayInLimitNotified { get; private set; }
        public string? LastEmail { get; private set; }

        public void NotifyApproachingPayInLimit(string emailAddress)
        {
            ApproachingPayInLimitNotified = true;
            LastEmail = emailAddress;
        }

        public void NotifyFundsLow(string emailAddress)
        {
            FundsLowNotified = true;
            LastEmail = emailAddress;
        }

        public void Reset()
        {
            FundsLowNotified = false;
            ApproachingPayInLimitNotified = false;
            LastEmail = "email@test.com";
        }
    }
}
