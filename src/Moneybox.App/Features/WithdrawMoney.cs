using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moneybox.App.DataAccess;
using Moneybox.App.Domain.Services;
using System;
using System.Transactions;

namespace Moneybox.App.Features
{ 
    /// <summary>
    /// WithdrawMoney feature handles the withdrawal of funds from a user's account. It ensures that the withdrawal amount is valid, checks for sufficient funds, and updates the account balance accordingly. If the account balance falls below a specified threshold after the withdrawal, it sends a notification to the user about low funds. The feature also implements retry logic to handle concurrency conflicts when updating the account in the database.
    /// </summary>
    public sealed class WithdrawMoney
    {
        private readonly IAccountRepository accountRepository;
        private readonly INotificationService notificationService;
        private readonly ILogger<WithdrawMoney> logger;
        private const decimal FundsLowLimitNotification = 500m;
        /// <summary>
        /// Initializes a new instance of the WithdrawMoney class with the specified account repository, notification service, and logger. Validates that none of the dependencies are null and throws an ArgumentNullException if any are missing.
        /// </summary>
        /// <param name="accountRepository"></param>
        /// <param name="notificationService"></param>
        /// <param name="logger"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public WithdrawMoney(
            IAccountRepository accountRepository,
            INotificationService notificationService,
            ILogger<WithdrawMoney> logger)
        {
            this.accountRepository = accountRepository ?? throw new ArgumentNullException(nameof(accountRepository));
            this.notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
    /// <summary>
    /// Executes the withdrawal process for a specified account and amount. Validates the input, retrieves the account, performs the withdrawal, updates the account in the repository, and handles notifications if the balance is low. Implements retry logic for concurrency conflicts during database updates.
    /// </summary>
    /// <param name="fromAccountId"></param>
    /// <param name="amount"></param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    /// <exception cref="InvalidOperationException"></exception>
    public void Execute(Guid fromAccountId, decimal amount)
        {
            if (amount <= 0m) throw new ArgumentOutOfRangeException(nameof(amount), "Withdrawal amount must be greater than zero.");
            const int maxRetries = 3;
            int attempt = 0;

            while (true)
            {
                attempt++;
                logger.LogDebug("Starting withdrawal attempt {Attempt} for account {AccountId} amount {Amount}.", attempt, fromAccountId, amount);

                using var scope = new TransactionScope();
                var from = accountRepository.GetAccountById(fromAccountId);
                if (from == null)
                {
                    logger.LogError("Account not found for id: {AccountId}", fromAccountId);
                    throw new InvalidOperationException($"Account not found for id: {fromAccountId}");
                }

                try
                {
                    from.Withdraw(amount);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Domain error while withdrawing {Amount} from account {AccountId}.", amount, fromAccountId);
                    throw;
                }

                try
                {
                    var expectedBalance = from.Balance;
                    accountRepository.Update(from);

                    // Re-query accounts to verify the persistence resulted in the expected balances.
                    // This is a runtime check to detect lost-update 
                    var verificationFrom = accountRepository.GetAccountById(fromAccountId)
                                          ?? throw new InvalidOperationException("Source account not found after update verification.");

                    if (verificationFrom.Balance != expectedBalance)
                    {
                        logger.LogWarning("Concurrent modification detected on attempt {Attempt} Withdrawing from Account {From}. Expected balance {Expected}, observed {Observed}.",
                            attempt, fromAccountId, expectedBalance, verificationFrom.Balance);

                        // Treat this as a concurrency/race condition to enable retry
                        throw new InvalidOperationException("Concurrent modification detected; the withdrawal could not be completed. Please try again.");
                    }
                    var email = from.User?.Email;
                    if (from.Balance < FundsLowLimitNotification && !string.IsNullOrWhiteSpace(email))
                    {
                        logger.LogInformation("Balance {Balance} below threshold after withdrawal for account {AccountId}. Sending funds low notification to {Email}.",
                            from.Balance, fromAccountId, email);
                        notificationService.NotifyFundsLow(email);
                    }

                    logger.LogInformation("Withdrawal of {Amount} from account {AccountId} completed successfully. New balance: {Balance}.",
                        amount, fromAccountId, from.Balance);

                    // Commit transaction
                    scope.Complete();

                    // Success
                    return;
                }
                catch (Exception ex) when (ex is DbUpdateConcurrencyException ||
                (ex is InvalidOperationException && ex.Message.Contains("Concurrent")))
                {
                    logger.LogWarning(ex, "Concurrency conflict during withdrawal attempt {Attempt} for account {AccountId}. Retrying (max {MaxRetries}).",
                        attempt, fromAccountId, maxRetries);
                    continue;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Unexpected error during withdrawal attempt {Attempt} for account {AccountId}.", attempt, fromAccountId);
                    throw;
                }
            }
        }
    }
}
