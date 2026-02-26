using System.ComponentModel.DataAnnotations;

namespace Moneybox.App.Tests
{
    public class AccountTests
    {
        private static Account CreateAccount(decimal balance = 0m, decimal paidIn = 0m, decimal withdrawn = 0m)
        {
            return new Account
            {
                Id = Guid.NewGuid(),
                User = new User { Id = Guid.NewGuid(), Name = "Test User" },
                Balance = balance,
                PaidIn = paidIn,
                Withdrawn = withdrawn
            };
        }

        [Fact]
        public void Withdraw_DecreasesBalanceAndIncreasesWithdrawn()
        {
            var account = CreateAccount(balance: 100m, withdrawn: 0m);

            account.Withdraw(40m);

            Assert.Equal(60m, account.Balance);
            Assert.Equal(40m, account.Withdrawn);
        }

        [Fact]
        public void Withdraw_ThrowsWhenInsufficientFunds()
        {
            var account = CreateAccount(balance: 10m);

            Assert.Throws<InvalidOperationException>(() => account.Withdraw(20m));
        }

        [Fact]
        public void Deposit_IncreasesBalanceAndPaidIn()
        {
            var account = CreateAccount(balance: 10m, paidIn: 0m);

            account.Deposit(100m);

            Assert.Equal(110m, account.Balance);
            Assert.Equal(100m, account.PaidIn);
        }

        [Fact]
        public void Deposit_ThrowsWhenPayInLimitExceeded()
        {
            var account = CreateAccount(paidIn: Account.PayInLimit - 50m);

            // This deposit will take PaidIn over the PayInLimit
            Assert.Throws<InvalidOperationException>(() => account.Deposit(100m));
        }

        [Fact]
        public void Balance_HasConcurrencyCheckAttribute()
        {
            var balanceProp = typeof(Account).GetProperty(nameof(Account.Balance));
            Assert.NotNull(balanceProp);

            var hasAttribute = balanceProp.GetCustomAttributes(typeof(ConcurrencyCheckAttribute), inherit: false)
                                          .Any();
            Assert.True(hasAttribute, "Balance property should have ConcurrencyCheckAttribute applied.");
        }
    }
}