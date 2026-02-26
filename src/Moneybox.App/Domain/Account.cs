using System;
using System.ComponentModel.DataAnnotations;

namespace Moneybox.App
{
    public class Account
    {
        public const decimal PayInLimit = 4000m;

        public Guid Id { get; set; }

        public User User { get; set; }

        [ConcurrencyCheck]
        public decimal Balance { get; set; }

        public decimal Withdrawn { get; set; }

        public decimal PaidIn { get; set; }

        public void Withdraw(decimal amount)
        {
            if (Balance < amount) throw new InvalidOperationException("Insufficient funds to make transfer");
            Balance -= amount;
            Withdrawn += amount;  //Changed this - Money Withdrawn should be increased by the amount withdrawn
        }

        public void Deposit(decimal amount)
        {
            if (PaidIn + amount > PayInLimit) throw new InvalidOperationException("Account pay in limit reached");
            Balance += amount;
            PaidIn += amount;
        }
    }
}
