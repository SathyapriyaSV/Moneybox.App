using System;
using System.Transactions;
using System.Threading;
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moneybox.App.DataAccess;
using Moneybox.App.Domain.Services;

namespace Moneybox.App.Features
{
    /// <summary>
    /// TransferMoney feature handles the transfer of funds between two user accounts. It ensures that the transfer amount is valid, checks for sufficient funds in the source account, and updates both accounts accordingly. If the source account balance falls below a specified threshold after the transfer, it sends a notification to the user about low funds. If the destination account is approaching its pay-in limit, it sends a notification to that user as well. The feature implements retry logic to handle concurrency conflicts when updating the accounts in the database, ensuring data integrity in concurrent scenarios.
    /// </summary>
    public sealed class TransferMoney
    {
        private readonly IAccountRepository accountRepository;
        private readonly INotificationService notificationService;
        private readonly ILogger<TransferMoney> logger;

        private const decimal FundsLowLimitNotification = 500m;
        private const decimal PayInLimitNotification = 500m;
        /// <summary>
        /// TransferMoney constructor initializes the feature with the necessary dependencies: an account repository for data access, a notification service for sending alerts to users, and a logger for recording the operation's progress and any issues that arise. It validates that none of the dependencies are null, throwing an ArgumentNullException if any are missing, ensuring that the feature is properly configured before use.
        /// </summary>
        /// <param name="accountRepository"></param>
        /// <param name="notificationService"></param>
        /// <param name="logger"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public TransferMoney(
            IAccountRepository accountRepository,
            INotificationService notificationService,
            ILogger<TransferMoney> logger)
        {
            this.accountRepository = accountRepository ?? throw new ArgumentNullException(nameof(accountRepository));
            this.notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        /// <summary>
        /// Executes the transfer of funds from one account to another. Validates the transfer amount and account IDs, checks for sufficient funds, and updates the accounts within a transaction scope. Implements retry logic to handle concurrency conflicts, and sends notifications if the source account balance is low or if the destination account is approaching its pay-in limit. Logs all significant steps and errors throughout the process.
        /// </summary>
        /// <param name="fromAccountId"></param>
        /// <param name="toAccountId"></param>
        /// <param name="amount"></param>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        public void Execute(Guid fromAccountId, Guid toAccountId, decimal amount)
        {
            const int maxRetries = 3;

            logger.LogDebug("Initiating transfer from {FromAccountId} to {ToAccountId} of amount {Amount}.", fromAccountId, toAccountId, amount);

            if (amount <= 0)
            {
                logger.LogError("Invalid transfer amount: {Amount}. Amount must be greater than zero.", amount);
                throw new ArgumentException("Transfer amount must be greater than zero.", nameof(amount));
            }

            if (fromAccountId == toAccountId)
            {
                logger.LogError("Transfer attempted between the same account {AccountId}.", fromAccountId);
                throw new InvalidOperationException("Cannot transfer to the same account.");
            }

            // Flags and emails captured so notifications are sent only after successful commit
            var shouldNotifyFundsLow = false;
            var shouldNotifyApproachingPayInLimit = false;
            string? fromEmail = null;
            string? toEmail = null;

            var attempt = 0;
            while (true)
            {
                try
                {
                    attempt++;
                    logger.LogDebug("Transfer attempt {Attempt} for {FromAccountId}->{ToAccountId}.", attempt, fromAccountId, toAccountId);

                    using var scope = new TransactionScope();
                    // Load accounts
                    var from = accountRepository.GetAccountById(fromAccountId)
                               ?? throw new InvalidOperationException("Source account not found.");

                    var to = accountRepository.GetAccountById(toAccountId)
                             ?? throw new InvalidOperationException("Destination account not found.");

                    if (from.Balance < amount)
                    {
                        logger.LogWarning("Insufficient funds for transfer from {FromAccountId}. Requested {Amount}, Available {Balance}.", fromAccountId, amount, from.Balance);
                        throw new InvalidOperationException("Insufficient funds in source account.");
                    }

                    // Record expected balances for post-update verification
                    var expectedFromBalance = from.Balance - amount;
                    var expectedToBalance = to.Balance + amount;

                    // Perform domain operations
                    from.Withdraw(amount);
                    to.Deposit(amount);

                    // Persist changes via repository
                    accountRepository.Update(from);
                    accountRepository.Update(to);

                    // Re-query accounts to verify the persistence resulted in the expected balances.
                    // This is a runtime check to detect lost-update 
                    var verificationFrom = accountRepository.GetAccountById(fromAccountId)
                                          ?? throw new InvalidOperationException("Source account not found after update verification.");

                    var verificationTo = accountRepository.GetAccountById(toAccountId)
                                        ?? throw new InvalidOperationException("Destination account not found after update verification.");

                    if (verificationFrom.Balance != expectedFromBalance || verificationTo.Balance != expectedToBalance)
                    {
                        logger.LogWarning("Concurrent modification detected on attempt {Attempt} for transfer {From}->{To}. Expected balances {ExpectedFrom}/{ExpectedTo}, observed {ObservedFrom}/{ObservedTo}.",
                            attempt, fromAccountId, toAccountId, expectedFromBalance, expectedToBalance, verificationFrom.Balance, verificationTo.Balance);

                        // Treat this as a concurrency/race condition to enable retry
                        throw new InvalidOperationException("Concurrent modification detected; the transfer could not be completed. Please try again.");
                    }

                    // Prepare notifications based on the domain state (use the updated balances)
                    shouldNotifyFundsLow = verificationFrom.Balance < FundsLowLimitNotification;
                    shouldNotifyApproachingPayInLimit = Account.PayInLimit - verificationTo.PaidIn < PayInLimitNotification;

                    fromEmail = verificationFrom.User?.Email;
                    toEmail = verificationTo.User?.Email;

                    // Commit transaction
                    scope.Complete();

                    logger.LogInformation("Transfer completed successfully on attempt {Attempt} from {From} to {To} of amount {Amount}.", attempt, fromAccountId, toAccountId, amount);

                    // Success — break out of retry loop and send notifications below
                    break;
                }
                catch (Exception ex) when (ex is DbUpdateConcurrencyException ||
                (ex is InvalidOperationException && ex.Message.Contains("Concurrent")))
                {
                    logger.LogWarning(ex, "Concurrency conflict detected on attempt {Attempt} for transfer {From}->{To}.", attempt, fromAccountId, toAccountId);

                    if (attempt >= maxRetries)
                    {
                        logger.LogError(ex, "Max retry attempts ({MaxRetries}) reached for transfer {From}->{To}. Failing operation.", maxRetries, fromAccountId, toAccountId);
                        throw new InvalidOperationException("The account was modified by another transaction. Please try again.", ex);
                    }

                    // small backoff before retrying
                    Thread.Sleep(50 * attempt);
                    continue;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Unexpected error during transfer attempt {Attempt} for {From}->{To}.", attempt, fromAccountId, toAccountId);
                    // Terminal error — rethrow
                    throw;
                }
            }

            if (shouldNotifyFundsLow && !string.IsNullOrWhiteSpace(fromEmail))
            {
                try
                {
                    notificationService.NotifyFundsLow(fromEmail!);
                    logger.LogInformation("Funds low notification sent to {Email} for account {From}.", fromEmail, fromAccountId);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to send funds low notification to {Email} for account {From}.", fromEmail, fromAccountId);
                }
            }

            if (shouldNotifyApproachingPayInLimit && !string.IsNullOrWhiteSpace(toEmail))
            {
                try
                {
                    notificationService.NotifyApproachingPayInLimit(toEmail!);
                    logger.LogInformation("Approaching pay-in limit notification sent to {Email} for account {To}.", toEmail, toAccountId);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to send approaching pay-in limit notification to {Email} for account {To}.", toEmail, toAccountId);
                }
            }
        }
    }
}
