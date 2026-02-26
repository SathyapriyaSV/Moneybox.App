using Microsoft.Extensions.Logging;
using Moneybox.App;
using Moneybox.App.DataAccess;
using Moneybox.App.Domain.Services;
using Moneybox.App.Features;
using Moneybox.App.Tests.Shared;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Moneybox.App.Tests
{
    public class WithdrawMoneyTests
    {
        [Fact]
        public void Execute_WhenInsufficientFunds_Throws()
        {
            var from = new Account { Id = Guid.NewGuid(), Balance = 100m, Withdrawn = 0m, PaidIn = 0m, User = new User { Email = "from@example.com" } };

            var repo = new FakeAccountRepository();
            repo.Add(from);

            var notifications = new FakeNotificationService();
            var logger = new Mock<ILogger<WithdrawMoney>>();
            var sut = new WithdrawMoney(repo, notifications, logger.Object);

            Assert.Throws<InvalidOperationException>(() => sut.Execute(from.Id, 200m));
        }

        [Fact]
        public void Execute_WhenBalanceGoesBelowThreshold_NotifiesFundsLow()
        {
            var from = new Account { Id = Guid.NewGuid(), Balance = 600m, Withdrawn = 0m, PaidIn = 0m, User = new User { Email = "from@example.com" } };

            var repo = new FakeAccountRepository();
            repo.Add(from);

            var notifications = new FakeNotificationService();

            var logger = new Mock<ILogger<WithdrawMoney>>();
            var sut = new WithdrawMoney(repo, notifications, logger.Object);

            sut.Execute(from.Id, 200m);

            Assert.True(notifications.FundsLowNotified);
            Assert.Equal(from.User.Email, notifications.LastEmail);
        }

        [Fact]
        public void Execute_PerformsWithdrawalAndUpdatesAccount()
        {
            var from = new Account { Id = Guid.NewGuid(), Balance = 1000m, Withdrawn = 50m, PaidIn = 0m, User = new User { Email = "from@example.com" } };

            var repo = new FakeAccountRepository();
            repo.Add(from);

            var notifications = new FakeNotificationService();
            var logger = new Mock<ILogger<WithdrawMoney>>();
            var sut = new WithdrawMoney(repo, notifications, logger.Object);

            sut.Execute(from.Id, 200m);

            Assert.Single(repo.Updated);

            // Expected behavior: balance decreased by amount and withdrawn increased by amount
            Assert.Equal(800m, from.Balance);
            Assert.Equal(250m, from.Withdrawn);
        }  
      
    }
}
