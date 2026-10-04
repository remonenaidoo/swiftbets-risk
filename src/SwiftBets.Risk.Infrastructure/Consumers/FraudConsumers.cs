using SwiftBets.BuildingBlocks.Messaging;
using SwiftBets.Contracts.Payments;
using SwiftBets.Risk.Application;

namespace SwiftBets.Risk.Infrastructure.Consumers;

/// <summary>Deposits count toward the deposit-then-withdraw rule.</summary>
public sealed class FraudDepositConsumer(FraudHandler fraud) : IEventHandler<DepositSucceededV1>
{
    public Task HandleAsync(ConsumedEvent<DepositSucceededV1> message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var deposit = message.Envelope.Payload;
        return fraud.DepositAsync(deposit.UserId, deposit.PaymentId, deposit.Amount.MinorUnits, deposit.CompletedAt, cancellationToken);
    }
}

/// <summary>A withdrawal request runs the deposit-then-withdraw rule.</summary>
public sealed class FraudWithdrawalConsumer(FraudHandler fraud) : IEventHandler<WithdrawalRequestedV1>
{
    public Task HandleAsync(ConsumedEvent<WithdrawalRequestedV1> message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var withdrawal = message.Envelope.Payload;
        return fraud.WithdrawalAsync(withdrawal.UserId, withdrawal.WithdrawalId, withdrawal.Amount.MinorUnits, withdrawal.RequestedAt, cancellationToken);
    }
}
