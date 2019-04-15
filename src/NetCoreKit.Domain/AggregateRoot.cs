using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using static NetCoreKit.Utils.Helpers.IdHelper;
using static NetCoreKit.Utils.Helpers.DateTimeHelper;

namespace NetCoreKit.Domain
{
    public interface IAggregateRoot : IAggregateRootWithType<Guid>
    {
    }

    public interface IAggregateRootWithType<TId> : IEntityWithId<TId>
    {
        IAggregateRootWithType<TId> ApplyEvent(IEvent payload);
        List<IEvent> GetUncommittedEvents();
        void ClearUncommittedEvents();
        IAggregateRootWithType<TId> RemoveEvent(IEvent @event);
        IAggregateRootWithType<TId> AddEvent(IEvent uncommittedEvent);
        IAggregateRootWithType<TId> RegisterHandler<T>(Action<T> handler);
    }

    public abstract class AggregateRootBase : AggregateRootWithIdBase<Guid>, IAggregateRoot
    {
        protected AggregateRootBase() : base(GenerateId())
        {
        }
    }

    public abstract class AggregateRootWithIdBase<TId> : EntityWithIdBase<TId>, IAggregateRootWithType<TId>
    {
        private readonly IDictionary<Type, Action<object>> _handlers = new ConcurrentDictionary<Type, Action<object>>();
        private readonly List<IEvent> _uncommittedEvents = new List<IEvent>();

        protected AggregateRootWithIdBase(TId id) : base(id)
        {
            Created = GenerateDateTime();
        }

        public int Version { get; protected set; }

        public IAggregateRootWithType<TId> AddEvent(IEvent uncommittedEvent)
        {
            _uncommittedEvents.Add(uncommittedEvent);
            ApplyEvent(uncommittedEvent);
            return this;
        }

        public IAggregateRootWithType<TId> ApplyEvent(IEvent payload)
        {
            if (!_handlers.ContainsKey(payload.GetType()))
                return this;
            _handlers[payload.GetType()]?.Invoke(payload);
            Version++;
            return this;
        }

        public void ClearUncommittedEvents()
        {
            _uncommittedEvents.Clear();
        }

        public List<IEvent> GetUncommittedEvents()
        {
            return _uncommittedEvents;
        }

        public IAggregateRootWithType<TId> RegisterHandler<T>(Action<T> handler)
        {
            _handlers.Add(typeof(T), e => handler((T)e));
            return this;
        }

        public IAggregateRootWithType<TId> RemoveEvent(IEvent @event)
        {
            if (_uncommittedEvents.Find(e => e == @event) != null)
                _uncommittedEvents.Remove(@event);
            return this;
        }
    }
}
