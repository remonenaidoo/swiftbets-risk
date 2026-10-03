using Akka.Actor;
using Akka.Event;
using SwiftBets.Risk.Application;
using SwiftBets.Risk.Domain;

namespace SwiftBets.Risk.Infrastructure.Actors;

/// <summary>
/// One fixture's liability. It loads its last snapshot and the journal after it on first use, journals each change
/// before replying, snapshots every so often, and publishes the new liability and, when the cap is crossed either way,
/// the exposure rule placement enforces. A failure to journal restarts it, which reloads the durable state.
/// </summary>
public sealed class FixtureActor : ReceiveActor
{
    private readonly string _fixtureId;
    private readonly IRiskJournal _journal;
    private readonly IRiskStore _store;
    private readonly IRiskPublisher _publisher;
    private readonly RiskOptions _options;
    private readonly TimeProvider _time;
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private FixtureBook? _book;
    private bool? _publishedSuspended;

    // The trader's cap, read once and again only when it changes, so a placement costs one journal write.
    private (bool Loaded, long? Override) _cap;

    public FixtureActor(string fixtureId, IRiskJournal journal, IRiskStore store, IRiskPublisher publisher, RiskOptions options, TimeProvider time)
    {
        (_fixtureId, _journal, _store, _publisher, _options, _time) = (fixtureId, journal, store, publisher, options, time);
        ReceiveAsync<PlaceOnFixture>(m => ChangeAsync(book => book.Place(m.Coupon), new JournalEntry(0, m.Coupon, null)));
        ReceiveAsync<SettleOnFixture>(m => ChangeAsync(book => book.Settle(m.CouponId), new JournalEntry(0, null, m.CouponId)));
        ReceiveAsync<CapChanged>(async _ =>
        {
            _cap = default;
            var view = await PublishAsync(await LoadAsync(), forceExposure: true);
            Sender.Tell(view);
        });
        ReceiveAsync<GetFixture>(async _ => Sender.Tell(await ViewAsync(await LoadAsync())));
    }

    public static Props Props(string fixtureId, IRiskJournal journal, IRiskStore store, IRiskPublisher publisher, RiskOptions options, TimeProvider time) =>
        Akka.Actor.Props.Create(() => new FixtureActor(fixtureId, journal, store, publisher, options, time));

    private async Task ChangeAsync(Func<FixtureBook, bool> change, JournalEntry entry)
    {
        var sender = Sender;
        try
        {
            var book = await LoadAsync();
            if (!change(book))
            {
                sender.Tell(new Applied(false));
                return;
            }

            await _journal.AppendAsync(_fixtureId, entry with { Version = book.Version }, CancellationToken.None);
            if (book.Version % _options.SnapshotEvery == 0)
            {
                await _journal.SaveSnapshotAsync(_fixtureId, book.Snapshot(), CancellationToken.None);
            }

            await PublishAsync(book, forceExposure: false);
            sender.Tell(new Applied(true));
        }
        catch (Exception ex)
        {
            sender.Tell(new Status.Failure(ex));
            throw;
        }
    }

    private async Task<FixtureView> PublishAsync(FixtureBook book, bool forceExposure)
    {
        var view = await ViewAsync(book);
        await Task.WhenAll(_store.SaveViewAsync(view, CancellationToken.None), _publisher.LiabilityAsync(view, CancellationToken.None));
        if (forceExposure || _publishedSuspended != view.Suspended)
        {
            var reason = view.Suspended
                ? view.CapMinor == 0 ? "Suspended by a trader." : $"Worst-case liability {view.WorstCaseMinor} reached the cap {view.CapMinor}."
                : "Within its cap.";
            await _publisher.ExposureAsync(view, reason, CancellationToken.None);
            _publishedSuspended = view.Suspended;
            _log.Info("Fixture {0} exposure: suspended={1}, worst case {2}, cap {3}", _fixtureId, view.Suspended, view.WorstCaseMinor, view.CapMinor);
        }

        return view;
    }

    private async Task<FixtureView> ViewAsync(FixtureBook book)
    {
        if (!_cap.Loaded)
        {
            _cap = (true, await _store.CapAsync(_fixtureId, CancellationToken.None));
        }

        var overridden = _cap.Override;
        var cap = overridden ?? _options.DefaultFixtureCapMinor;
        var worst = book.WorstCaseMinor;
        return new FixtureView(_fixtureId, book.Version, worst, cap, overridden is not null, cap == 0 || worst >= cap, book.Totals(), _time.GetUtcNow());
    }

    private async Task<FixtureBook> LoadAsync()
    {
        if (_book is not null)
        {
            return _book;
        }

        var (snapshot, after) = await _journal.LoadAsync(_fixtureId, CancellationToken.None);
        var book = snapshot is null ? new FixtureBook() : FixtureBook.Restore(snapshot);
        foreach (var entry in after)
        {
            if (entry.Placed is { } placed)
            {
                book.Place(placed);
            }
            else if (entry.SettledCouponId is { } settled)
            {
                book.Settle(settled);
            }
        }

        _log.Info("Fixture {0} recovered at version {1} ({2} from the snapshot, {3} replayed)", _fixtureId, book.Version, snapshot?.Version ?? 0, after.Count);
        return _book = book;
    }
}
