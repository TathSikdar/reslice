using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace InterviewTrea.Core.Measurements;

/// <summary>
/// Read access to the measurements a session has made (FR-503).
/// </summary>
/// <remarks>
/// <para>
/// Read-only on purpose. A clinical application is a guest in the viewer: it may look at
/// what the user has measured - a calcium scoring tool wants the ROIs someone drew - but
/// nothing outside the shell gets to add to or delete from that list. Handing a plugin
/// something it could mutate would make the viewer's own state everyone's problem.
/// </para>
/// <para>
/// <see cref="INotifyCollectionChanged"/> rather than a bespoke event, because that is the
/// interface the list already raises and a plugin can bind to it directly. It lives in
/// System.Collections.Specialized, which is the base library and not WPF, so Core stays
/// free of any presentation dependency.
/// </para>
/// </remarks>
public interface IMeasurementStore : IReadOnlyList<Measurement>, INotifyCollectionChanged
{
}

/// <summary>
/// The shell's measurement list, and the only implementation of
/// <see cref="IMeasurementStore"/>.
/// </summary>
/// <remarks>
/// <see cref="ObservableCollection{T}"/> already is a read-only list that raises collection
/// changes, so this exists only to name that fact as the contract. An adapter that copied
/// the list to satisfy the interface would be a second copy to keep in step, and one that
/// wrapped it would be indirection for its own sake.
/// </remarks>
public sealed class MeasurementStore : ObservableCollection<Measurement>, IMeasurementStore
{
}
