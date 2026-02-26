using Moneybox.App.DataAccess;
using System.Data;

namespace Moneybox.App.Tests.Shared
{
    public class FakeAccountRepository : IAccountRepository
    {
        private readonly Dictionary<Guid, Account> accounts = new();

        public List<Account> Updated { get; } = new();
        public Guid ThrowOnUpdateId { get; internal set; }

        // Add an account to the in-memory store
        public void Add(Account account)
        {
            accounts[account.Id] = account;
        }

        // Return the stored account
        public Account GetAccountById(Guid accountId)
        {
            return accounts[accountId];
        }

        public void Update(Account account)
        {
            if (ThrowOnUpdateId != Guid.Empty && account.Id == ThrowOnUpdateId)
            {
                throw new DBConcurrencyException("Forced concurrency exception for testing.");
            }

            if (!accounts.ContainsKey(account.Id))
            {
                throw new InvalidOperationException("Account not found in fake repository.");
            }

            // Replace stored account with incoming account to simulate persistence
            accounts[account.Id] = account;

            Updated.Add(account);
        }

        public void Reset() => Updated.Clear();
    }
}
