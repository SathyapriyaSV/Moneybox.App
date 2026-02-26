using Microsoft.EntityFrameworkCore;
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
    public class TransferMoneyTests
    {

        [Fact]
        public async Task Execute_WhenInsufficientFunds_Throws()
        {
            var from = new Account { Id = Guid.NewGuid(), Balance = 100m, Withdrawn = 0m, PaidIn = 0m, User = new User { Email = "from@example.com" } };
            var to = new Account { Id = Guid.NewGuid(), Balance = 0m, Withdrawn = 0m, PaidIn = 0m, User = new User { Email = "to@example.com" } };

            var repo = new FakeAccountRepository();
            repo.Add(from);
            repo.Add(to);

            var notifications = new FakeNotificationService();

            var logger = new Mock<ILogger<TransferMoney>>();
            var sut = new TransferMoney(repo, notifications, logger.Object);

            Assert.Throws<InvalidOperationException>(() => sut.Execute(from.Id, to.Id, 200m));
        }

        [Fact]
        public void Execute_WhenFromBalanceGoesBelowThreshold_NotifiesFundsLow()
        {
            var from = new Account { Id = Guid.NewGuid(), Balance = 600m, Withdrawn = 0m, PaidIn = 0m, User = new User { Email = "from@example.com" } };
            var to = new Account { Id = Guid.NewGuid(), Balance = 0m, Withdrawn = 0m, PaidIn = 0m, User = new User { Email = "to@example.com" } };

            var repo = new FakeAccountRepository();
            repo.Add(from);
            repo.Add(to);

            var notifications = new FakeNotificationService();

            var logger = new Mock<ILogger<TransferMoney>>();
            var sut = new TransferMoney(repo, notifications, logger.Object);

            sut.Execute(from.Id, to.Id, 200m);

            Assert.True(notifications.FundsLowNotified);
            Assert.Equal(from.User.Email, notifications.LastEmail);
        }

        [Fact]
        public void Execute_WhenToApproachingPayInLimit_NotifiesApproachingPayInLimit()
        {
            var from = new Account { Id = Guid.NewGuid(), Balance = 1000m, Withdrawn = 0m, PaidIn = 0m, User = new User { Email = "from@example.com" } };
            // set PaidIn so that after transfer the remaining pay in is < 500
            var to = new Account { Id = Guid.NewGuid(), Balance = 0m, Withdrawn = 0m, PaidIn = 3600m, User = new User { Email = "to@example.com" } };

            var repo = new FakeAccountRepository();
            repo.Add(from);
            repo.Add(to);

            var notifications = new FakeNotificationService();

            var logger = new Mock<ILogger<TransferMoney>>();
            var sut = new TransferMoney(repo, notifications, logger.Object);

            sut.Execute(from.Id, to.Id, 200m);

            Assert.True(notifications.ApproachingPayInLimitNotified);
            Assert.Equal(to.User.Email, notifications.LastEmail);
        }

        [Fact]
        public void Execute_PerformsTransferAndUpdatesAccounts()
        {
            var from = new Account { Id = Guid.NewGuid(), Balance = 1000m, Withdrawn = 50m, PaidIn = 0m, User = new User { Email = "from@example.com" } };
            var to = new Account { Id = Guid.NewGuid(), Balance = 100m, Withdrawn = 0m, PaidIn = 100m, User = new User { Email = "to@example.com" } };

            var repo = new FakeAccountRepository();
            repo.Add(from);
            repo.Add(to);

            var notifications = new FakeNotificationService();

            var logger = new Mock<ILogger<TransferMoney>>();
            var sut = new TransferMoney(repo, notifications, logger.Object);

            // Perform the transfer
            sut.Execute(from.Id, to.Id, 200m);

            // Two updates expected: one for from and one for to
            Assert.Equal(2, repo.Updated.Count);

            // Verify balances and paid in/withdrawn updated according to current implementation
            Assert.Equal(800m, from.Balance);
            Assert.Equal(250m, from.Withdrawn);

            Assert.Equal(300m, to.Balance);
            Assert.Equal(300m, to.PaidIn);
        }

    }
}
