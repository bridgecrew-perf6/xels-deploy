﻿using System;

namespace Xels.Bitcoin.EventBus
{
    /// <summary>
    /// Basic abstract implementation of <see cref="IEvent"/>.
    /// </summary>
    /// <seealso cref="Xels.Bitcoin.EventBus.IEvent" />
    public abstract class EventBase
    {
        /// <inheritdoc />
        public Guid CorrelationId { get; }

        public EventBase()
        {
            // Assigns an unique id to the event.
            this.CorrelationId = Guid.NewGuid();
        }

        public override string ToString()
        {
            return $"{this.CorrelationId.ToString()} - {this.GetType().Name}";
        }
    }
}
